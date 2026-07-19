namespace Cadence.Application.Common.Models;

/// <summary>
/// Why an operation failed, in a form the API layer can turn into RFC 9457 problem+json.
/// </summary>
/// <param name="Code">
/// A stable, machine-readable identifier such as <c>meeting.not_found</c>. The client switches on
/// this; it never parses <paramref name="Description"/>.
/// </param>
/// <param name="Description">Human-readable text, safe to show a user.</param>
/// <param name="Type">Maps to an HTTP status in one place, in the API layer.</param>
public readonly record struct Error(string Code, string Description, ErrorType Type)
{
    /// <summary>The absence of an error. Never returned from a failed result.</summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);
}

/// <summary>
/// The kind of failure, which is what decides the HTTP status.
/// </summary>
/// <remarks>
/// Handlers classify failures in these terms and never mention status codes — that keeps the
/// Application layer free of HTTP, and keeps the mapping in exactly one place (§10.2).
/// </remarks>
public enum ErrorType
{
    /// <summary>Something genuinely went wrong. → 500</summary>
    Failure,

    /// <summary>The input is malformed or breaks a rule. → 400</summary>
    Validation,

    /// <summary>No credentials, or credentials that no longer work. → 401</summary>
    Unauthorized,

    /// <summary>Authenticated, but not allowed to do this. → 403</summary>
    Forbidden,

    /// <summary>The resource does not exist, or is invisible to this tenant. → 404</summary>
    NotFound,

    /// <summary>The request contradicts current state. → 409</summary>
    Conflict,
}
