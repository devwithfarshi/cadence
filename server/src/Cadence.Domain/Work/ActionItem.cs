using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Work.Events;

namespace Cadence.Domain.Work;

/// <summary>
/// A task: either extracted from a meeting or created by hand.
/// </summary>
/// <remarks>
/// <see cref="MeetingId"/> is nullable and the foreign key is <c>ON DELETE SET NULL</c>
/// (blueprint §3.8). <b>A commitment outlives the meeting that produced it</b> — deleting a
/// meeting must never silently delete someone's assigned work. The same reasoning applies to
/// <see cref="AssigneeId"/>: removing a member unassigns their tasks rather than destroying them.
/// </remarks>
public sealed class ActionItem : AggregateRoot, ISoftDeletable
{
    private readonly List<string> _tags = [];

    private ActionItem()
    {
        Title = null!;
        Description = null!;
    }

    private ActionItem(
        Guid organizationId,
        Guid creatorId,
        string title,
        string description,
        ActionItemPriority priority)
    {
        OrganizationId = organizationId;
        CreatorId = creatorId;
        Title = title;
        Description = description;
        Priority = priority;
        Status = ActionItemStatus.Todo;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>Null for a hand-created task, or once its meeting has been deleted.</summary>
    public Guid? MeetingId { get; private set; }

    /// <summary>The transcript line this was extracted from, shown as provenance in the UI.</summary>
    public Guid? SourceSegmentId { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public Guid CreatorId { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset? DueDate { get; private set; }

    public ActionItemPriority Priority { get; private set; }

    public ActionItemStatus Status { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public IReadOnlyCollection<string> Tags => _tags.AsReadOnly();

    public bool IsOverdue =>
        Status != ActionItemStatus.Done && DueDate is not null && DueDate < DateTimeOffset.UtcNow;

    public static ActionItem Create(
        Guid organizationId,
        Guid creatorId,
        string title,
        string description = "",
        ActionItemPriority priority = ActionItemPriority.Medium,
        Guid? assigneeId = null,
        DateTimeOffset? dueDate = null,
        IEnumerable<string>? tags = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Title is required.");

        var item = new ActionItem(organizationId, creatorId, title.Trim(), description.Trim(), priority)
        {
            AssigneeId = assigneeId,
            DueDate = dueDate,
        };

        item.ReplaceTags(tags ?? []);

        if (assigneeId is not null)
        {
            item.Raise(new ActionItemAssigned(item.Id, organizationId, assigneeId.Value, creatorId));
        }

        return item;
    }

    /// <summary>Creates a task the AI extracted, keeping the link to its source line.</summary>
    public static ActionItem FromMeeting(
        Guid organizationId,
        Guid creatorId,
        Guid meetingId,
        Guid? sourceSegmentId,
        string title,
        Guid? assigneeId,
        ActionItemPriority priority)
    {
        var item = Create(organizationId, creatorId, title, priority: priority, assigneeId: assigneeId);
        item.MeetingId = meetingId;
        item.SourceSegmentId = sourceSegmentId;
        return item;
    }

    public void UpdateDetails(string title, string description)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Title cannot be empty.");

        Title = title.Trim();
        Description = description.Trim();
    }

    public void Assign(Guid? assigneeId, Guid actorId)
    {
        if (AssigneeId == assigneeId)
        {
            return;
        }

        AssigneeId = assigneeId;

        if (assigneeId is not null)
        {
            Raise(new ActionItemAssigned(Id, OrganizationId, assigneeId.Value, actorId));
        }
    }

    public void ChangeStatus(ActionItemStatus status)
    {
        if (Status == status)
        {
            return;
        }

        Status = status;

        // Keep CompletedAt consistent with status here, so no caller has to remember to.
        if (status == ActionItemStatus.Done)
        {
            CompletedAt = DateTimeOffset.UtcNow;
            Raise(new ActionItemCompleted(Id, OrganizationId, AssigneeId));
        }
        else
        {
            CompletedAt = null;
        }
    }

    public void ChangePriority(ActionItemPriority priority) => Priority = priority;

    public void SetDueDate(DateTimeOffset? dueDate) => DueDate = dueDate;

    public void ReplaceTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        _tags.AddRange(
            tags.Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct());
    }

    /// <summary>Called when the owning meeting is deleted; the task survives (§3.8).</summary>
    public void DetachFromMeeting()
    {
        MeetingId = null;
        SourceSegmentId = null;
    }

    public void MarkDeleted(DateTimeOffset deletedAt, Guid? deletedBy)
    {
        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }
}
