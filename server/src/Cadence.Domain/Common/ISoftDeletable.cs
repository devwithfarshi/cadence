namespace Cadence.Domain.Common;

/// <summary>
/// Marks an entity that is hidden rather than removed when deleted (blueprint §3.7).
/// </summary>
/// <remarks>
/// Applied to records a user can destroy but which the business may still need: meetings,
/// transcripts, action items, documents. <c>CadenceDbContext</c> attaches a global query
/// filter to every implementor, so a soft-deleted row is invisible to ordinary queries without any
/// handler remembering to exclude it.
/// <para>
/// Two consequences worth knowing: unique indexes on these tables must be partial
/// (<c>WHERE deleted_at IS NULL</c>), or a deleted row still blocks re-use of its key; and a
/// recurring job hard-deletes rows past the retention window so the tables do not grow forever.
/// </para>
/// </remarks>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; }

    Guid? DeletedBy { get; }

    void MarkDeleted(DateTimeOffset deletedAt, Guid? deletedBy);

    void Restore();
}
