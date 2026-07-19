using Cadence.Domain.Common;
using Mediator;

namespace Cadence.Application.Common.Messaging;

/// <summary>
/// Carries a <see cref="DomainEvent"/> onto the mediator's notification pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper exists so <c>DomainEvent</c> does not have to implement <c>INotification</c>. Domain
/// depends on nothing — not even a mediator abstraction — and an architecture test fails the build
/// if that changes. The cost is one generic indirection; the benefit is that domain events stay
/// plain records that unit tests can assert on without any infrastructure.
/// </para>
/// <para>
/// Handlers subscribe to the closed form:
/// <c>INotificationHandler&lt;DomainEventNotification&lt;MeetingCompleted&gt;&gt;</c>.
/// </para>
/// </remarks>
public sealed record DomainEventNotification<TDomainEvent>(TDomainEvent DomainEvent) : INotification
    where TDomainEvent : Domain.Common.DomainEvent;
