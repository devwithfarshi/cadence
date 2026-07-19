using Cadence.Domain.Common;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class ValueObjectTests
{
    private sealed class DateRange : ValueObject
    {
        public DateRange(DateTimeOffset start, DateTimeOffset end)
        {
            Start = start;
            End = end;
        }

        public DateTimeOffset Start { get; }

        public DateTimeOffset End { get; }

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Start;
            yield return End;
        }
    }

    [Fact]
    public void ValuesWithSameComponents_AreEqual()
    {
        var start = DateTimeOffset.UtcNow;
        var end = start.AddHours(1);

        new DateRange(start, end).ShouldBe(new DateRange(start, end));
        new DateRange(start, end).GetHashCode().ShouldBe(new DateRange(start, end).GetHashCode());
    }

    [Fact]
    public void ValuesWithDifferentComponents_AreNotEqual()
    {
        var start = DateTimeOffset.UtcNow;

        new DateRange(start, start.AddHours(1))
            .ShouldNotBe(new DateRange(start, start.AddHours(2)));
    }
}
