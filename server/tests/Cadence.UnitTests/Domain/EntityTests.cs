using Cadence.Domain.Common;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class EntityTests
{
    private sealed class Meeting : Entity
    {
        public Meeting()
        {
        }

        public Meeting(Guid id)
            : base(id)
        {
        }
    }

    private sealed class Document : Entity
    {
        public Document(Guid id)
            : base(id)
        {
        }
    }

    [Fact]
    public void NewEntity_GetsVersion7Uuid()
    {
        // Version is the high nibble of byte 6 in the big-endian (RFC 9562) layout.
        var version = new Meeting().Id.ToByteArray(bigEndian: true)[6] >> 4;

        version.ShouldBe(7);
    }

    [Fact]
    public void NewEntities_AreTimeOrdered_ByBigEndianBytes()
    {
        var first = new Meeting();
        Thread.Sleep(2); // v7 has millisecond timestamp resolution
        var second = new Meeting();

        // Ordering must be checked on the big-endian bytes, because that is how PostgreSQL's
        // `uuid` type compares — and the whole reason for v7 is that index inserts stay
        // append-mostly rather than fragmenting the B-tree (blueprint §3.4).
        //
        // Deliberately NOT Guid.CompareTo: .NET compares the first three fields as little-endian
        // integers, so it does not reflect v7's time ordering at all. Asserting with CompareTo
        // would fail here even though the ids are perfectly ordered in the database.
        var firstBytes = first.Id.ToByteArray(bigEndian: true);
        var secondBytes = second.Id.ToByteArray(bigEndian: true);

        firstBytes.AsSpan().SequenceCompareTo(secondBytes).ShouldBeLessThan(0);
    }

    [Fact]
    public void Entities_OfSameTypeAndId_AreEqual()
    {
        var id = Guid.CreateVersion7();

        new Meeting(id).ShouldBe(new Meeting(id));
        (new Meeting(id) == new Meeting(id)).ShouldBeTrue();
    }

    [Fact]
    public void Entities_OfDifferentTypes_AreNotEqual_EvenWithSameId()
    {
        // Two aggregates can legitimately hold the same id value; they are still different things.
        var id = Guid.CreateVersion7();

        new Meeting(id).Equals(new Document(id)).ShouldBeFalse();
    }

    [Fact]
    public void Entity_IsNeverEqualToNull()
    {
        var meeting = new Meeting();

        meeting.Equals(null).ShouldBeFalse();
        (meeting == null).ShouldBeFalse();
        (meeting != null).ShouldBeTrue();
    }
}
