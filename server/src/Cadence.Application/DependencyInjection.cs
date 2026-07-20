using System.Reflection;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Behaviors;
using FluentValidation;
using Mapster;
using MapsterMapper;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Application;

/// <summary>
/// Everything the Application layer needs, wired in one place.
/// </summary>
/// <remarks>
/// Each layer owns its own registration (<c>AddApplication</c>, <c>AddInfrastructure</c>,
/// <c>AddApiServices</c>) so <c>Program.cs</c> stays a readable table of contents rather than a
/// hundred-line list nobody maintains (§9.3).
/// </remarks>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        // Registration order is pipeline order, outermost first (§2.3):
        //
        //   Logging → Validation → Caching → Transaction → Performance → handler
        //
        // Logging is outermost so every inner line carries the request scope. Validation precedes
        // Caching and Transaction so a bad request touches neither Redis nor the database.
        // Transaction is inside Caching because a cache hit should not open one. Performance is
        // innermost so it measures the handler, not the pipeline.
        //
        // Caching and Transaction carry extra generic constraints (ICacheableQuery, IBaseCommand);
        // the container only closes an open generic whose constraints are satisfied, so those two
        // are simply absent from the pipeline of requests that do not qualify.
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(CachingBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(PerformanceBehavior<,>));

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);
        services.AddDomainEventHandlers(assembly);
        services.AddMapster(assembly);

        return services;
    }

    /// <summary>
    /// Registers every <see cref="IDomainEventHandler{TDomainEvent}"/> in the assembly.
    /// </summary>
    /// <remarks>
    /// By convention, so subscribing to an event is writing the handler and nothing else. A handler
    /// that has to be registered by hand is one that will silently not run when someone forgets —
    /// and a domain event with no subscriber fails no test, it just quietly does nothing.
    /// </remarks>
    private static void AddDomainEventHandlers(this IServiceCollection services, Assembly assembly)
    {
        var handlers = assembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false })
            .SelectMany(
                type => type.GetInterfaces()
                    .Where(@interface => @interface.IsGenericType
                        && @interface.GetGenericTypeDefinition() == typeof(IDomainEventHandler<>)),
                (type, @interface) => (Implementation: type, Service: @interface));

        foreach (var (implementation, service) in handlers)
        {
            services.AddScoped(service, implementation);
        }
    }

    /// <summary>
    /// Applies every Mapster <c>IRegister</c> in the assembly and registers the mapper.
    /// </summary>
    /// <remarks>
    /// <c>ServiceMapper</c> rather than the plain <c>Mapper</c>: it resolves from the DI container,
    /// which is what lets a mapping use a scoped service when one is genuinely needed.
    /// </remarks>
    private static void AddMapster(this IServiceCollection services, Assembly assembly)
    {
        var config = TypeAdapterConfig.GlobalSettings;
        config.Scan(assembly);

        services.AddSingleton(config);
        services.AddScoped<IMapper, ServiceMapper>();
    }
}
