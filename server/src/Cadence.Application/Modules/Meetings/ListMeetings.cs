using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Meetings;

/// <summary>
/// The meetings list, filtered and paged.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>MeetingQuery</c> 1:1 (§6.3). Archived meetings are excluded unless asked
/// for, because the list is a working view and the archive is where things go to stop appearing in
/// it.
/// </remarks>
public sealed record MeetingQuery : ListQuery
{
    public IReadOnlyList<MeetingStatus>? Status { get; init; }

    public IReadOnlyList<MeetingPlatform>? Platform { get; init; }

    public IReadOnlyList<SummaryStatus>? SummaryStatus { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public Guid? ParticipantId { get; init; }

    public bool FavoritesOnly { get; init; }

    public bool IncludeArchived { get; init; }

    /// <summary>Restricts to meetings starting within this window.</summary>
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }
}

public sealed record ListMeetingsQuery(MeetingQuery Query)
    : IQuery<Result<PagedResult<MeetingSummaryDto>>>;

/// <summary>
/// Everything already held, newest first.
/// </summary>
/// <remarks>
/// A separate endpoint rather than a preset the client assembles, so "history" means the same thing
/// everywhere. It defaults to finished meetings and <b>includes archived ones</b> — an archive that
/// hides archived meetings would have nothing to show.
/// </remarks>
public sealed record MeetingHistoryQuery(MeetingQuery Query)
    : IQuery<Result<PagedResult<MeetingSummaryDto>>>;

/// <summary>Distinct tags across the workspace's meetings, for the filter menus.</summary>
public sealed record ListMeetingTagsQuery : IQuery<Result<IReadOnlyList<string>>>;

public sealed class ListMeetingsHandler(ICadenceDbContext context)
    : IQueryHandler<ListMeetingsQuery, Result<PagedResult<MeetingSummaryDto>>>
{
    public async ValueTask<Result<PagedResult<MeetingSummaryDto>>> Handle(
        ListMeetingsQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(await MeetingReads.PageAsync(context, query.Query, cancellationToken));
}

public sealed class MeetingHistoryHandler(ICadenceDbContext context, IDateTime clock)
    : IQueryHandler<MeetingHistoryQuery, Result<PagedResult<MeetingSummaryDto>>>
{
    public async ValueTask<Result<PagedResult<MeetingSummaryDto>>> Handle(
        MeetingHistoryQuery query,
        CancellationToken cancellationToken)
    {
        var history = query.Query with
        {
            // Defaults, not overrides: a caller narrowing to one status or one month still gets what
            // they asked for. Only the unset parts are filled in.
            Status = query.Query.Status is { Count: > 0 }
                ? query.Query.Status
                : [MeetingStatus.Completed, MeetingStatus.Cancelled],
            To = query.Query.To ?? clock.UtcNow,
            IncludeArchived = true,
            SortBy = query.Query.SortBy ?? "startsAt",
        };

        return Result.Success(await MeetingReads.PageAsync(context, history, cancellationToken));
    }
}

public sealed class ListMeetingTagsHandler(ICadenceDbContext context)
    : IQueryHandler<ListMeetingTagsQuery, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(
        ListMeetingTagsQuery query,
        CancellationToken cancellationToken)
    {
        // Flattened in the database rather than by loading every meeting's tag array and unioning in
        // memory — this feeds a filter menu, and the menu should not cost a full table read.
        var tags = await context.Meetings
            .AsNoTracking()
            .SelectMany(meeting => EF.Property<List<string>>(meeting, "_tags"))
            .Distinct()
            .OrderBy(tag => tag)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<string>>(tags);
    }
}
