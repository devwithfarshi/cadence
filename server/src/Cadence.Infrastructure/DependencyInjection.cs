using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Authentication;
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
        services.AddAuthenticationServices(configuration);

        services.AddSingleton<IDateTime, SystemDateTime>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    private static void AddAuthenticationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ValidateOnStart, so a missing signing key or Google client id stops the process at boot
        // rather than failing the first sign-in an hour later (§11.2).
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<GoogleAuthOptions>()
            .Bind(configuration.GetSection(GoogleAuthOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singletons: both are stateless, and the Google validator holds the cached JWKS, which is
        // the whole point of not re-fetching it per request.
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
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

        // Both ports resolve to the *same* context instance, not to separate registrations. Two
        // instances would mean a handler's writes and the pipeline's commit hit different change
        // trackers, so the commit would find nothing to save.
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<CadenceDbContext>());
        services.AddScoped<ICadenceDbContext>(provider => provider.GetRequiredService<CadenceDbContext>());

        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
    }
}
