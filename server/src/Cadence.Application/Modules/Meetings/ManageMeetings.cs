using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Meetings;

/// <summary>One meeting, with its bookmarks.</summary>
public sealed record GetMeetingQuery(Guid MeetingId) : IQuery<Result<MeetingDetailDto>>;

public sealed record CreateMeetingCommand(CreateMeetingRequest Meeting)
    : ICommand<Result<MeetingDetailDto>>;

public sealed record UpdateMeetingCommand(Guid MeetingId, UpdateMeetingRequest Meeting)
    : ICommand<Result<MeetingDetailDto>>;

public sealed record DeleteMeetingCommand(Guid MeetingId) : ICommand<Result>;

public sealed record DeleteMeetingsCommand(IReadOnlyList<Guid> Ids) : ICommand<Result<BulkResultDto>>;

internal sealed class CreateMeetingValidator : AbstractValidator<CreateMeetingCommand>
{
    public CreateMeetingValidator()
    {
        RuleFor(command => command.Meeting.Title)
            .NotEmpty().WithMessage("Give the meeting a title.")
            .MaximumLength(300);

        RuleFor(command => command.Meeting.EndsAt)
            .GreaterThan(command => command.Meeting.StartsAt)
            .WithMessage("End time must be after the start time.");

        RuleFor(command => command.Meeting.Platform).IsInEnum();
    }
}

internal sealed class UpdateMeetingValidator : AbstractValidator<UpdateMeetingCommand>
{
    public UpdateMeetingValidator()
    {
        RuleFor(command => command.Meeting.Title)
            .NotEmpty().WithMessage("Give the meeting a title.")
            .MaximumLength(300);

        RuleFor(command => command.Meeting.EndsAt)
            .GreaterThan(command => command.Meeting.StartsAt)
            .WithMessage("End time must be after the start time.");

        RuleFor(command => command.Meeting.Platform).IsInEnum();

        RuleFor(command => command.Meeting.MeetingUrl)
            .MaximumLength(2048)
            .Must(BeAnAbsoluteHttpUrl).WithMessage("The meeting link must be an absolute http(s) URL.")
            .When(command => !string.IsNullOrWhiteSpace(command.Meeting.MeetingUrl));
    }

    private static bool BeAnAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) && parsed.Scheme is "http" or "https";
}

public sealed class GetMeetingHandler(ICadenceDbContext context)
    : IQueryHandler<GetMeetingQuery, Result<MeetingDetailDto>>
{
    public async ValueTask<Result<MeetingDetailDto>> Handle(
        GetMeetingQuery query,
        CancellationToken cancellationToken) =>
        await MeetingReads.LoadDetailAsync(context, query.MeetingId, cancellationToken);
}

public sealed class CreateMeetingHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<CreateMeetingCommand, Result<MeetingDetailDto>>
{
    public async ValueTask<Result<MeetingDetailDto>> Handle(
        CreateMeetingCommand command,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.RequireOrganizationId();
        var organizerId = currentUser.RequireId();

        var meeting = Meeting.Schedule(
            organizationId,
            organizerId,
            command.Meeting.Title,
            command.Meeting.Description ?? string.Empty,
            command.Meeting.StartsAt,
            command.Meeting.EndsAt,
            command.Meeting.Platform,
            command.Meeting.Tags);

        var attendees = await MeetingParticipants.ResolveAsync(
            context,
            organizationId,
            // The organizer is always an attendee of their own meeting, whether or not the client
            // sent them — the create dialog does not even offer them in the picker.
            [.. command.Meeting.ParticipantIds, organizerId],
            cancellationToken);

        if (attendees.IsFailure)
        {
            return Result.Failure<MeetingDetailDto>(attendees.Error);
        }

        MeetingParticipants.Apply(meeting, organizerId, attendees.Value);

        await context.Meetings.AddAsync(meeting, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await MeetingReads.LoadDetailAsync(context, meeting.Id, cancellationToken);
    }
}

public sealed class UpdateMeetingHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<UpdateMeetingCommand, Result<MeetingDetailDto>>
{
    public async ValueTask<Result<MeetingDetailDto>> Handle(
        UpdateMeetingCommand command,
        CancellationToken cancellationToken)
    {
        var meeting = await context.Meetings
            .Include(candidate => candidate.Participants)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MeetingId, cancellationToken);

        if (meeting is null)
        {
            return Result.Failure<MeetingDetailDto>(MeetingReads.NotFound);
        }

        try
        {
            meeting.UpdateDetails(
                command.Meeting.Title,
                command.Meeting.Description ?? string.Empty,
                command.Meeting.StartsAt,
                command.Meeting.EndsAt,
                command.Meeting.Platform,
                command.Meeting.Tags);
        }
        catch (DomainException exception)
        {
            return Result.Failure<MeetingDetailDto>(
                Error.Validation("meeting.invalid", exception.Message));
        }

        meeting.SetMeetingUrl(command.Meeting.MeetingUrl);

        // Null means "leave the attendee list alone"; an empty array means "remove everyone but the
        // organizer". Collapsing the two would make it impossible to edit a title without
        // resending the whole participant list.
        if (command.Meeting.ParticipantIds is { } participantIds)
        {
            var attendees = await MeetingParticipants.ResolveAsync(
                context,
                currentUser.RequireOrganizationId(),
                [.. participantIds, meeting.OrganizerId],
                cancellationToken);

            if (attendees.IsFailure)
            {
                return Result.Failure<MeetingDetailDto>(attendees.Error);
            }

            MeetingParticipants.Apply(meeting, meeting.OrganizerId, attendees.Value, context);
        }

