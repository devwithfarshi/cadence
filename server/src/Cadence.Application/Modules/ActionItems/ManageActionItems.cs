using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Common;
using Cadence.Domain.Work;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.ActionItems;

/// <summary>One task.</summary>
public sealed record GetActionItemQuery(Guid ActionItemId) : IQuery<Result<ActionItemDto>>;

public sealed record CreateActionItemCommand(CreateActionItemRequest Item)
    : ICommand<Result<ActionItemDto>>;

public sealed record UpdateActionItemCommand(Guid ActionItemId, UpdateActionItemRequest Item)
    : ICommand<Result<ActionItemDto>>;

public sealed record DeleteActionItemCommand(Guid ActionItemId) : ICommand<Result>;

internal sealed class CreateActionItemValidator : AbstractValidator<CreateActionItemCommand>
{
    public CreateActionItemValidator()
    {
        RuleFor(command => command.Item.Title)
            .NotEmpty().WithMessage("Describe what needs to happen.")
            .MaximumLength(300);

        RuleFor(command => command.Item.Priority).IsInEnum();
    }
}

internal sealed class UpdateActionItemValidator : AbstractValidator<UpdateActionItemCommand>
{
    public UpdateActionItemValidator()
    {
        // Each rule is conditioned on the field having been sent. A patch that only changes the
        // status must not be rejected for a title it never mentioned.
        RuleFor(command => command.Item.Title.Value)
            .NotEmpty().WithMessage("Describe what needs to happen.")
            .MaximumLength(300)
            .When(command => command.Item.Title.HasValue);

        RuleFor(command => command.Item.Priority.Value)
            .IsInEnum()
            .When(command => command.Item.Priority.HasValue);

        RuleFor(command => command.Item.Status.Value)
            .IsInEnum()
            .When(command => command.Item.Status.HasValue);
    }
}

public sealed class GetActionItemHandler(ICadenceDbContext context)
    : IQueryHandler<GetActionItemQuery, Result<ActionItemDto>>
{
    public async ValueTask<Result<ActionItemDto>> Handle(
        GetActionItemQuery query,
        CancellationToken cancellationToken) =>
        await ActionItemReads.LoadAsync(context, query.ActionItemId, cancellationToken);
}

public sealed class CreateActionItemHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<CreateActionItemCommand, Result<ActionItemDto>>
{
    public async ValueTask<Result<ActionItemDto>> Handle(
        CreateActionItemCommand command,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.RequireOrganizationId();
        var creatorId = currentUser.RequireId();
        var request = command.Item;

        var assignee = await ActionItemWrites.ResolveAssigneeAsync(
            context,
            organizationId,
            request.AssigneeId,
            cancellationToken);

        if (assignee.IsFailure)
        {
            return Result.Failure<ActionItemDto>(assignee.Error);
        }

        var provenance = await ActionItemWrites.ResolveProvenanceAsync(
            context,
            request.MeetingId,
            request.SourceSegmentId,
            cancellationToken);

        if (provenance.IsFailure)
        {
            return Result.Failure<ActionItemDto>(provenance.Error);
        }

        ActionItem item;

        try
        {
            item = ActionItem.Create(
                organizationId,
                creatorId,
                request.Title,
                request.Description ?? string.Empty,
                request.Priority ?? Domain.Enums.ActionItemPriority.Medium,
                assignee.Value,
                request.DueDate,
                request.Tags);
        }
        catch (DomainException exception)
        {
            return Result.Failure<ActionItemDto>(
                Error.Validation("action_item.invalid", exception.Message));
        }

        ActionItemWrites.AttachProvenance(item, provenance.Value);

        await context.ActionItems.AddAsync(item, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await ActionItemReads.LoadAsync(context, item.Id, cancellationToken);
    }
}

public sealed class UpdateActionItemHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<UpdateActionItemCommand, Result<ActionItemDto>>
{
    public async ValueTask<Result<ActionItemDto>> Handle(
        UpdateActionItemCommand command,
        CancellationToken cancellationToken)
    {
        var item = await context.ActionItems
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ActionItemId, cancellationToken);

        if (item is null)
        {
            return Result.Failure<ActionItemDto>(ActionItemReads.NotFound);
        }

        var patch = command.Item;

        // Every branch below runs only when the client actually sent the field. An absent field and
        // an explicit null mean different things here — see Patch<T>.
        if (patch.AssigneeId.HasValue)
        {
            var assignee = await ActionItemWrites.ResolveAssigneeAsync(
                context,
                currentUser.RequireOrganizationId(),
                patch.AssigneeId.Value,
                cancellationToken);

            if (assignee.IsFailure)
            {
                return Result.Failure<ActionItemDto>(assignee.Error);
            }

            item.Assign(assignee.Value, currentUser.RequireId());
        }

