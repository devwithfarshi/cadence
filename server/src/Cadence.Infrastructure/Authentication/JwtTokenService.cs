using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Cadence.Infrastructure.Authentication;

/// <summary>
/// Issues Cadence's access and refresh tokens.
/// </summary>
public sealed class JwtTokenService(IOptions<JwtOptions> options, IDateTime clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken CreateAccessToken(AccessTokenRequest request)
    {
        var issuedAt = clock.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_options.AccessTokenMinutes);

        var claims = new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = request.UserId.ToString(),
            [JwtRegisteredClaimNames.Email] = request.Email,
            [JwtRegisteredClaimNames.Name] = request.Name,

            // Organization and role travel in the token so authorization needs no database round
            // trip. The cost is that both are fixed for the token's lifetime (§4.4).
            [CadenceClaims.OrganizationId] = request.OrganizationId.ToString(),
            [CadenceClaims.Role] = request.Role.ToString().ToLowerInvariant(),
            [CadenceClaims.SessionId] = request.SessionId.ToString(),

            // Distinct per token, so a specific one can be denylisted if that is ever needed.
            [JwtRegisteredClaimNames.Jti] = Guid.CreateVersion7().ToString(),
        };

        if (!string.IsNullOrWhiteSpace(request.PictureUrl))
        {
            claims["picture"] = request.PictureUrl;
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(
            _handler.CreateToken(descriptor),
            (int)(expiresAt - issuedAt).TotalSeconds,
            expiresAt);
    }

    /// <summary>
    /// Mints a refresh token: 256 bits from a cryptographic RNG, returned once and stored hashed.
    /// </summary>
    /// <remarks>
    /// Not a JWT. A refresh token needs to be opaque, revocable and long-lived — none of which a
    /// self-contained signed token gives you, since revoking one would mean tracking it in the
    /// database anyway.
    /// </remarks>
    public RefreshTokenPair CreateRefreshToken()
    {
        var value = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        return new RefreshTokenPair(
            value,
            HashRefreshToken(value),
            clock.UtcNow.AddDays(_options.RefreshTokenDays));
    }

    /// <summary>
    /// SHA-256, deliberately unsalted and fast.
    /// </summary>
    /// <remarks>
    /// The opposite of the right answer for passwords, and correct here. The input is 256 bits of
    /// uniform randomness rather than something a human chose, so there is no dictionary to attack
    /// and nothing for a work factor to defend against — while the lookup happens on every refresh
    /// and must be an indexed equality match, which a per-row salt would make impossible.
    /// </remarks>
    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private SymmetricSecurityKey SigningKey => new(Encoding.UTF8.GetBytes(_options.SigningKey));
}
