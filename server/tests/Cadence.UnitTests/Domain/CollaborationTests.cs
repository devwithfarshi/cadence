using Cadence.Domain.Collaboration;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class CollaborationTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid MeetingId = Guid.CreateVersion7();
    private static readonly Guid AuthorId = Guid.CreateVersion7();

    [Fact]
    public void AnEmptyComment_IsRejected()
    {
        Should.Throw<DomainException>(() => CreateComment("   "));
    }

    [Fact]
    public void Mentions_AreDeduplicated_SoNobodyIsNotifiedTwice()
    {
        var mentioned = Guid.CreateVersion7();

        var comment = Comment.Create(
            MeetingId,
            OrganizationId,
            AuthorId,
            "@priya @priya can you confirm?",
            mentions: [mentioned, mentioned]);

        comment.Mentions.ShouldHaveSingleItem();
    }

    [Fact]
    public void Editing_ReplacesTheMentionSet()
    {
        // Someone removed from an edited comment should stop being a notification target.
        var removed = Guid.CreateVersion7();
        var added = Guid.CreateVersion7();
        var comment = Comment.Create(
            MeetingId,
            OrganizationId,
            AuthorId,
            "@priya can you confirm?",
            mentions: [removed]);

        comment.Edit("@sam can you confirm?", [added]);

        comment.Mentions.ShouldBe([added]);
    }

    [Fact]
    public void ARestoredComment_HasNoDeletionTrace()
    {
        var comment = CreateComment();
        comment.MarkDeleted(DateTimeOffset.UtcNow, AuthorId);

        comment.Restore();

        comment.DeletedAt.ShouldBeNull();
        comment.DeletedBy.ShouldBeNull();
    }

    [Fact]
    public void ANotification_StartsUnreadAndUnarchived()
    {
        var notification = CreateNotification();

        notification.IsRead.ShouldBeFalse();
        notification.IsArchived.ShouldBeFalse();
    }

    [Fact]
    public void ANotificationNeedsATitle()
    {
        Should.Throw<DomainException>(() => CreateNotification(title: " "));
    }

    [Fact]
    public void AnActivityEntryStoresItsSummaryRendered()
    {
        // The feed must still read correctly after the meeting it refers to is renamed or deleted.
        var entry = ActivityLog.Record(
            OrganizationId,
            AuthorId,
            ActivityKind.MeetingCompleted,
            "Alex Rivera completed Quarterly planning",
            MeetingId,
            $"/meetings/{MeetingId}");

        entry.Summary.ShouldBe("Alex Rivera completed Quarterly planning");
        entry.TargetId.ShouldBe(MeetingId);
    }

    [Fact]
    public void AnActivityEntryNeedsASummary()
    {
        var act = () => ActivityLog.Record(
            OrganizationId,
            AuthorId,
            ActivityKind.MeetingCompleted,
            "  ");

        Should.Throw<DomainException>(act);
    }

    private static Comment CreateComment(string body = "Agreed — let's hold list price.") =>
        Comment.Create(MeetingId, OrganizationId, AuthorId, body);

    private static Notification CreateNotification(string title = "Summary ready") =>
        Notification.Create(
            Guid.CreateVersion7(),
            OrganizationId,
            NotificationKind.SummaryReady,
            title,
            "The summary for Quarterly planning is ready.",
            $"/meetings/{MeetingId}");
}
