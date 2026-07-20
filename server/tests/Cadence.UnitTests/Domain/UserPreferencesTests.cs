using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class UserPreferencesTests
{
    [Fact]
    public void RecentMeetings_PutTheMostRecentFirst()
    {
        var preferences = Create();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();

        preferences.RecordMeetingVisit(first);
        preferences.RecordMeetingVisit(second);

        preferences.RecentMeetingIds.ShouldBe([second, first]);
    }

    [Fact]
    public void RevisitingAMeeting_PromotesItInsteadOfDuplicatingIt()
    {
        var preferences = Create();
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        preferences.RecordMeetingVisit(first);
        preferences.RecordMeetingVisit(second);

        preferences.RecordMeetingVisit(first);

        preferences.RecentMeetingIds.ShouldBe([first, second]);
    }

    [Fact]
    public void RecentMeetings_AreCapped_SoTheListStaysAShortcut()
    {
        var preferences = Create();
        foreach (var _ in Enumerable.Range(0, 25))
        {
            preferences.RecordMeetingVisit(Guid.CreateVersion7());
        }

        preferences.RecentMeetingIds.Count.ShouldBe(10);
    }

    [Fact]
    public void ABlankSearchTerm_IsNotRecorded()
    {
        var preferences = Create();

        preferences.RecordSearch("   ");

        preferences.RecentSearches.ShouldBeEmpty();
    }

    [Fact]
    public void EmailNotifications_DefaultToASubsetOfInAppOnes()
    {
        // Anything that sends mail defaults to off; a new NotificationKind must be opted into.
        var preferences = Create();

        preferences.Notifications.Email.Length
            .ShouldBeLessThan(preferences.Notifications.InApp.Length);
        preferences.Notifications.Allows(NotificationKind.MeetingReminder, viaEmail: false).ShouldBeTrue();
        preferences.Notifications.Allows(NotificationKind.MeetingReminder, viaEmail: true).ShouldBeFalse();
    }

    [Fact]
    public void ExtractedActionItems_DefaultToRequiringReview()
    {
        // The model should never silently assign work to a colleague.
        Create().Ai.RequireActionItemReview.ShouldBeTrue();
    }

    [Fact]
    public void TwoDefaultNotificationPreferences_AreEqual_RegardlessOfOrder()
    {
        var a = NotificationPreferences.Create(
            [NotificationKind.Mention, NotificationKind.TaskAssigned],
            [NotificationKind.Mention]);
        var b = NotificationPreferences.Create(
            [NotificationKind.TaskAssigned, NotificationKind.Mention],
            [NotificationKind.Mention]);

        a.ShouldBe(b);
    }

    private static UserPreferences Create() => UserPreferences.CreateDefault(Guid.CreateVersion7());
}
