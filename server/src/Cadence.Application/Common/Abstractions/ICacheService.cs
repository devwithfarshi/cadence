namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Distributed cache, backed by Redis.
/// </summary>
/// <remarks>
/// Reads return <c>null</c> on a miss <b>and on any cache failure</b>. A cache is an optimisation:
/// if Redis is down the request should be slow, not broken. Writes are likewise best-effort
/// (§13.2).
/// </remarks>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Drops every key under a prefix — used when a write invalidates a family of cached reads
    /// (e.g. all dashboard aggregates for one workspace).
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

    /// <summary>Read-through helper for call sites that are not queries in the pipeline.</summary>
    Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);
}