        await context.SaveChangesAsync(cancellationToken);

        return await MeetingReads.LoadDetailAsync(context, meeting.Id, cancellationToken);
    }
}

public sealed class DeleteMeetingHandler(ICadenceDbContext context)
    : ICommandHandler<DeleteMeetingCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteMeetingCommand command,
        CancellationToken cancellationToken)
    {
        var meeting = await context.Meetings
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MeetingId, cancellationToken);

        if (meeting is null)
        {
            return Result.Failure(MeetingReads.NotFound);
        }

        await MeetingDeletion.DetachActionItemsAsync(context, [meeting.Id], cancellationToken);

        // Soft delete, applied by the auditing interceptor.
        context.Meetings.Remove(meeting);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class DeleteMeetingsHandler(ICadenceDbContext context)
    : ICommandHandler<DeleteMeetingsCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        DeleteMeetingsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        // The tenant filter is what makes this safe to take a list of ids from a client: an id from
        // another workspace simply does not come back, and the count reflects that rather than
        // reporting which ids were rejected.
        var meetings = await context.Meetings
            .Where(meeting => command.Ids.Contains(meeting.Id))
            .ToListAsync(cancellationToken);

        await MeetingDeletion.DetachActionItemsAsync(
            context,
            [.. meetings.Select(meeting => meeting.Id)],
            cancellationToken);

        context.Meetings.RemoveRange(meetings);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResultDto(meetings.Count));
    }
}

/// <summary>
/// What has to happen to a meeting's tasks before the meeting goes.
/// </summary>
/// <remarks>
/// <para>
/// <b>The <c>ON DELETE SET NULL</c> foreign key does not fire for a soft delete</b>, because
/// nothing is deleted — the row is updated. So the behaviour §3.8 specifies, a task surviving its
/// meeting and losing its back-reference, has to be performed here. Without it a task keeps pointing
/// at a meeting that every read now hides, and the UI offers a link to a 404.
/// </para>
/// <para>
/// Done inline rather than through a <c>MeetingDeleted</c> event on purpose. Events are dispatched
/// after the commit and a failing handler is swallowed, which would leave the meeting deleted and
/// the tasks dangling. These two writes have to be one transaction.
/// </para>
/// </remarks>
internal static class MeetingDeletion
{
    public static async Task DetachActionItemsAsync(
        ICadenceDbContext context,
        IReadOnlyList<Guid> meetingIds,
        CancellationToken cancellationToken)
    {
        if (meetingIds.Count == 0)
        {
            return;
        }

        var items = await context.ActionItems
            .Where(item => item.MeetingId != null && meetingIds.Contains(item.MeetingId.Value))
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.DetachFromMeeting();
        }
    }
}

/// <summary>
/// Resolves participant ids to people who are actually in this workspace.
/// </summary>
/// <remarks>
/// <b>The membership check is a tenant boundary, not a validation nicety.</b> A participant row
/// copies the person's name and email onto the meeting, so accepting an arbitrary user id would
/// turn "create a meeting" into a lookup that returns any user's name and address — including users
/// of other organizations. Ids that are not members are dropped rather than reported, for the same
/// reason a cross-tenant read returns nothing rather than "forbidden".
/// </remarks>
internal static class MeetingParticipants
{
    public static async Task<Result<IReadOnlyList<ResolvedParticipant>>> ResolveAsync(
        ICadenceDbContext context,
        Guid organizationId,
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return Result.Success<IReadOnlyList<ResolvedParticipant>>([]);
        }

        var distinct = userIds.Distinct().ToList();

        var resolved = await context.OrganizationMembers
            .AsNoTracking()
            .Where(member => member.OrganizationId == organizationId
                && distinct.Contains(member.UserId))
            .Join(
                context.Users.AsNoTracking().IgnoreQueryFilters(),
                member => member.UserId,
                user => user.Id,
                (member, user) => new ResolvedParticipant(user.Id, user.Name, user.Email))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ResolvedParticipant>>(resolved);
    }

    /// <summary>
    /// Syncs the meeting's attendees to <paramref name="attendees"/>, adding and removing.
    /// </summary>
    /// <remarks>
    /// Callers resolve the organizer alongside the requested ids, so the organizer is always in the
    /// wanted set and is never removed by the pass below.
    /// </remarks>
    public static void Apply(
        Meeting meeting,
        Guid organizerId,
        IReadOnlyList<ResolvedParticipant> attendees,
        ICadenceDbContext? context = null)
    {
        var wanted = attendees.ToDictionary(attendee => attendee.UserId);

        foreach (var existing in meeting.Participants.ToList())
        {
            if (!wanted.ContainsKey(existing.UserId))
            {
                meeting.RemoveParticipant(existing.UserId);
            }
        }

        foreach (var attendee in attendees)
        {
            if (meeting.Participants.Any(participant => participant.UserId == attendee.UserId))
            {
                continue;
            }

            // Host, not a separate "organizer" role — the client's ParticipantRole has three values
            // and Host is the one the meeting header renders for whoever called the meeting.
            var role = attendee.UserId == organizerId
                ? ParticipantRole.Host
                : ParticipantRole.Attendee;

            var participant = meeting.AddParticipant(
                attendee.UserId,
                attendee.Name,
                attendee.Email,
                role);

            // Added to the set explicitly when the meeting is already tracked. Cadence entities
            // generate their own key in the constructor, so change detection reads a populated key
            // as an existing row and emits an UPDATE that matches nothing. A meeting being created
            // is added as a whole graph and does not need this.
            context?.Participants.Add(participant);
        }
    }

    public sealed record ResolvedParticipant(Guid UserId, string Name, string Email);
}
