using Cadence.Domain.Common;

namespace Cadence.Domain.Work.Events;

public sealed record ActionItemAssigned(
    Guid ActionItemId,
    Guid OrganizationId,
    Guid AssigneeId,
    Guid ActorId) : DomainEvent;

public sealed record ActionItemCompleted(
    Guid ActionItemId,
    Guid OrganizationId,
    Guid? AssigneeId) : DomainEvent;
