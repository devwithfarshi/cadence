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
                RestoreCascadedDeletes(context, entry);
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

    /// <summary>
    /// Undoes the deletes EF cascaded from an entity that is only being soft-deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By the time an interceptor runs, EF has already decided that deleting a row deletes what
    /// hangs off it. Rewriting the parent to <c>Modified</c> does not revisit that, and the two
    /// consequences are quite different in severity:
    /// </para>
    /// <list type="number">
    /// <item>
    /// <b>Owned types crash the save.</b> An owned entity shares its owner's row, so a
    /// still-<c>Deleted</c> owned entry makes EF write <c>NULL</c> into its columns — and the first
    /// one that is <c>NOT NULL</c> fails the statement. Deleting an organization hit exactly this
    /// on <c>settings_name</c>.
    /// </item>
    /// <item>
    /// <b>Dependents are deleted for real.</b> A soft delete removes no row, so cascading to loaded
    /// children hard-deletes them — leaving a "restorable" workspace that comes back with no
    /// members, and unrecoverably so.
    /// </item>
    /// </list>
    /// <para>
    /// Only loaded navigations are walked, which is exactly the right scope: EF cannot have
    /// cascaded to a child it never tracked.
    /// </para>
    /// </remarks>
    private static void RestoreCascadedDeletes(DbContext context, EntityEntry entry)
    {
        foreach (var navigation in entry.Navigations)
        {
            foreach (var target in TargetsOf(context, navigation))
            {
                if (target.State != EntityState.Deleted)
                {
                    continue;
                }

                // Unchanged, not Modified: nothing about the child actually changed, and marking it
                // Modified would issue a pointless UPDATE of every column on every soft delete.
                target.State = EntityState.Unchanged;

                // Depth-first, because a dependent may own types of its own.
                RestoreCascadedDeletes(context, target);
            }
        }
    }

    private static IEnumerable<EntityEntry> TargetsOf(DbContext context, NavigationEntry navigation)
    {
        switch (navigation)
        {
            case ReferenceEntry { TargetEntry: { } target }:
                yield return target;
                break;

            case CollectionEntry { CurrentValue: { } items }:
                foreach (var item in items)
                {
                    yield return context.Entry(item);
                }

                break;

            default:
                break;
        }
    }

    private static bool HasModifiedTarget(ReferenceEntry reference) =>
        reference.TargetEntry?.State is EntityState.Added or EntityState.Modified;
}
