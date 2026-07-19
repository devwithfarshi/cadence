using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Infrastructure.Persistence.Repositories;

/// <summary>
/// The generic write-side repository, one instance per aggregate type.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately tiny. Everything here goes through the context's global query filters, so a lookup
/// cannot reach another tenant's row or a soft-deleted one.
/// </para>
/// <para>
/// Note there is no <c>SaveChanges</c> on the repository. Saving is the unit of work's job — a
/// repository that saves turns every call into its own transaction, which is exactly what makes
/// multi-aggregate commands non-atomic (§9.2).
/// </para>
/// </remarks>
public class EfRepository<TAggregate>(CadenceDbContext context) : IRepository<TAggregate>
    where TAggregate : AggregateRoot
{
    protected CadenceDbContext Context { get; } = context;

    protected DbSet<TAggregate> Set => Context.Set<TAggregate>();

    public virtual async Task<TAggregate?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        await Set.FirstOrDefaultAsync(aggregate => aggregate.Id == id, cancellationToken);

    public virtual async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default) =>
        await Set.AnyAsync(aggregate => aggregate.Id == id, cancellationToken);

    public virtual async Task AddAsync(TAggregate aggregate, CancellationToken cancellationToken = default) =>
        await Set.AddAsync(aggregate, cancellationToken);

    /// <summary>
    /// For an <see cref="ISoftDeletable"/> aggregate the auditing interceptor rewrites this into an
    /// update, so callers never branch on which kind they are holding.
    /// </summary>
    public virtual void Remove(TAggregate aggregate) => Set.Remove(aggregate);
}
