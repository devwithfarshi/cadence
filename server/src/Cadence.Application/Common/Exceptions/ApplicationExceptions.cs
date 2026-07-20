namespace Cadence.Application.Common.Exceptions;

// These are for the cases a handler genuinely cannot continue past — a missing aggregate it was
// told to load, a caller without permission. Ordinary expected failures still return Result<T>
// (§10.3); throwing for those is what makes a stack trace in the log stop meaning "defect".
//
// The API's exception handler maps each to a status, so no handler mentions HTTP.

/// <summary>The requested resource does not exist, or is invisible to this tenant. → 404</summary>
/// <remarks>
/// Deliberately does not distinguish "absent" from "not yours". Saying "this exists but is not
/// yours" confirms the id to someone who should not be able to learn it.
/// </remarks>
public sealed class NotFoundException : Exception
{
    public NotFoundException()
        : base("The requested resource was not found.")
    {
    }

    public NotFoundException(string message)
        : base(message)
    {
    }

    public NotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static NotFoundException For(string resource, Guid id) =>
        new($"{resource} '{id}' was not found.");
}

/// <summary>Authenticated, but not allowed to do this. → 403</summary>
public sealed class ForbiddenException : Exception
{
    public ForbiddenException()
        : base("You do not have permission to perform this action.")
    {
    }

    public ForbiddenException(string message)
        : base(message)
    {
    }

    public ForbiddenException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The request contradicts current state. → 409</summary>
public sealed class ConflictException : Exception
{
    public ConflictException()
        : base("The request conflicts with the current state of the resource.")
    {
    }

    public ConflictException(string message)
        : base(message)
    {
    }

    public ConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
