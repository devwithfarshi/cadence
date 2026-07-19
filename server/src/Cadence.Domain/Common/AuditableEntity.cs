namespace Cadence.Domain.Common;

/// <summary>
/// An entity that records who created and last changed it, and when.
/// </summary>
/// <remarks>
/// These fields are written centrally by an EF <c>SaveChangesInterceptor</c> (blueprint §3.5),
/// never assigned in a handler. Hand-written auditing is how audit trails silently rot: one
/// forgotten assignment and the row lies about its own history. The setters are therefore
/// internal-by-convention — exposed only through <see cref="SetCreated"/> / <see cref="SetUpdated"/>
/// so the interceptor is the single writer.
/// </remarks>
public abstract class AuditableEntity : Entity
{
    protected AuditableEntity()
    {
    }

    protected AuditableEntity(Guid id)
        : base(id)
    {
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void SetCreated(DateTimeOffset at, Guid? by)
    {
        CreatedAt = at;
        CreatedBy = by;
        UpdatedAt = at;
        UpdatedBy = by;
    }

    public void SetUpdated(DateTimeOffset at, Guid? by)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }
}
