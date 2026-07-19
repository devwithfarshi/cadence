using Cadence.Domain.Common;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class AggregateRootTests
{
    private sealed record MeetingCompleted(Guid MeetingId) : DomainEvent;

    private sealed class Meeting : AggregateRoot
    {
        public void Complete() => Raise(new MeetingCompleted(Id));
    }

    [Fact]
    public void RaisedEvents_AreCollectedUntilCleared()
    {
        var meeting = new Meeting();

        meeting.DomainEvents.ShouldBeEmpty();

        meeting.Complete();
        meeting.DomainEvents.Count.ShouldBe(1);
        meeting.DomainEvents.Single().ShouldBeOfType<MeetingCompleted>();

        // The persistence interceptor drains events only after SaveChanges succeeds, so a
        // rolled-back transaction never publishes anything (blueprint §7.1).
        meeting.ClearDomainEvents();
        meeting.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void SetCreated_AlsoSeedsUpdatedFields()
    {
        var meeting = new Meeting();
        var at = DateTimeOffset.UtcNow;
        var by = Guid.CreateVersion7();

        meeting.SetCreated(at, by);

        meeting.CreatedAt.ShouldBe(at);
        meeting.CreatedBy.ShouldBe(by);

        // A row that has never been updated still needs a sensible updated_at, or "recently
        // changed" sorts push brand-new rows to the bottom.
        meeting.UpdatedAt.ShouldBe(at);
        meeting.UpdatedBy.ShouldBe(by);
    }

    [Fact]
    public void SetUpdated_DoesNotDisturbCreationAudit()
    {
        var meeting = new Meeting();
        var created = DateTimeOffset.UtcNow;
        var creator = Guid.CreateVersion7();
        meeting.SetCreated(created, creator);

        var editor = Guid.CreateVersion7();
        meeting.SetUpdated(created.AddMinutes(5), editor);

        meeting.CreatedAt.ShouldBe(created);
        meeting.CreatedBy.ShouldBe(creator);
        meeting.UpdatedBy.ShouldBe(editor);
    }
}
