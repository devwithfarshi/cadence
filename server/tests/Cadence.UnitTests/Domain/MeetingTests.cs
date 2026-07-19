using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using Cadence.Domain.Meetings.Events;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class MeetingTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid OrganizerId = Guid.CreateVersion7();
    private static readonly DateTimeOffset StartsAt = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Schedule_RejectsAnEndBeforeTheStart()
    {
        var act = () => Schedule(StartsAt, StartsAt.AddMinutes(-1));

        Should.Throw<DomainException>(act);
    }

    [Fact]
    public void Schedule_RejectsAZeroLengthMeeting()
    {
        // The CHECK constraint is `ends_at > starts_at`, not `>=`; the entity must agree.
        var act = () => Schedule(StartsAt, StartsAt);

        Should.Throw<DomainException>(act);
    }

    [Fact]
    public void Complete_QueuesSummarisation_OnlyWhenSomethingWasRecorded()
    {
        var recorded = Schedule();
        recorded.StartRecording();
        recorded.Complete(1800);

        var notRecorded = Schedule();
        notRecorded.Complete(1800);

        recorded.RecordingStatus.ShouldBe(RecordingStatus.Recorded);
        recorded.SummaryStatus.ShouldBe(SummaryStatus.Queued);

        notRecorded.RecordingStatus.ShouldBe(RecordingStatus.NotRecorded);
        notRecorded.SummaryStatus.ShouldBe(SummaryStatus.None);
    }

    [Fact]
    public void Complete_RaisesTheEventThatStartsThePipeline()
    {
        var meeting = Schedule();

        meeting.Complete(2700);

        meeting.DomainEvents
            .OfType<MeetingCompleted>()
            .ShouldHaveSingleItem()
            .DurationSeconds.ShouldBe(2700);
    }

    [Fact]
    public void Complete_IsRejectedTwice_SoThePipelineCannotBeStartedAgain()
    {
        var meeting = Schedule();
        meeting.Complete(600);

        Should.Throw<DomainException>(() => meeting.Complete(600));
    }

    [Fact]
    public void SetTalkTime_NormalisesToRatiosThatSumToOne()
    {
        var meeting = Schedule();
        var alex = Guid.CreateVersion7();
        var priya = Guid.CreateVersion7();
        meeting.AddParticipant(alex, "Alex Rivera", "alex@northwind.io", ParticipantRole.Host);
        meeting.AddParticipant(priya, "Priya Nair", "priya@northwind.io", ParticipantRole.Attendee);

        meeting.SetTalkTime(new Dictionary<Guid, double> { [alex] = 300, [priya] = 100 });

        meeting.Participants.Sum(participant => participant.TalkTimeRatio).ShouldBe(1.0, 0.0001);
        meeting.Participants.First(p => p.UserId == alex).TalkTimeRatio.ShouldBe(0.75, 0.0001);
    }

    [Fact]
    public void SetTalkTime_LeavesRatiosAloneWhenNobodySpoke()
    {
        // A silent or failed recording must not divide by zero, and must not claim 0% for people
        // whose share is simply unknown.
        var meeting = Schedule();
        var alex = Guid.CreateVersion7();
        meeting.AddParticipant(alex, "Alex Rivera", "alex@northwind.io", ParticipantRole.Host);

        meeting.SetTalkTime(new Dictionary<Guid, double> { [alex] = 0 });

        meeting.Participants.Single().TalkTimeRatio.ShouldBe(0);
    }

    [Fact]
    public void AddParticipant_RefusesTheSamePersonTwice()
    {
        var meeting = Schedule();
        var alex = Guid.CreateVersion7();
        meeting.AddParticipant(alex, "Alex Rivera", "alex@northwind.io", ParticipantRole.Host);

        Should.Throw<DomainException>(
            () => meeting.AddParticipant(alex, "Alex Rivera", "alex@northwind.io", ParticipantRole.Attendee));
    }

    [Fact]
    public void Cancel_IsRejectedForACompletedMeeting()
    {
        var meeting = Schedule();
        meeting.Complete(600);

        Should.Throw<DomainException>(meeting.Cancel);
    }

    private static Meeting Schedule(DateTimeOffset? startsAt = null, DateTimeOffset? endsAt = null) =>
        Meeting.Schedule(
            OrganizationId,
            OrganizerId,
            "Quarterly planning",
            "Roadmap review",
            startsAt ?? StartsAt,
            endsAt ?? StartsAt.AddHours(1),
            MeetingPlatform.Zoom);
}
