using Cadence.Domain.Common;

namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Handles one kind of domain event. A module subscribes by implementing this.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately <b>not</b> the mediator's notification pipeline. Two reasons:
/// </para>
/// <list type="number">
/// <item>
/// Routing events through Mediator would need <c>DomainEvent</c> to implement
/// <c>INotification</c> — a package reference in Domain, which depends on nothing — or an
/// open-generic wrapper, which Mediator's source generator cannot register at all (it emits code
/// referencing the unbound type parameter and fails the build).
/// </item>
/// <item>
/// Events do not want the request pipeline. Validation, caching and transaction behaviors are for
/// requests; an event that has already happened needs typed fan-out and nothing else.
/// </item>
/// </list>
/// <para>
/// Handlers run <b>after</b> the commit, so an event never describes a state that was rolled back.
/// The corollary is that a failing handler cannot undo the change that raised it — a handler that
/// must not be lost belongs in a background job, queued by the handler (§14.3).
/// </para>
/// </remarks>
public interface IDomainEventHandler<in TDomainEvent>
    where TDomainEvent : DomainEvent
{
    Task HandleAsync(TDomainEvent domainEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fans a batch of raised events out to their handlers.
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(
        IReadOnlyCollection<DomainEvent> domainEvents,
        CancellationToken cancellationToken = default);
}
