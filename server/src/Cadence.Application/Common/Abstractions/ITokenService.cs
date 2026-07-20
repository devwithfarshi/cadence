using Cadence.Domain.Enums;

namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Issues Cadence's own tokens once a Google identity has been verified.
/// </summary>
public interface ITokenService
{
    /// <summary>Mints a short-lived access token carrying the caller's workspace and role.</summary>
    AccessToken CreateAccessToken(AccessTokenRequest request);

    /// <summary>
    /// Generates a refresh token: the value handed to the client, and the hash stored.
    /// </summary>
    /// <remarks>
    /// The plaintext is returned once and never persisted, for the same reason a password is not.
    /// A database leak must not hand over live sessions (§4.3).
    /// </remarks>
    RefreshTokenPair CreateRefreshToken();

    /// <summary>Hashes a presented refresh token so it can be looked up against stored hashes.</summary>
    string HashRefreshToken(string token);

    /// <summary>
    /// Mints an opaque secret and the hash to store beside it — invitation links today, API keys
    /// when that module lands.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CreateRefreshToken"/> because these secrets carry their own
    /// lifetimes: an invitation expires in fourteen days and an API key never does, neither of which
    /// is the refresh window. Returning an expiry here would only invite a caller to use the wrong
    /// one.
    /// </remarks>
    OpaqueToken CreateOpaqueToken();
}

/// <summary>
/// A generated secret and its stored hash.
/// </summary>
/// <remarks>
/// <c>Value</c> exists exactly once, in the response or the email that carries it. Only
/// <c>Hash</c> is ever persisted, so a database leak yields nothing redeemable.
/// </remarks>
public sealed record OpaqueToken(string Value, string Hash);

public sealed record AccessTokenRequest(
    Guid UserId,
    string Email,
    string Name,
    string? PictureUrl,
    Guid OrganizationId,
    UserRole Role,
    Guid SessionId);

/// <summary>A minted access token.</summary>
/// <remarks>
/// <c>ExpiresInSeconds</c> is returned so the client can refresh <i>before</i> expiry rather than
/// discovering it through a 401 mid-action.
/// </remarks>
public sealed record AccessToken(string Value, int ExpiresInSeconds, DateTimeOffset ExpiresAt);

public sealed record RefreshTokenPair(string Value, string Hash, DateTimeOffset ExpiresAt);
