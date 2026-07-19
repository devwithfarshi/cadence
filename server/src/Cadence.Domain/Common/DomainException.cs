namespace Cadence.Domain.Common;

/// <summary>
/// A business rule was violated.
/// </summary>
/// <remarks>
/// Distinct from a validation failure. Validation asks "is this request well-formed?" and is
/// answered before a handler runs (400). A domain exception answers "is this operation legal given
/// the current state?" — demoting the last owner, ending a meeting that never started — and can
/// only be known once the aggregate is loaded. The global handler maps it to <b>422</b>
/// (blueprint §10.3), so the client can tell the two apart.
/// </remarks>
public class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public DomainException()
    {
    }

    /// <summary>Throws when <paramref name="condition"/> is true.</summary>
    public static void ThrowIf(bool condition, string message)
    {
        if (condition)
        {
            throw new DomainException(message);
        }
    }
}
