using System.ComponentModel.DataAnnotations;

namespace Cadence.Infrastructure.Configuration;

/// <summary>
/// Signing and lifetime settings for Cadence's own tokens.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>
    /// HMAC signing key.
    /// </summary>
    /// <remarks>
    /// At least 32 bytes, because HS256 offers no more security than the key it is given and a short
    /// key is brute-forceable offline by anyone holding one issued token. Validated at startup, so a
    /// missing key stops the process with a clear message rather than surfacing as a confusing 500
    /// on the first sign-in an hour later (§11.2).
    /// </remarks>
    [Required]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters. Generate one with: openssl rand -base64 48")]
    public string SigningKey { get; init; } = string.Empty;

    [Required]
    public string Issuer { get; init; } = "cadence";

    [Required]
    public string Audience { get; init; } = "cadence-client";

    /// <summary>
    /// Access-token lifetime.
    /// </summary>
    /// <remarks>
    /// Short by design. The token carries organization and role, so it cannot be revoked mid-life —
    /// 15 minutes is the window in which a role change or removal has not yet taken effect (§4.4).
    /// </remarks>
    [Range(1, 1440)]
    public int AccessTokenMinutes { get; init; } = 15;

    /// <summary>Refresh-token lifetime, sliding through rotation.</summary>
    [Range(1, 365)]
    public int RefreshTokenDays { get; init; } = 30;
}

/// <summary>
/// Google sign-in settings.
/// </summary>
public sealed class GoogleAuthOptions
{
    public const string SectionName = "Google";

    /// <summary>
    /// The OAuth client id an ID token must be addressed to.
    /// </summary>
    /// <remarks>
    /// Validating the audience is what stops a token minted for <i>another</i> Google application
    /// being replayed here. A signature check alone would accept any valid Google token from
    /// anywhere.
    /// </remarks>
    [Required]
    public string ClientId { get; init; } = string.Empty;

    /// <summary>
    /// Optional allow-list of Workspace domains (the token's <c>hd</c> claim).
    /// </summary>
    /// <remarks>
    /// Empty means any Google account may sign in, which is the self-serve default. Setting it
    /// restricts the deployment to one or more companies.
    /// </remarks>
    public string[] AllowedHostedDomains { get; init; } = [];
}
