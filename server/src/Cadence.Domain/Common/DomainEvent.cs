namespace Cadence.Domain.Common;

/// <summary>
/// Something that has already happened in the domain, named in the past tense.
/// </summary>
/// <remarks>
/// Events are how modules talk to each other (blueprint §7.1). A meeting ending does not call the
/// summaries module; it raises <c>MeetingCompleted</c> and the summaries module decides what that
/// means. That keeps the seam between modules real, so one can later be extracted without
/// unpicking direct calls.
/// <para>
/// Dispatch happens <b>after</b> <c>SaveChanges</c> succeeds, so a handler never reacts to a
/// transaction that subsequently rolls back.
/// </para>
/// </remarks>
public abstract record DomainEvent
{
    public Guid EventId { get; } = Guid.CreateVersion7();

    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
