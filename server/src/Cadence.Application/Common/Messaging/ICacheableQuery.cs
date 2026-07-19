namespace Cadence.Application.Common.Messaging;

// Cadence deliberately does NOT define its own ICommand/IQuery aliases. Mediator already ships
// ICommand, ICommand<T>, IQuery<T>, IBaseCommand and IBaseQuery, and shadowing those names in a
// namespace that is always imported alongside `using Mediator;` produces CS0104 at every use site.
//
// Requests are written against Mediator's interfaces directly, wrapping their payload in Result<T>:
//
//     public sealed record CreateMeetingCommand(...) : ICommand<Result<Guid>>;
//     internal sealed class Handler : ICommandHandler<CreateMeetingCommand, Result<Guid>>;
//
//     public sealed record GetMeetingQuery(Guid Id) : IQuery<Result<MeetingDto>>;
//
// That keeps the marker interfaces the pipeline constrains on — IBaseCommand for
// TransactionBehavior — owned by the library rather than duplicated here.

/// <summary>
/// Marks a query whose result may be served from Redis.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in, deliberately. Caching every query by default is how stale reads reach production; a
/// query has to state that it tolerates staleness, and for how long.
/// </para>
/// <para>
/// Implement this alongside <c>IQuery&lt;T&gt;</c>; <c>CachingBehavior</c> constrains on it, so the
/// container never even constructs the behavior for a query that has not opted in.
/// </para>
/// </remarks>
public interface ICacheableQuery
{
    /// <summary>
    /// Must include every parameter that changes the result. The behavior prefixes the caller's
    /// organization and user, so a key never has to encode identity — but it does have to encode
    /// the filters, or two different filters will share one entry.
    /// </summary>
    string CacheKey { get; }

    TimeSpan CacheTtl { get; }
}
