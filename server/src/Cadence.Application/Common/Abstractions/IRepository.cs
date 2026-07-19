using Cadence.Domain.Common;

namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// The small set of operations every aggregate needs.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately minimal, and deliberately not <c>IQueryable</c>. A repository that returns
/// <c>IQueryable</c> leaks query construction to callers, which is exactly the N+1 and over-fetching
/// problem repositories were meant to prevent. Module repositories add intent-revealing methods
/// (<c>GetWithParticipantsAsync</c>) instead (§9.1).
/// </para>
/// <para>
/// <b>Read queries do not go through here at all.</b> They project straight from the DbContext with
/// <c>AsNoTracking()</c> + <c>Select</c>, which is faster and lets each read fetch exactly its own
/// columns.
/// </para>
/// </remarks>
public interface IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    Task<TAggregate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the aggregate. For an <see cref="ISoftDeletable"/> type the interceptor converts this
    /// into a soft delete, so callers never branch on which kind they are holding (§3.7).
    /// </summary>
    void Remove(TAggregate aggregate);
}
