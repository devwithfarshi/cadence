using System.Collections.Concurrent;
using System.Reflection;
using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Cadence.Infrastructure.Persistence;

/// <summary>
/// Resolves and invokes the handlers registered for each raised event.
/// </summary>
public sealed class DomainEventDispatcher(
    IServiceProvider services,
    ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    // The reflection is done once per event type, not once per event. Without the cache a busy
    // meeting-completed batch would re-resolve the same MethodInfo on every row.
    private static readonly ConcurrentDictionary<Type, DispatchPlan> Plans = new();

    public async Task DispatchAsync(
        IReadOnlyCollection<DomainEvent> domainEvents,
        CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var plan = Plans.GetOrAdd(domainEvent.GetType(), DispatchPlan.For);

            foreach (var handler in plan.ResolveHandlers(services))
            {
                await InvokeAsync(plan, handler, domainEvent, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Runs one handler, logging and swallowing its failure.
    /// </summary>
    /// <remarks>
    /// Dispatch happens after the commit, so throwing here cannot roll the change back — it would
    /// only turn a succeeded write into a 500 the client would retry, duplicating the work. One
    /// failing subscriber must not stop the others either. Anything that genuinely must not be lost
    /// is queued as a job by its handler, where Hangfire owns the retry.
    /// </remarks>
    private async Task InvokeAsync(
        DispatchPlan plan,
        object handler,
        DomainEvent domainEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            await plan.InvokeAsync(handler, domainEvent, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Handler {Handler} failed for {EventType} ({EventId}). The originating change is "
                + "already committed and is not rolled back.",
                handler.GetType().Name,
                domainEvent.GetType().Name,
                domainEvent.EventId);
        }
    }

    private sealed record DispatchPlan(Type HandlerType, MethodInfo Handle)
    {
        public static DispatchPlan For(Type eventType)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);
            var handle = handlerType.GetMethod(nameof(IDomainEventHandler<DomainEvent>.HandleAsync))!;

            return new DispatchPlan(handlerType, handle);
        }

        public IEnumerable<object> ResolveHandlers(IServiceProvider services) =>
            ((IEnumerable<object?>)services.GetServices(HandlerType)).OfType<object>();

        public Task InvokeAsync(object handler, DomainEvent domainEvent, CancellationToken cancellationToken) =>
            (Task)Handle.Invoke(handler, [domainEvent, cancellationToken])!;
    }
}
