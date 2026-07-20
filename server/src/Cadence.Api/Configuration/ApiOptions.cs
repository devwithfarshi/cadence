using System.ComponentModel.DataAnnotations;

namespace Cadence.Api.Configuration;

/// <summary>
/// Cross-origin settings for the Next.js client.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public const string PolicyName = "CadenceClient";

    /// <summary>
    /// Exact origins, never a wildcard.
    /// </summary>
    /// <remarks>
    /// The refresh token travels in a cookie, so credentialed requests are required — and the
    /// browser refuses <c>Access-Control-Allow-Origin: *</c> together with credentials. An explicit
    /// list is the only correct answer here, not a stricter-than-necessary one.
    /// </remarks>
    [MinLength(1)]
    public string[] AllowedOrigins { get; init; } = [];
}

/// <summary>
/// Request rate limits.
/// </summary>
/// <remarks>
/// Partitioned per user when authenticated and per IP otherwise, so one noisy tenant cannot exhaust
/// the budget for everyone behind the same proxy (§15.3).
/// </remarks>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public const string GlobalPolicy = "global";

    /// <summary>Requests allowed per window.</summary>
    [Range(1, 100_000)]
    public int PermitLimit { get; init; } = 300;

    [Range(1, 3600)]
    public int WindowSeconds { get; init; } = 60;

    /// <summary>
    /// How many requests wait rather than being rejected once the limit is hit.
    /// </summary>
    /// <remarks>
    /// Small on purpose. A deep queue converts a burst into latency for everyone instead of a fast
    /// 429 the client can back off from.
    /// </remarks>
    [Range(0, 1000)]
    public int QueueLimit { get; init; } = 0;
}
