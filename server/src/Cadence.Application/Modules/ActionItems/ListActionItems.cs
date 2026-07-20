using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.ActionItems;

/// <summary>
/// The task list, filtered and paged.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>TaskQuery</c> 1:1 (§6). Nothing here is workspace-wide-optional the way
/// meetings have an archive: a task is either in the workspace or it is deleted.
/// </remarks>
public sealed record ActionItemQuery : ListQuery
{
    public IReadOnlyList<ActionItemStatus>? Status { get; init; }

    public IReadOnlyList<ActionItemPriority>? Priority { get; init; }

    public Guid? AssigneeId { get; init; }

    public Guid? CreatorId { get; init; }

    public Guid? MeetingId { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>Only tasks with no assignee. Distinct from <see cref="AssigneeId"/> being absent.</summary>
    public bool UnassignedOnly { get; init; }

    /// <summary>Only tasks whose due date has passed and which are not done.</summary>
    public bool OverdueOnly { get; init; }

    /// <summary>Restricts to tasks due within this window; undated tasks drop out.</summary>
    public DateTimeOffset? DueFrom { get; init; }

    public DateTimeOffset? DueTo { get; init; }
}

public sealed record ListActionItemsQuery(ActionItemQuery Query)
    : IQuery<Result<PagedResult<ActionItemDto>>>;

/// <summary>Every task raised in one meeting, for the detail page's panel.</summary>
/// <remarks>
/// Unpaged: a meeting produces a handful of commitments, and the panel shows all of them. The
/// meeting is checked through the tenant-filtered set first, so an id from another workspace is a
/// 404 rather than a list of its tasks.
/// </remarks>
public sealed record ListMeetingActionItemsQuery(Guid MeetingId)
    : IQuery<Result<IReadOnlyList<ActionItemDto>>>;

/// <summary>The counts behind the view tabs and the board column headers.</summary>
public sealed record ActionItemCountsQuery : IQuery<Result<ActionItemCountsDto>>;

/// <summary>Distinct tags across the workspace's tasks, for the filter menus.</summary>
public sealed record ListActionItemTagsQuery : IQuery<Result<IReadOnlyList<string>>>;

public sealed class ListActionItemsHandler(ICadenceDbContext context, IDateTime clock)
    : IQueryHandler<ListActionItemsQuery, Result<PagedResult<ActionItemDto>>>
{
    public async ValueTask<Result<PagedResult<ActionItemDto>>> Handle(
        ListActionItemsQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(
            await ActionItemReads.PageAsync(context, query.Query, clock.UtcNow, cancellationToken));
}

public sealed class ListMeetingActionItemsHandler(ICadenceDbContext context)
    : IQueryHandler<ListMeetingActionItemsQuery, Result<IReadOnlyList<ActionItemDto>>>
{
    public async ValueTask<Result<IReadOnlyList<ActionItemDto>>> Handle(
        ListMeetingActionItemsQuery query,
        CancellationToken cancellationToken)
    {
        var meetingExists = await context.Meetings
            .AsNoTracking()
            .AnyAsync(meeting => meeting.Id == query.MeetingId, cancellationToken);

        if (!meetingExists)
        {
            return Result.Failure<IReadOnlyList<ActionItemDto>>(
                Error.NotFound("meeting.not_found", "That meeting could not be found."));
        }

        // Ordered before projecting, never after. A sort over the projected DTO has to be translated
        // against a constructor call, which EF cannot do — it fails at runtime, not at compile time.
        var items = await ActionItemReads.Project(
                context.ActionItems
                    .AsNoTracking()
                    .Where(item => item.MeetingId == query.MeetingId)
                    .OrderBy(item => item.CreatedAt)
                    .ThenBy(item => item.Id))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<ActionItemDto>>(items);
    }
}

public sealed class ActionItemCountsHandler(
    ICadenceDbContext context,
    ICurrentUser currentUser,
    IDateTime clock)
    : IQueryHandler<ActionItemCountsQuery, Result<ActionItemCountsDto>>
{
    public async ValueTask<Result<ActionItemCountsDto>> Handle(
        ActionItemCountsQuery query,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();
        var now = clock.UtcNow;

        // One grouped round trip rather than six counts. Separate queries would each see a slightly
        // different database, and the tab badges would disagree with the list beneath them in a way
        // that looks like a bug in the list.
        var grouped = await context.ActionItems
            .AsNoTracking()
            .GroupBy(item => item.Status)
            .Select(group => new
            {
                Status = group.Key,
                Total = group.Count(),
                Assigned = group.Count(item => item.AssigneeId == userId),
                Created = group.Count(item => item.CreatorId == userId),
                Overdue = group.Count(item => item.DueDate != null && item.DueDate < now),
            })
            .ToListAsync(cancellationToken);

        // Every status is present even at zero: the board renders a column per status and the client
        // indexes straight into this map. A missing key would read as undefined, not as none.
        var byStatus = Enum.GetValues<ActionItemStatus>().ToDictionary(
            status => status,
            status => grouped.FirstOrDefault(row => row.Status == status)?.Total ?? 0);

        var open = grouped.Where(row => row.Status != ActionItemStatus.Done).ToList();

        return Result.Success(new ActionItemCountsDto(
            All: grouped.Sum(row => row.Total),
            // "Assigned to me" means work still to do — a tab counting finished tasks would never
            // reach zero.
            Assigned: open.Sum(row => row.Assigned),
            Created: grouped.Sum(row => row.Created),
            Completed: byStatus[ActionItemStatus.Done],
            Overdue: open.Sum(row => row.Overdue),
            ByStatus: byStatus));
    }
}

public sealed class ListActionItemTagsHandler(ICadenceDbContext context)
    : IQueryHandler<ListActionItemTagsQuery, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(
        ListActionItemTagsQuery query,
        CancellationToken cancellationToken)
    {
        // Flattened in the database rather than by loading every task's tag array and unioning in
        // memory — this feeds a filter menu, and the menu should not cost a full table read.
        var tags = await context.ActionItems
            .AsNoTracking()
            .SelectMany(item => EF.Property<List<string>>(item, "_tags"))
            .Distinct()
            .OrderBy(tag => tag)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<string>>(tags);
    }
}
