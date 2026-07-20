using System.Globalization;
using System.Threading.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cadence.Api.Common;
using Cadence.Api.Realtime;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Modules.Transcripts;
using Cadence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Api.Configuration;

/// <summary>
/// Delivery-layer wiring: HTTP concerns only.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApiServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddHttpContextAccessor();
        services.AddScoped<ScopedPrincipal>();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddCadenceJson();
        services.AddCadenceRealtime();

        services.AddCadenceAuthentication(configuration);
        services.AddCadenceProblemDetails();
        services.AddCadenceCors(configuration);
        services.AddCadenceRateLimiting(configuration);
        services.AddCadenceHealthChecks(configuration);
        services.AddCadenceSwagger();

        return services;
    }

    /// <summary>
    /// Serialises enums as the snake_case strings the client's types are written in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Without this, <c>System.Text.Json</c> writes enums as <b>integers</b> — a meeting would come
    /// back as <c>"status": 2</c> where the client's type says <c>"completed"</c>. Round-tripping
    /// between two .NET processes hides it completely, which is why it survived three modules: a
    /// typed test deserialises <c>2</c> back into the right enum and passes.
    /// </para>
    /// <para>
    /// The policy matches the one the database converter uses, so a value reads identically in a
    /// response, in a query string and in a psql session. Reading stays case-insensitive, so a
    /// client sending <c>"Completed"</c> is understood rather than rejected.
    /// </para>
    /// </remarks>
    private static void AddCadenceJson(this IServiceCollection services) =>
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(
                new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower)));

    /// <summary>
    /// SignalR, the live-ingest buffer and its flush loop.
    /// </summary>
    /// <remarks>
    /// The buffer is a singleton because it is shared state across connections, and the flush
    /// service is registered <b>as itself as well as</b> a hosted service — the hub calls it
    /// directly to flush a busy meeting early, and two instances would mean the timer draining a
    /// different buffer than the one the hub filled.
    /// </remarks>
    private static void AddCadenceRealtime(this IServiceCollection services)
    {
        services.AddSingleton<HubPrincipalFilter>();
        services.AddSignalR(options => options.AddFilter<HubPrincipalFilter>());

        services.AddSingleton<TranscriptIngestBuffer>();
        services.AddSingleton<TranscriptFlushService>();
        services.AddHostedService(provider => provider.GetRequiredService<TranscriptFlushService>());

        services.AddScoped<IMeetingBroadcaster, SignalRMeetingBroadcaster>();
    }

    private static void AddCadenceProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                // Applies to the framework's own responses too — a 404 from routing or a 405 from a
                // wrong verb carries the same correlation id as one Cadence produced, so the client
                // never meets an error shape it cannot read.
                context.ProblemDetails.Instance ??= context.HttpContext.Request.Path;
                context.ProblemDetails.Extensions.TryAdd(
                    "correlationId",
                    context.HttpContext.TraceIdentifier);
            });

        services.AddExceptionHandler<GlobalExceptionHandler>();
    }

    private static void AddCadenceCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = configuration
            .GetSection($"{CorsOptions.SectionName}:{nameof(CorsOptions.AllowedOrigins)}")
            .Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(
            CorsOptions.PolicyName,
            policy => policy
                .WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                // Required for the refresh-token cookie. Note this forces explicit origins — the
                // browser rejects credentials alongside a wildcard.
                .AllowCredentials()
                .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));
    }

    private static void AddCadenceRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
            ?? new RateLimitOptions();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = async (context, cancellationToken) =>
            {
                // Tell the client when to come back. Without Retry-After a well-behaved client can
                // only guess, and guessing usually means retrying immediately.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Type = "https://cadence.app/errors/rate-limit",
                        Title = "Too many requests.",
                        Detail = "Slow down and retry shortly.",
                        Instance = context.HttpContext.Request.Path,
                        Extensions = { ["correlationId"] = context.HttpContext.TraceIdentifier },
                    },
                    cancellationToken: cancellationToken);
            };

            options.AddPolicy(RateLimitOptions.GlobalPolicy, context =>
            {
                // Per user when we know who they are, per IP otherwise. Partitioning only by IP
                // would let one office's shared address exhaust the budget for everyone in it.
                var partitionKey =
                    context.User.Identity?.IsAuthenticated == true
                        ? $"user:{context.User.Identity.Name ?? context.User.FindFirst("sub")?.Value}"
                        : $"ip:{context.Connection.RemoteIpAddress}";

                return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ =>
                    new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limits.PermitLimit,
                        Window = TimeSpan.FromSeconds(limits.WindowSeconds),
                        QueueLimit = limits.QueueLimit,
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    });
            });
        });
    }

    private static void AddCadenceHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var builder = services.AddHealthChecks();

        // Readiness only. /health/live stays dependency-free so a brief database blip does not make
        // the orchestrator kill an otherwise healthy process (§19.1).
        var postgres = configuration.GetConnectionString("Postgres");
        if (!string.IsNullOrWhiteSpace(postgres))
        {
            builder.AddNpgSql(postgres, name: "postgres", tags: ["ready"]);
        }

        var redis = configuration["Redis:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(redis))
        {
            builder.AddRedis(redis, name: "redis", tags: ["ready"]);
        }

        builder.AddDbContextCheck<CadenceDbContext>("ef-model", tags: ["ready"]);
    }
}
