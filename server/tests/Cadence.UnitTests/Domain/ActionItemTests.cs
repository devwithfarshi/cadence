using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Work;
using Cadence.Domain.Work.Events;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class ActionItemTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid CreatorId = Guid.CreateVersion7();

    [Fact]
    public void CompletingAnItem_StampsCompletedAt()
    {
        var item = Create();

        item.ChangeStatus(ActionItemStatus.Done);

        item.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public void ReopeningAnItem_ClearsCompletedAt()
    {
        // Analytics counts completions by `completed_at`; a stale timestamp on a reopened item
        // would inflate the throughput chart.
        var item = Create();
        item.ChangeStatus(ActionItemStatus.Done);

        item.ChangeStatus(ActionItemStatus.InProgress);

        item.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void CompletingAnAlreadyCompletedItem_DoesNotRaiseASecondEvent()
    {
        var item = Create();
        item.ChangeStatus(ActionItemStatus.Done);
        item.ClearDomainEvents();

        item.ChangeStatus(ActionItemStatus.Done);

        item.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void CreatingAnAssignedItem_RaisesTheAssignmentEvent()
    {
        var assignee = Guid.CreateVersion7();

        var item = Create(assigneeId: assignee);

        item.DomainEvents
            .OfType<ActionItemAssigned>()
            .ShouldHaveSingleItem()
            .AssigneeId.ShouldBe(assignee);
    }

    [Fact]
    public void DetachFromMeeting_LeavesTheTaskIntact()
    {
        // Deleting a meeting must not delete the work it produced (blueprint §3.8).
        var meetingId = Guid.CreateVersion7();
        var item = ActionItem.FromMeeting(
            OrganizationId,
            CreatorId,
            meetingId,
            Guid.CreateVersion7(),
            "Send the revised forecast",
            assigneeId: null,
            ActionItemPriority.High);

        item.DetachFromMeeting();

        item.MeetingId.ShouldBeNull();
        item.SourceSegmentId.ShouldBeNull();
        item.Title.ShouldBe("Send the revised forecast");
    }

    [Fact]
    public void AnEmptyTitle_IsRejected()
    {
        Should.Throw<DomainException>(() => Create(title: "   "));
    }

    private static ActionItem Create(string title = "Draft the pricing memo", Guid? assigneeId = null) =>
        ActionItem.Create(OrganizationId, CreatorId, title, assigneeId: assigneeId);
}
