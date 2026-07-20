using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Authentication;
using Cadence.Infrastructure.Ai;
using Cadence.Infrastructure.Configuration;
using Cadence.Infrastructure.Jobs;
using Hangfire;
using Hangfire.PostgreSql;
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
        services.AddAiServices(configuration);
        services.AddBackgroundJobs(configuration);

        services.AddSingleton<IDateTime, SystemDateTime>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        return services;
    }

    /// <summary>
    /// The AI provider behind <c>ILlmProvider</c> (§23.3).
    /// </summary>
    /// <remarks>
    /// <b>Not</b> <c>ValidateOnStart</c> on the key. A missing key must not stop the app from
    /// booting — a developer looking at meetings should not need a paid API key, and in production
    /// an unset key surfaces as a failed summary with a retry rather than a process that will not
    /// start. The other settings <i>are</i> validated, because a bad model name or token budget is a
    /// misconfiguration worth catching at boot.
    /// </remarks>
    private static void AddAiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton: the SDK client is thread-safe and holds the connection pool, so one per process
        // is both correct and what keeps keep-alive working.
        services.AddSingleton<ILlmProvider, AnthropicLlmProvider>();
    }

    /// <summary>
    /// Hangfire on the Postgres already in the stack (§14.1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Storage is registered unconditionally so <c>IJobScheduler</c> always resolves, but the
    /// <b>server</b> — the thing that actually executes jobs — is only added when enabled. That lets
    /// a test host enqueue and assert without a worker racing it to run the job first.
    /// </para>
    /// <para>
    /// <c>PrepareSchemaIfNecessary</c> is on: Hangfire owns its own tables in its own schema, and
    /// they are not part of the EF migration story. This is the one place the "migrations never run
    /// at startup" rule (§8.4) does not apply, because the schema belongs to the library rather than
    /// to Cadence.
    /// </para>
    /// </remarks>
    private static void AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        services.AddHangfire(options => options
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(postgres => postgres.UseNpgsqlConnection(connectionString)));

        services.AddScoped<IJobScheduler, HangfireJobScheduler>();

        if (configuration.GetValue("Jobs:RunWorker", defaultValue: true))
        {
            services.AddHangfireServer(options => options.WorkerCount = Environment.ProcessorCount);
        }
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
