namespace Cadence.Domain.Common;

/// <summary>
/// A type with no identity, compared entirely by its components.
/// </summary>
/// <remarks>
/// Two <c>EmailAddress</c> values holding the same string <i>are</i> the same value — unlike two
/// users who happen to share a name. Value objects are immutable, so they can be shared freely and
/// cannot drift into an invalid state after construction.
/// </remarks>
public abstract class ValueObject : IEquatable<ValueObject>
{
    /// <summary>The components that define equality, in a stable order.</summary>
    protected abstract IEnumerable<object?> GetEqualityComponents();

    public bool Equals(ValueObject? other)
    {
        if (other is null || other.GetType() != GetType())
        {
            return false;
        }

        return GetEqualityComponents().SequenceEqual(other.GetEqualityComponents());
    }

    public override bool Equals(object? obj) => Equals(obj as ValueObject);

    public override int GetHashCode() =>
        GetEqualityComponents()
            .Aggregate(default(HashCode), (hash, component) =>
            {
                hash.Add(component);
                return hash;
            })
            .ToHashCode();

    public static bool operator ==(ValueObject? left, ValueObject? right) =>
        left?.Equals(right) ?? right is null;

    public static bool operator !=(ValueObject? left, ValueObject? right) => !(left == right);
}
