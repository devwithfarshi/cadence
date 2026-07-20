using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Common;
using Cadence.Domain.Meetings;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Meetings;

/// <summary>Flips the favourite flag.</summary>
public sealed record ToggleFavoriteCommand(Guid MeetingId) : ICommand<Result<MeetingSummaryDto>>;

/// <summary>Archives or unarchives meetings in bulk.</summary>
public sealed record SetArchivedCommand(IReadOnlyList<Guid> Ids, bool Archived)
    : ICommand<Result<BulkResultDto>>;

/// <summary>Copies a meeting into a new scheduled one.</summary>
public sealed record DuplicateMeetingCommand(Guid MeetingId) : ICommand<Result<MeetingDetailDto>>;

/// <summary>Flags a moment in the recording.</summary>
public sealed record AddBookmarkCommand(Guid MeetingId, AddBookmarkRequest Bookmark)
    : ICommand<Result<BookmarkDto>>;

internal sealed class AddBookmarkValidator : AbstractValidator<AddBookmarkCommand>
{
    public AddBookmarkValidator()
    {
        RuleFor(command => command.Bookmark.AtSeconds)
            .GreaterThanOrEqualTo(0)
            .WithMessage("A bookmark cannot sit before the start of the recording.");

        RuleFor(command => command.Bookmark.Label).MaximumLength(300);
    }
}

public sealed class ToggleFavoriteHandler(ICadenceDbContext context)
    : ICommandHandler<ToggleFavoriteCommand, Result<MeetingSummaryDto>>
{
    public async ValueTask<Result<MeetingSummaryDto>> Handle(
        ToggleFavoriteCommand command,
        CancellationToken cancellationToken)
    {
        var meeting = await context.Meetings
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MeetingId, cancellationToken);

        if (meeting is null)
        {
            return Result.Failure<MeetingSummaryDto>(MeetingReads.NotFound);
        }

        // A toggle rather than a set, matching the client. The two differ under a double-click: a
        // toggle applied twice returns to where it started, where two "set true" calls do not
        // reveal that the second one did nothing.
        meeting.ToggleFavorite();
        await context.SaveChangesAsync(cancellationToken);

        return await MeetingReads.LoadSummaryAsync(context, meeting.Id, cancellationToken);
    }
}

public sealed class SetArchivedHandler(ICadenceDbContext context)
    : ICommandHandler<SetArchivedCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        SetArchivedCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        // Already-archived rows are excluded so the count means "changed", not "matched". A bulk
        // archive over a mixed selection otherwise reports a number the UI cannot explain.
        var meetings = await context.Meetings
            .Where(meeting => command.Ids.Contains(meeting.Id)
                && meeting.IsArchived != command.Archived)
            .ToListAsync(cancellationToken);

        foreach (var meeting in meetings)
        {
            meeting.SetArchived(command.Archived);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResultDto(meetings.Count));
    }
}

public sealed class DuplicateMeetingHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<DuplicateMeetingCommand, Result<MeetingDetailDto>>
{
    public async ValueTask<Result<MeetingDetailDto>> Handle(
        DuplicateMeetingCommand command,
        CancellationToken cancellationToken)
    {
        var source = await context.Meetings
            .AsNoTracking()
            .Include(meeting => meeting.Participants)
            .FirstOrDefaultAsync(meeting => meeting.Id == command.MeetingId, cancellationToken);

        if (source is null)
        {
            return Result.Failure<MeetingDetailDto>(MeetingReads.NotFound);
        }

        var tags = await context.Meetings
            .AsNoTracking()
            .Where(meeting => meeting.Id == command.MeetingId)
            .Select(meeting => EF.Property<List<string>>(meeting, "_tags"))
            .FirstAsync(cancellationToken);

        // A fresh scheduled meeting, not a copy of a finished one. Duplicating is how a recurring
        // meeting gets set up again, so the recording, the summary and the bookmarks are all
        // deliberately left behind — carrying them over would attach last week's transcript to a
        // meeting that has not happened.
        var copy = Meeting.Schedule(
            currentUser.RequireOrganizationId(),
            currentUser.RequireId(),
            $"{source.Title} (copy)",
            source.Description,
            source.StartsAt,
            source.EndsAt,
            source.Platform,
            tags);

        copy.SetMeetingUrl(source.MeetingUrl);

        foreach (var participant in source.Participants)
        {
            copy.AddParticipant(
                participant.UserId,
                participant.Name,
                participant.Email,
                participant.Role);
        }

        await context.Meetings.AddAsync(copy, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await MeetingReads.LoadDetailAsync(context, copy.Id, cancellationToken);
    }
}

public sealed class AddBookmarkHandler(ICadenceDbContext context)
    : ICommandHandler<AddBookmarkCommand, Result<BookmarkDto>>
{
    public async ValueTask<Result<BookmarkDto>> Handle(
        AddBookmarkCommand command,
        CancellationToken cancellationToken)
    {
        var meeting = await context.Meetings
            .Include(candidate => candidate.Bookmarks)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MeetingId, cancellationToken);

        if (meeting is null)
        {
            return Result.Failure<BookmarkDto>(MeetingReads.NotFound);
        }

        Bookmark bookmark;

        try
        {
            bookmark = meeting.AddBookmark(command.Bookmark.AtSeconds, command.Bookmark.Label);
        }
        catch (DomainException exception)
        {
            return Result.Failure<BookmarkDto>(Error.Validation("bookmark.invalid", exception.Message));
        }

        // Explicitly added: the meeting is already tracked, so a child carrying its own generated
        // key would otherwise be detected as an existing row and issued as an UPDATE.
        context.Bookmarks.Add(bookmark);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BookmarkDto(
            bookmark.Id,
            bookmark.AtSeconds,
            bookmark.Label,
            bookmark.CreatedAt));
    }
}
