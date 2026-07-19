using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Messaging;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Common.Behaviors;

/// <summary>
/// Read-through cache for queries that declare themselves cacheable.
/// </summary>
/// <remarks>
/// <para>
/// The generic constraint is what scopes this: the DI container only closes an open generic whose
/// constraints are satisfied, so this behavior is never even constructed for a request that is not
/// an <see cref="ICacheableQuery"/>. That is a compile-time guarantee, not a runtime <c>if</c>.
/// </para>
/// <para>
/// The key is namespaced by organization and user. Two members of the same workspace can legitimately
/// see different results — row visibility depends on the caller — so a key that omitted the user
/// would serve one person's rows to another (§13.3).
/// </para>
/// </remarks>
public sealed class CachingBehavior<TMessage, TResponse>(
    ICacheService cache,
    ICurrentUser currentUser,
    ILogger<CachingBehavior<TMessage, TResponse>> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage, ICacheableQuery
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var key = $"org:{currentUser.OrganizationId}:user:{currentUser.Id}:{message.CacheKey}";

        var cached = await cache.GetAsync<TResponse>(key, cancellationToken);
        if (cached is not null)
        {
            logger.LogDebug("Cache hit for {CacheKey}", key);
            return cached;
        }

        var response = await next(message, cancellationToken);

        // Nulls are not cached: a miss and a cached null are indistinguishable on read, so caching
        // one would pin the miss for the whole TTL.
        if (response is not null)
        {
            await cache.SetAsync(key, response, message.CacheTtl, cancellationToken);
        }

        return response;
    }
}
