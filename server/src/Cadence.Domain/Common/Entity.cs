namespace Cadence.Domain.Common;

/// <summary>
/// Base type for every persisted entity.
/// </summary>
/// <remarks>
/// The identifier is a <b>UUIDv7</b> (blueprint §3.4): globally unique and safe to expose like a
/// UUIDv4, but time-ordered, so B-tree inserts stay append-mostly instead of fragmenting the index.
/// It is generated in the constructor rather than by the database so an aggregate is fully valid —
/// and can raise events referencing its own id — before it is ever saved.
/// </remarks>
public abstract class Entity : IEquatable<Entity>
{
    protected Entity() => Id = Guid.CreateVersion7();

    protected Entity(Guid id) => Id = id;

    public Guid Id { get; protected set; }

    /// <summary>
    /// Entities are compared by identity, never by value: two instances loaded separately still
    /// represent the same thing, and a changed property must not make them unequal.
    /// </summary>
    public bool Equals(Entity? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        // Different aggregate types can legitimately share an id value.
        return GetType() == other.GetType() && Id == other.Id;
    }

    public override bool Equals(object? obj) => Equals(obj as Entity);

    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    public static bool operator ==(Entity? left, Entity? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(Entity? left, Entity? right) => !(left == right);
}
