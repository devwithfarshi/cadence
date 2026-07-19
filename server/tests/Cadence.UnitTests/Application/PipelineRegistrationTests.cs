using Cadence.Application;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Behaviors;
using Cadence.Application.Common.Messaging;
using Cadence.Application.Common.Models;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;

namespace Cadence.UnitTests.Application;

/// <summary>
/// Pins the assumption <c>AddApplication()</c> is built on: the container closes an open-generic
/// behavior only for requests whose type satisfies its constraints.
/// </summary>
/// <remarks>
/// Without this, <c>CachingBehavior</c> would need a runtime <c>if</c> and <c>TransactionBehavior</c>
/// would open a transaction for every read. If a future .NET changes this filtering, these tests
/// fail here rather than as a mysterious performance regression in production.
/// </remarks>
public class PipelineRegistrationTests
{
    private sealed record PlainQuery : IQuery<Result<string>>;

    private sealed record CachedQuery : IQuery<Result<string>>, ICacheableQuery
    {
        public string CacheKey => "dashboard:v1";

        public TimeSpan CacheTtl => TimeSpan.FromMinutes(5);
    }

    private sealed record SomeCommand : ICommand<Result<Guid>>;

    // The behaviors are compared by open generic definition. Writing `is CachingBehavior<PlainQuery,
    // ...>` is not even expressible — the compiler rejects the type argument, which is itself the
    // proof that the constraint holds. These tests cover the second half: that the *container*
    // agrees and silently skips the registration rather than throwing at resolve time.

    [Fact]
    public void AQueryThatHasNotOptedIn_GetsNoCachingBehavior()
    {
        BehaviorKindsFor<PlainQuery, Result<string>>().ShouldNotContain(typeof(CachingBehavior<,>));
    }

    [Fact]
    public void AQueryThatOptedIn_GetsTheCachingBehavior()
    {
        BehaviorKindsFor<CachedQuery, Result<string>>().ShouldContain(typeof(CachingBehavior<,>));
    }

    [Fact]
    public void AQuery_NeverOpensATransaction()
    {
        BehaviorKindsFor<PlainQuery, Result<string>>().ShouldNotContain(typeof(TransactionBehavior<,>));
    }

    [Fact]
    public void ACommand_IsWrappedInATransaction()
    {
        BehaviorKindsFor<SomeCommand, Result<Guid>>().ShouldContain(typeof(TransactionBehavior<,>));
    }

    [Fact]
    public void ACommand_IsNotCached()
    {
        BehaviorKindsFor<SomeCommand, Result<Guid>>().ShouldNotContain(typeof(CachingBehavior<,>));
    }

    [Fact]
    public void LoggingIsOutermostAndPerformanceIsInnermost()
    {
        // Registration order is pipeline order. Logging first means every inner line carries the
        // request scope; Performance last means it times the handler, not the pipeline.
        var kinds = BehaviorKindsFor<CachedQuery, Result<string>>();

        kinds[0].ShouldBe(typeof(LoggingBehavior<,>));
        kinds[^1].ShouldBe(typeof(PerformanceBehavior<,>));
    }

    [Fact]
    public void EveryRequestIsValidated()
    {
        BehaviorKindsFor<PlainQuery, Result<string>>().ShouldContain(typeof(ValidationBehavior<,>));
        BehaviorKindsFor<SomeCommand, Result<Guid>>().ShouldContain(typeof(ValidationBehavior<,>));
    }

    private static Type[] BehaviorKindsFor<TMessage, TResponse>()
        where TMessage : notnull, IMessage =>
        [.. BehaviorsFor<TMessage, TResponse>().Select(behavior => behavior.GetType().GetGenericTypeDefinition())];

    private static IPipelineBehavior<TMessage, TResponse>[] BehaviorsFor<TMessage, TResponse>()
        where TMessage : notnull, IMessage
    {
        var services = new ServiceCollection();

        // Ports the behaviors depend on. Infrastructure supplies the real ones; substitutes are
        // enough to prove the registrations resolve.
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddSingleton(Substitute.For<ICurrentUser>());
        services.AddSingleton(Substitute.For<ICacheService>());
        services.AddSingleton(Substitute.For<IUnitOfWork>());

        services.AddApplication();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        return [.. scope.ServiceProvider.GetServices<IPipelineBehavior<TMessage, TResponse>>()];
    }
}
