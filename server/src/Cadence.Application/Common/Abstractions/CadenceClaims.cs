namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// The claim names Cadence puts in its access tokens.
/// </summary>
/// <remarks>
/// Shared so the issuer (Infrastructure) and the reader (Api) cannot drift. A typo in one of these
/// strings does not fail to compile — it silently produces a caller with no organization, which the
/// tenant filter then reads as "see nothing".
/// </remarks>
public static class CadenceClaims
{
    /// <summary>The workspace this token is scoped to. Switching workspace re-issues the token.</summary>
    public const string OrganizationId = "org_id";

    /// <summary>The caller's role <i>within that organization</i>, not a global one.</summary>
    public const string Role = "org_role";

    /// <summary>Identifies the token itself, so a single session can be revoked.</summary>
    public const string SessionId = "sid";
}
