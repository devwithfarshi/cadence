using Cadence.Application.Common.Messaging;
using Cadence.Domain.Common;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cadence.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Publishes the domain events raised during a unit of work, once it has actually been saved.
/// </summary>
/// <remarks>
/// <para>
/// Dispatch happens in <c>SavedChanges</c>, not <c>SavingChanges</c>: an event describes something
/// that <b>has happened</b>, and publishing before the commit would let a handler react to a
/// transaction that then rolls back — sending "your summary is ready" for a meeting that was never
/// stored.
/// </para>
/// <para>
/// Events are drained from the aggregates before publishing, so a handler that itself saves cannot
/// re-publish the same event and loop.
/// </para>
/// </remarks>
public sealed class DomainEventDispatchInterceptor(IPublisher publisher) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await DispatchAsync(eventData.Context, cancellationToken);
        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        // The synchronous path exists because EF offers it; Cadence saves asynchronously
        // everywhere, so blocking here is acceptable rather than duplicating the dispatch logic.
        DispatchAsync(eventData.Context, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    private async ValueTask DispatchAsync(DbContext? context, CancellationToken cancellationToken)
    {
        if (context is null)
        {
            return;
        }

        var aggregates = context.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(entry => entry.Entity.DomainEvents.Count != 0)
            .Select(entry => entry.Entity)
            .ToArray();

        if (aggregates.Length == 0)
        {
            return;
        }

        var events = aggregates.SelectMany(aggregate => aggregate.DomainEvents).ToArray();

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        foreach (var domainEvent in events)
        {
            await publisher.Publish(Wrap(domainEvent), cancellationToken);
        }
    }

    /// <summary>
    /// Boxes the event into <see cref="DomainEventNotification{TDomainEvent}"/> so the mediator can
    /// route it, without Domain having to know a mediator exists.
    /// </summary>
    private static object Wrap(DomainEvent domainEvent)
    {
        var notificationType = typeof(DomainEventNotification<>).MakeGenericType(domainEvent.GetType());
        return Activator.CreateInstance(notificationType, domainEvent)!;
    }
}
