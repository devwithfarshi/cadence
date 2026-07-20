using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Configuration;
using Cadence.Infrastructure.Persistence;
using Cadence.Infrastructure.Persistence.Interceptors;
using Cadence.Infrastructure.Persistence.Repositories;
using Cadence.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cadence.Infrastructure;

/// <summary>
/// Wires the adapters that satisfy the Application layer's ports.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddPersistence(configuration);

        services.AddSingleton<IDateTime, SystemDateTime>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:Postgres is not configured. Copy .env.example to .env and set it.");

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName)
            .Get<DatabaseOptions>() ?? new DatabaseOptions();

        // Interceptors are scoped because they depend on ICurrentUser, which is per-request.
        services.AddScoped<AuditingInterceptor>();
        services.AddScoped<DomainEventDispatchInterceptor>();

        services.AddDbContext<CadenceDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.CommandTimeout(databaseOptions.CommandTimeoutSeconds);
                npgsql.EnableRetryOnFailure(databaseOptions.MaxRetryCount);
            });

            // snake_case identifiers, so hand-written SQL and psql sessions read naturally (§3.1).
            options.UseSnakeCaseNamingConvention();

            options.AddInterceptors(
                provider.GetRequiredService<AuditingInterceptor>(),
                provider.GetRequiredService<DomainEventDispatchInterceptor>());

            if (databaseOptions.EnableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
                options.EnableDetailedErrors();
            }
        });

        // The unit of work is the same instance as the context, not a second registration — two
        // instances would mean a handler's writes and the pipeline's commit hit different change
        // trackers.
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CadenceDbContext>());

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
    }
}
