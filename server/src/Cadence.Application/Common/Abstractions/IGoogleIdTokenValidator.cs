namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Verifies a Google ID token and returns what it asserts.
/// </summary>
/// <remarks>
/// Validation is offline against cached JWKS — the token is a self-contained signed assertion, so
/// no call to Google is needed per sign-in (§4.2). Cadence never sees a Google <i>access</i> token
/// and never handles the consent redirect.
/// </remarks>
public interface IGoogleIdTokenValidator
{
    /// <summary>
    /// Returns the verified payload, or <c>null</c> if the token is not valid for this application.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception: an invalid token is an expected outcome of a public endpoint,
    /// not a defect, and it must produce a 401 without a stack trace in the log.
    /// </remarks>
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default);
}

/// <summary>What a verified Google ID token asserts about its bearer.</summary>
/// <remarks>
/// <c>Subject</c> is Google's <c>sub</c> — the stable identity key. It is what
/// <c>external_login</c> is keyed on, never the email: an address can be reassigned inside a
/// Workspace domain, so keying on email would eventually hand one person another's account.
/// <para>
/// <c>EmailVerified</c> gates linking to an existing account by address. Skipping that check is a
/// known account-takeover vector — anyone able to obtain an unverified token for
/// <c>victim@corp.com</c> would inherit the account (§4.2).
/// </para>
/// </remarks>
public sealed record GoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string Name,
    string? PictureUrl,
    string? HostedDomain);
