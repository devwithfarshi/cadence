using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Api.Endpoints;
using Cadence.Api.Realtime;
using Cadence.Application;
using Cadence.Infrastructure;
using Cadence.Infrastructure.Configuration;
using Cadence.Infrastructure.Persistence;
using HealthChecks.UI.Client;
using Mediator;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NetEscapades.AspNetCore.SecurityHeaders;
using Serilog;
using Serilog.Events;

// Bootstrap logger: active before configuration is read, so a failure during startup is still
// logged rather than vanishing into a silent non-zero exit.
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Cadence.Api"));

    // Each layer owns its own registration, so this file stays a readable table of contents (§9.3).
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddApiServices(builder.Configuration);

    // Source-generated dispatch — no runtime reflection over handlers. It has to be registered here
    // rather than inside AddApplication(): the generator runs against the composition root, which is
    // the only assembly that can see every handler. Scoped, so a handler shares the request's
    // DbContext rather than resolving a second one.
    builder.Services.AddMediator((MediatorOptions options) =>
    {
        options.ServiceLifetime = ServiceLifetime.Scoped;
        options.Assemblies = [typeof(Cadence.Application.DependencyInjection)];
    });

    var app = builder.Build();

    // Order matters. Correlation first, so every later line — including the exception handler's —
    // carries the id. The exception handler then wraps everything after it.
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseExceptionHandler();

    // Turns a framework-generated bare status code (404 from routing, 405 from a wrong verb) into
    // the same problem+json shape as everything else.
    app.UseStatusCodePages();

    app.UseSerilogRequestLogging(options =>
        options.GetLevel = (httpContext, _, exception) =>
            // Health probes run every few seconds; logging them at Information buries real traffic.
            exception is not null || httpContext.Response.StatusCode >= 500
                ? LogEventLevel.Error
                : httpContext.Request.Path.StartsWithSegments("/health")
                    ? LogEventLevel.Verbose
                    : LogEventLevel.Information);

    // §15: nosniff, DENY framing, referrer policy, strict CSP, permissions policy. The API serves
    // JSON only, so the restrictive defaults cost nothing; Swagger UI gets the library's same-origin
    // relaxations in Development.
    app.UseSecurityHeaders(new HeaderPolicyCollection().AddDefaultApiSecurityHeaders());

    app.UseCors(CorsOptions.PolicyName);
    app.UseRateLimiter();

    // Authentication before authorization, both before endpoints — the order is what makes
    // User populated by the time a policy is evaluated.
    app.UseAuthentication();
    app.UseAuthorization();

    // Served in every environment: the client renders it as an API reference.
    app.UseSwagger();

    if (app.Environment.IsDevelopment())
    {
        // The UI, unlike the document, is Development-only (§17.1).
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint(
                $"/swagger/{SwaggerConfiguration.DocumentName}/swagger.json",
                "Cadence API v1");
            options.DocumentTitle = "Cadence API";
        });
    }

    app.MapAuthEndpoints();
    app.MapUserEndpoints();
    app.MapOrganizationEndpoints();
    app.MapInvitationEndpoints();
    app.MapMeetingEndpoints();
    app.MapActionItemEndpoints();
    app.MapDocumentEndpoints();
    app.MapKnowledgeEndpoints();

    // The live meeting channel. Mapped alongside the endpoints so the same authentication,
    // authorization and CORS pipeline above applies to the negotiate request.
    app.MapHub<MeetingHub>(HubAuthentication.Path);

    MapHealthEndpoints(app);

    await MigrateIfConfiguredAsync(app);

    await app.RunAsync();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "Cadence.Api terminated unexpectedly");
    throw;
}
finally
{
    // Flushes buffered entries; without this the very failure that killed the process is the one
    // most likely to be lost.
    await Log.CloseAndFlushAsync();
}

static void MapHealthEndpoints(WebApplication app)
{
    // Liveness answers "is the process running" and must not consult dependencies — otherwise a
    // brief database outage makes the orchestrator restart every healthy replica, turning a
    // degradation into an outage.
    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false,
    }).WithTags("Health");

    // Readiness answers "can this instance serve traffic", so it does check them.
    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse,
    }).WithTags("Health");
}

static async Task MigrateIfConfiguredAsync(WebApplication app)
{
    var options = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;

    if (!options.MigrateOnStartup)
    {
        return;
    }

    if (!app.Environment.IsDevelopment())
    {
        // Refuse rather than obey. In production every replica would race to migrate, and a failed
        // migration would take down the whole deployment instead of one job (§8.4). Failing at boot
        // with this message is far better than discovering the setting during an incident.
        throw new InvalidOperationException(
            "Database:MigrateOnStartup is enabled outside Development. Migrations run as a gated "
            + "job before the rolling deploy — see blueprint §8.4.");
    }

    await using var scope = app.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

    await context.Database.MigrateAsync();
}

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in tests (§18).</summary>
public partial class Program;
