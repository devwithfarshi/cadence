using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Cadence.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps audit columns and turns a delete of an <see cref="ISoftDeletable"/> into a soft delete.
/// </summary>
/// <remarks>
/// Both jobs belong here rather than in a handler. An audit trail maintained by hand rots on the
/// first forgotten assignment, and a hard delete that slips through is unrecoverable — the
/// interceptor cannot be forgotten (§3.5, §3.7).
/// </remarks>
public sealed class AuditingInterceptor(ICurrentUser currentUser, IDateTime dateTime)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Apply(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = dateTime.UtcNow;
        var actor = currentUser.Id;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable softDeletable)
            {
                SoftDelete(entry, softDeletable, now, actor);
                continue;
            }

            if (entry.Entity is not AuditableEntity auditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.SetCreated(now, actor);
                    break;

                // A modified owned/child entity leaves its parent Unchanged, so the parent's
                // updated_at would not move. Treating that as a modification keeps the aggregate's
                // timestamp honest — it is what "this record changed" means to a reader.
                case EntityState.Modified:
                case EntityState.Unchanged when entry.References.Any(HasModifiedTarget):
                    auditable.SetUpdated(now, actor);
                    break;

                default:
                    break;
            }
        }
    }

    private static void SoftDelete(
        EntityEntry entry,
        ISoftDeletable softDeletable,
        DateTimeOffset now,
        Guid? actor)
    {
        // Rewriting the state is what makes Remove() mean "soft delete" for these types, so callers
        // never branch on which kind of entity they are holding.
        entry.State = EntityState.Modified;
        softDeletable.MarkDeleted(now, actor);

        if (entry.Entity is AuditableEntity auditable)
        {
            auditable.SetUpdated(now, actor);
        }
    }

    private static bool HasModifiedTarget(ReferenceEntry reference) =>
        reference.TargetEntry?.State is EntityState.Added or EntityState.Modified;
}
