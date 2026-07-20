using System.Linq.Expressions;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.ActionItems;

/// <summary>
/// The single read path for action items.
/// </summary>
/// <remarks>
/// Every endpoint that returns a task — the list, the board, a meeting's panel, and the response to
/// each write — projects through here, so no two of them can disagree about the same row.
/// </remarks>
internal static class ActionItemReads
{
    /// <summary>The property name of the tag collection's backing field.</summary>
    /// <remarks>
    /// <c>ActionItem.Tags</c> is <c>Ignore</c>d in the EF configuration — the mapped member is the
    /// private field — so a query has to name the field. Naming the public property compiles and
    /// then fails at translation time.
    /// </remarks>
    private const string TagsField = "_tags";

    public static Error NotFound =>
        Error.NotFound("action_item.not_found", "That task could not be found.");

    public static async Task<Result<ActionItemDto>> LoadAsync(
        ICadenceDbContext context,
        Guid actionItemId,
        CancellationToken cancellationToken)
    {
        // Filtered before projecting: a predicate over the projected DTO has to be translated
        // against a constructor call, which EF cannot do.
        var item = await Project(
                context.ActionItems.AsNoTracking().Where(row => row.Id == actionItemId))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? Result.Failure<ActionItemDto>(NotFound) : Result.Success(item);
    }

    public static IQueryable<ActionItemDto> Project(IQueryable<ActionItem> source) =>
        source.Select(item => new ActionItemDto(
            item.Id,
            item.Title,
            item.Description,
            item.AssigneeId,
            item.CreatorId,
            item.DueDate,
            item.Priority,
            item.Status,
            item.MeetingId,
            item.SourceSegmentId,
            item.CompletedAt,
            EF.Property<List<string>>(item, TagsField),
            item.CreatedAt,
            item.UpdatedAt));

    /// <summary>
    /// Applies the task filters.
    /// </summary>
    /// <remarks>
    /// No tenant predicate: <c>ActionItem</c> is <c>ITenantScoped</c>, so the global filter has
    /// already scoped this. Writing one by hand is how the filter eventually gets relied on
    /// somewhere that forgot it (§3.3).
    /// </remarks>
    public static IQueryable<ActionItem> Filtered(
        ICadenceDbContext context,
        ActionItemQuery query,
        DateTimeOffset now)
    {
        var items = context.ActionItems.AsNoTracking();

        if (query.Status is { Count: > 0 })
        {
            items = items.Where(item => query.Status.Contains(item.Status));
        }

        if (query.Priority is { Count: > 0 })
        {
            items = items.Where(item => query.Priority.Contains(item.Priority));
        }

        if (query.AssigneeId is { } assigneeId)
        {
            items = items.Where(item => item.AssigneeId == assigneeId);
        }

        if (query.CreatorId is { } creatorId)
        {
            items = items.Where(item => item.CreatorId == creatorId);
        }

        if (query.MeetingId is { } meetingId)
        {
            items = items.Where(item => item.MeetingId == meetingId);
        }

        // Distinct from AssigneeId simply being absent, which means "any assignee".
        if (query.UnassignedOnly)
        {
            items = items.Where(item => item.AssigneeId == null);
        }

        if (query.Tags is { Count: > 0 })
        {
            items = items.Where(item =>
                EF.Property<List<string>>(item, TagsField).Any(tag => query.Tags.Contains(tag)));
        }

        if (query.OverdueOnly)
        {
            items = items.Where(item =>
                item.Status != ActionItemStatus.Done
                && item.DueDate != null
                && item.DueDate < now);
        }

        // An undated task cannot fall inside a date window, so it drops out of a windowed query
        // rather than being treated as due at either edge.
        if (query.DueFrom is { } dueFrom)
        {
            items = items.Where(item => item.DueDate != null && item.DueDate >= dueFrom);
        }

        if (query.DueTo is { } dueTo)
        {
            items = items.Where(item => item.DueDate != null && item.DueDate <= dueTo);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // lower() + Contains rather than Npgsql's ILike: Application is provider-neutral and an
            // architecture test fails the build if Npgsql appears here.
            var term = query.Search.Trim().ToLowerInvariant();

            items = items.Where(item =>
                item.Title.ToLower().Contains(term)
                || item.Description.ToLower().Contains(term)
                || EF.Property<List<string>>(item, TagsField).Any(tag => tag.Contains(term)));
        }

        return items;
    }

