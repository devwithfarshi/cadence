using FluentValidation.Results;

namespace Cadence.Application.Common.Exceptions;

/// <summary>
/// Thrown by <c>ValidationBehavior</c> when a request fails its validators.
/// </summary>
/// <remarks>
/// An exception rather than a <c>Result</c>, unlike every other failure. Validation runs before the
/// handler, so there is no handler return value to put a failure into — and the API's exception
/// handler turns this into the one RFC 9457 shape with per-field errors that the client's form
/// components already read (§10.1).
/// </remarks>
public sealed class ValidationException : Exception
{
    private static readonly IReadOnlyDictionary<string, string[]> Empty =
        new Dictionary<string, string[]>(StringComparer.Ordinal);

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("One or more validation errors occurred.") =>
        Errors = failures
            .GroupBy(failure => failure.PropertyName, failure => failure.ErrorMessage)
            .ToDictionary(
                group => group.Key,
                group => group.Distinct().ToArray(),
                StringComparer.Ordinal);

    public ValidationException()
        : this([])
    {
    }

    public ValidationException(string message)
        : base(message) => Errors = Empty;

    public ValidationException(string message, Exception innerException)
        : base(message, innerException) => Errors = Empty;

    /// <summary>Field name → messages, ready to become <c>problem+json</c>'s <c>errors</c>.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
