namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// One atomic commit of everything a handler changed.
/// </summary>
/// <remarks>
/// Implemented by the DbContext. Handlers call <see cref="SaveChangesAsync"/>; they never open a
/// transaction — <c>TransactionBehavior</c> does that for commands, so a handler that touches three
/// aggregates commits atomically without knowing transactions exist (§9.2).
/// </remarks>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a transaction, committing on success and rolling
    /// back on any exception. Used by the pipeline; a handler should not need to call it.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default);
}
