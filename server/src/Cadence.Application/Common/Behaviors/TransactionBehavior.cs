using Cadence.Application.Common.Abstractions;
using Mediator;

namespace Cadence.Application.Common.Behaviors;

/// <summary>
/// Wraps a command in one database transaction.
/// </summary>
/// <remarks>
/// <para>
/// Constrained to Mediator's <see cref="IBaseCommand"/>, so queries never open a transaction — a
/// read that takes a transaction holds a connection longer than it needs and can block writers for
/// nothing.
/// </para>
/// <para>
/// A handler that touches three aggregates therefore commits atomically without containing a single
/// line about transactions. Domain events are dispatched by the persistence interceptor inside the
/// same transaction, so an event can never describe a state that was rolled back (§9.2).
/// </para>
/// </remarks>
public sealed class TransactionBehavior<TMessage, TResponse>(IUnitOfWork unitOfWork)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, IBaseCommand
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken) =>
        await unitOfWork.ExecuteInTransactionAsync(
            async token => await next(message, token),
            cancellationToken);
}
