using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Configuration;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Infrastructure.Authentication;

/// <summary>
/// Validates a Google ID token against Google's published signing keys.
/// </summary>
/// <remarks>
/// <para>
/// <c>GoogleJsonWebSignature</c> fetches and caches Google's JWKS itself, so validation is offline
/// after the first call — there is no network round trip per sign-in, and a brief Google outage does
/// not stop existing users signing in.
/// </para>
/// <para>
/// It checks the signature, issuer and expiry. The audience is checked because we pass
/// <c>Audience</c>; without it any validly-signed Google token — including one minted for a
/// completely different application — would be accepted here.
/// </para>
/// </remarks>
public sealed class GoogleIdTokenValidator(
    IOptions<GoogleAuthOptions> options,
    ILogger<GoogleIdTokenValidator> logger)
    : IGoogleIdTokenValidator
{
    private readonly GoogleAuthOptions _options = options.Value;

    public async Task<GoogleIdentity?> ValidateAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        GoogleJsonWebSignature.Payload payload;

        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(
                idToken,
                new GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [_options.ClientId],
                });
        }
        catch (InvalidJwtException exception)
        {
            // Expected on a public endpoint: expired, malformed or wrong-audience tokens arrive
            // routinely. Debug, not Error — logging these as errors makes the log useless and hides
            // the failures that are defects.
            logger.LogDebug(exception, "Rejected a Google ID token");
            return null;
        }

        if (!IsAllowedDomain(payload.HostedDomain))
        {
            logger.LogInformation(
                "Rejected sign-in from hosted domain {HostedDomain}, which is not allow-listed",
                payload.HostedDomain ?? "(none)");

            return null;
        }

        return new GoogleIdentity(
            payload.Subject,
            payload.Email.Trim().ToLowerInvariant(),
            // The library exposes this as bool?; anything but an explicit true is treated as
            // unverified, since this claim gates linking to an existing account by address.
            payload.EmailVerified == true,
            string.IsNullOrWhiteSpace(payload.Name) ? payload.Email : payload.Name,
            payload.Picture,
            payload.HostedDomain);
    }

    private bool IsAllowedDomain(string? hostedDomain) =>
        _options.AllowedHostedDomains.Length == 0
        || (hostedDomain is not null
            && _options.AllowedHostedDomains.Contains(hostedDomain, StringComparer.OrdinalIgnoreCase));
}
