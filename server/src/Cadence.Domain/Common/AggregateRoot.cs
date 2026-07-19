namespace Cadence.Domain.Common;

/// <summary>
/// The entry point to a cluster of entities that change together and are saved as one unit.
/// </summary>
/// <remarks>
/// Only aggregate roots get a repository. Children (a <c>Participant</c>, a <c>Bookmark</c>) are
/// reached through their root, which is what keeps invariants enforceable — the root is the only
/// place that can see enough state to check them.
/// </remarks>
public abstract class AggregateRoot : AuditableEntity
{
    private readonly List<DomainEvent> _domainEvents = [];

    protected AggregateRoot()
    {
    }

    protected AggregateRoot(Guid id)
        : base(id)
    {
    }

    /// <summary>Events raised but not yet dispatched. Drained by the persistence interceptor.</summary>
    public IReadOnlyCollection<DomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(DomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
