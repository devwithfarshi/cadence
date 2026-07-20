namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// The claim names Cadence puts in its access tokens.
/// </summary>
/// <remarks>
/// Shared so the issuer (Infrastructure) and the readers (Api policies, <c>CurrentUser</c>) cannot
/// drift. A typo in one of these strings does not fail to compile — it silently produces a caller
/// with no organization, which the tenant filter then reads as "see nothing".
/// </remarks>
public static class CadenceClaims
{
    /// <summary>
    /// The <b>current</b> workspace this token is scoped to (§4.4).
    /// </summary>
    /// <remarks>
    /// Every tenant-scoped query derives from this. Switching workspace re-issues the token rather
    /// than taking an organization id from the request, because a caller-supplied id is an assertion
    /// and this one is signed.
    /// </remarks>
    public const string OrganizationId = "org";

    /// <summary>
    /// The caller's role <i>within that organization</i>, not a global one.
    /// </summary>
    /// <remarks>
    /// Baked into a 15-minute token, so a role change takes effect at the next refresh at the
    /// latest. That bounded staleness is the deliberate price of stateless authorization.
    /// </remarks>
    public const string Role = "role";

    /// <summary>Identifies the refresh-token family, so one session can be listed and revoked.</summary>
    public const string SessionId = "sid";
}