    /// <summary>
    /// Orders the result, defaulting to the nearest due date first.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Priority and status cannot be ordered by their column.</b> Both are stored as text (§3.4),
    /// so <c>ORDER BY priority</c> is alphabetical — <c>high, low, medium, urgent</c> — which reads
    /// as a severity ordering and is not one. The <c>CASE</c> expressions below rank them by meaning
    /// instead, and EF translates them into the query rather than sorting in memory.
    /// </para>
    /// <para>
    /// Undated tasks sort last in <i>both</i> directions. Postgres puts nulls first on a descending
    /// sort, which would open the list with every task that has no due date — the least urgent rows
    /// leading a view sorted by urgency.
    /// </para>
    /// <para>
    /// Every sort ends on <c>Id</c>. Without a tiebreaker, rows with equal sort keys have no defined
    /// order, so one can appear on two pages while another never appears at all.
    /// </para>
    /// </remarks>
    public static IQueryable<ActionItem> Sorted(IQueryable<ActionItem> items, ActionItemQuery query)
    {
        var ascending = query.SortDir == SortDirection.Asc;

        return query.SortBy?.ToLowerInvariant() switch
        {
            "priority" => ascending
                ? items.OrderBy(PriorityRank).ThenBy(item => item.Id)
                : items.OrderByDescending(PriorityRank).ThenBy(item => item.Id),

            "title" => ascending
                ? items.OrderBy(item => item.Title).ThenBy(item => item.Id)
                : items.OrderByDescending(item => item.Title).ThenBy(item => item.Id),

            "status" => ascending
                ? items.OrderBy(StatusRank).ThenBy(item => item.Id)
                : items.OrderByDescending(StatusRank).ThenBy(item => item.Id),

            "createdat" => ascending
                ? items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
                : items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id),

            _ => ascending
                ? items.OrderBy(item => item.DueDate == null)
                    .ThenBy(item => item.DueDate)
                    .ThenBy(item => item.Id)
                : items.OrderBy(item => item.DueDate == null)
                    .ThenByDescending(item => item.DueDate)
                    .ThenBy(item => item.Id),
        };
    }

    /// <summary>
    /// Severity order, matching the client's <c>PRIORITY_RANK</c>.
    /// </summary>
    /// <remarks>
    /// An <see cref="Expression"/> field rather than a method: a call to a user-defined method
    /// inside a query cannot be translated, and EF's only recourse is to fail at runtime. Held once
    /// so the ascending and descending branches provably rank the same way.
    /// </remarks>
    private static readonly Expression<Func<ActionItem, int>> PriorityRank =
        item => item.Priority == ActionItemPriority.Urgent ? 3
            : item.Priority == ActionItemPriority.High ? 2
            : item.Priority == ActionItemPriority.Medium ? 1
            : 0;

    /// <summary>
    /// Workflow order: todo → in progress → blocked → done.
    /// </summary>
    /// <remarks>
    /// The client's mock sorts the status string, which yields <c>blocked, done, in_progress,
    /// todo</c> — an artifact of alphabetising wire values rather than an order anyone reads meaning
    /// into. Nothing in the UI depends on the exact sequence, so the server sorts by the pipeline a
    /// task actually moves through.
    /// </remarks>
    private static readonly Expression<Func<ActionItem, int>> StatusRank =
        item => item.Status == ActionItemStatus.Todo ? 0
            : item.Status == ActionItemStatus.InProgress ? 1
            : item.Status == ActionItemStatus.Blocked ? 2
            : 3;

    public static async Task<PagedResult<ActionItemDto>> PageAsync(
        ICadenceDbContext context,
        ActionItemQuery query,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var filtered = Filtered(context, query, now);

        var total = await filtered.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<ActionItemDto>.Empty(query.Page, query.PageSize);
        }

        var page = await Project(Sorted(filtered, query))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<ActionItemDto>(page, total, query.Page, query.PageSize);
    }
}