        if (patch.Title.HasValue || patch.Description.HasValue)
        {
            try
            {
                item.UpdateDetails(
                    patch.Title.Or(item.Title) ?? string.Empty,
                    patch.Description.Or(item.Description) ?? string.Empty);
            }
            catch (DomainException exception)
            {
                return Result.Failure<ActionItemDto>(
                    Error.Validation("action_item.invalid", exception.Message));
            }
        }

        patch.DueDate.Apply(item.SetDueDate);
        patch.Priority.Apply(priority => item.ChangePriority(priority));

        // Status last, so CompletedAt is derived from the status the request ends on rather than
        // from an intermediate one.
        patch.Status.Apply(status => item.ChangeStatus(status));
        patch.Tags.Apply(tags => item.ReplaceTags(tags ?? []));

        await context.SaveChangesAsync(cancellationToken);

        return await ActionItemReads.LoadAsync(context, item.Id, cancellationToken);
    }
}

public sealed class DeleteActionItemHandler(ICadenceDbContext context)
    : ICommandHandler<DeleteActionItemCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteActionItemCommand command,
        CancellationToken cancellationToken)
    {
        var item = await context.ActionItems
            .FirstOrDefaultAsync(candidate => candidate.Id == command.ActionItemId, cancellationToken);

        if (item is null)
        {
            return Result.Failure(ActionItemReads.NotFound);
        }

        // Soft delete, applied by the auditing interceptor.
        context.ActionItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

/// <summary>
/// The checks every task write shares.
/// </summary>
/// <remarks>
/// Both of these are tenant boundaries rather than input tidiness, which is why they are here rather
/// than in a validator: a validator answers "is this well-formed", and the question these ask is
/// "may this caller reference that row".
/// </remarks>
internal static class ActionItemWrites
{
    /// <summary>
    /// Checks that an assignee is a member of this workspace.
    /// </summary>
    /// <remarks>
    /// Without it, assigning is a way to write into a stranger's task list — the row would carry a
    /// user id from another organization and surface in their "assigned to me" view. Unlike a
    /// meeting participant, which is dropped silently, an unknown assignee is reported: the client
    /// picked this person from a member list, so a mismatch is a real error rather than a stale
    /// selection to ignore.
    /// </remarks>
    public static async Task<Result<Guid?>> ResolveAssigneeAsync(
        ICadenceDbContext context,
        Guid organizationId,
        Guid? assigneeId,
        CancellationToken cancellationToken)
    {
        if (assigneeId is not { } id)
        {
            return Result.Success<Guid?>(null);
        }

        var isMember = await context.OrganizationMembers
            .AsNoTracking()
            .AnyAsync(
                member => member.OrganizationId == organizationId && member.UserId == id,
                cancellationToken);

        return isMember
            ? Result.Success<Guid?>(id)
            : Result.Failure<Guid?>(Error.Validation(
                "action_item.assignee_not_a_member",
                "That person is not a member of this workspace."));
    }

    /// <summary>
    /// Checks the meeting is visible here, and that the cited line belongs to it.
    /// </summary>
    /// <remarks>
    /// The segment check is the same rule the summary applies to a highlight: a citation that does
    /// not resolve is worse than none, because it looks checkable and is not. A line from a
    /// different meeting is dropped rather than stored.
    /// </remarks>
    public static async Task<Result<Provenance>> ResolveProvenanceAsync(
        ICadenceDbContext context,
        Guid? meetingId,
        Guid? sourceSegmentId,
        CancellationToken cancellationToken)
    {
        if (meetingId is not { } id)
        {
            // A hand-created task with no meeting cannot cite a transcript line either.
            return Result.Success(new Provenance(null, null));
        }

        var meetingExists = await context.Meetings
            .AsNoTracking()
            .AnyAsync(meeting => meeting.Id == id, cancellationToken);

        if (!meetingExists)
        {
            return Result.Failure<Provenance>(
                Error.NotFound("meeting.not_found", "That meeting could not be found."));
        }

        if (sourceSegmentId is not { } segmentId)
        {
            return Result.Success(new Provenance(id, null));
        }

        var segmentBelongs = await context.TranscriptSegments
            .AsNoTracking()
            .AnyAsync(
                segment => segment.Id == segmentId && segment.MeetingId == id,
                cancellationToken);

        return Result.Success(new Provenance(id, segmentBelongs ? segmentId : null));
    }

    public static void AttachProvenance(ActionItem item, Provenance provenance)
    {
        if (provenance.MeetingId is { } meetingId)
        {
            item.LinkToMeeting(meetingId, provenance.SourceSegmentId);
        }
    }

    public sealed record Provenance(Guid? MeetingId, Guid? SourceSegmentId);
}
