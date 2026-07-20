using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.ActionItems;

/// <summary>
/// A task, as the Tasks pages and the meeting detail panel render it.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>ActionItem</c> shape 1:1 (§6). <c>MeetingId</c> and
/// <c>SourceSegmentId</c> are null for a task created by hand — and <c>MeetingId</c> also goes null
/// when the meeting it came from is deleted, because a commitment outlives the meeting that produced
/// it (§3.8).
/// </remarks>
public sealed record ActionItemDto(
    Guid Id,
    string Title,
    string Description,
    Guid? AssigneeId,
    Guid CreatorId,
    DateTimeOffset? DueDate,
    ActionItemPriority Priority,
    ActionItemStatus Status,
    Guid? MeetingId,
    Guid? SourceSegmentId,
    DateTimeOffset? CompletedAt,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A new task.
/// </summary>
/// <remarks>
/// There is deliberately no <c>creatorId</c>. The client's mock takes one because it has no session
/// to read; here it comes from the token. Accepting it from the body would let any member file work
/// under someone else's name.
/// </remarks>
public sealed record CreateActionItemRequest(
    string Title,
    string? Description,
    Guid? AssigneeId,
    DateTimeOffset? DueDate,
    ActionItemPriority? Priority,
    Guid? MeetingId,
    Guid? SourceSegmentId,
    IReadOnlyList<string>? Tags);

/// <summary>
/// The editable parts of a task, as a true partial update.
/// </summary>
/// <remarks>
/// <para>
/// Every field is a <see cref="Patch{T}"/> because the client patches one field at a time — a
/// checkbox sends <c>{ status }</c>, the assignee picker sends <c>{ assigneeId }</c>, and clearing
/// an assignee sends <c>{ assigneeId: null }</c>. An absent field and an explicit null have to mean
/// different things, and a plain nullable property cannot say which arrived.
/// </para>
/// <para>
/// <c>CompletedAt</c> is not here. It is derived from <c>Status</c> by the aggregate, so a client
/// cannot report a task as done at a time it was not.
/// </para>
/// </remarks>
public sealed record UpdateActionItemRequest
{
    public Patch<string> Title { get; init; }

    public Patch<string> Description { get; init; }

    public Patch<Guid?> AssigneeId { get; init; }

    public Patch<DateTimeOffset?> DueDate { get; init; }

    public Patch<ActionItemPriority> Priority { get; init; }

    public Patch<ActionItemStatus> Status { get; init; }

    public Patch<IReadOnlyList<string>> Tags { get; init; }
}

public sealed record BulkStatusRequest(IReadOnlyList<Guid> Ids, ActionItemStatus Status);

/// <summary>Reassigns in bulk. A null <c>AssigneeId</c> unassigns.</summary>
public sealed record BulkAssignRequest(IReadOnlyList<Guid> Ids, Guid? AssigneeId);

public sealed record BulkPriorityRequest(IReadOnlyList<Guid> Ids, ActionItemPriority Priority);

/// <summary>
/// The numbers behind the Tasks view tabs and the board column headers.
/// </summary>
/// <remarks>
/// <para>
/// Computed in one pass over one query, so a tab badge cannot disagree with the list beneath it.
/// Six separate count queries would drift the moment anything changed between them.
/// </para>
/// <para>
/// <c>Assigned</c> and <c>Created</c> are relative to the calling user, taken from the token — the
/// client's mock passes a <c>userId</c> because it has no session to read.
/// </para>
/// </remarks>
/// <param name="All">Every task in the workspace.</param>
/// <param name="Assigned">Assigned to the caller and not yet done.</param>
/// <param name="Created">Filed by the caller, in any state.</param>
/// <param name="Completed">Done, by anyone.</param>
/// <param name="Overdue">Past its due date and not done.</param>
/// <param name="ByStatus">One entry per status, including the statuses with no tasks.</param>
public sealed record ActionItemCountsDto(
    int All,
    int Assigned,
    int Created,
    int Completed,
    int Overdue,
    IReadOnlyDictionary<ActionItemStatus, int> ByStatus);
