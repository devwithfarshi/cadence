using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// Boots the real host and exercises the pipeline.
/// </summary>
/// <remarks>
/// No database or container: nothing here touches one. The point is the wiring — DI resolving
/// end to end, middleware in the right order, the error shape, the OpenAPI document. Every defect
/// found while building task 6 was of that kind, and none of them failed a build.
/// </remarks>
public sealed class HostSmokeTests : IClassFixture<CadenceApiFactory>
{
    private readonly CadenceApiFactory _factory;

    public HostSmokeTests(CadenceApiFactory factory) => _factory = factory;

    [Fact]
    public async Task TheHostStarts_AndEveryServiceResolves()
    {
        // The single most valuable assertion here. A missing registration — AddMediator(), a port
        // with no adapter — compiles perfectly and throws only when the container is built.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task LivenessDoesNotConsultDependencies()
    {
        // It must answer even with no database reachable, or a brief outage makes the orchestrator
        // restart every healthy replica and turn a degradation into an outage.
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AnUnknownRoute_ReturnsProblemJsonWithACorrelationId()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/v1/does-not-exist", UriKind.Relative));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("correlationId").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("trace_abc-123", true)]
    [InlineData("bad value here", false)]
    public async Task AClientCorrelationId_IsEchoedOnlyWhenItIsSafe(string supplied, bool echoed)
    {
        // The value reaches logs and a response header, so an unbounded client-controlled string
        // would be a log-injection vector.
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("/health/live", UriKind.Relative));
        request.Headers.TryAddWithoutValidation(CorrelationHeader, supplied);

        var response = await client.SendAsync(request);

        var returned = response.Headers.GetValues(CorrelationHeader).Single();

        if (echoed)
        {
            returned.ShouldBe(supplied);
        }
        else
        {
            returned.ShouldNotBe(supplied);
            returned.ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task TheOpenApiDocumentIsServed_WithTheBearerScheme()
    {
        // Served in every environment — the client renders it as an API reference (§17.1).
        using var client = _factory.CreateClient();

        var document = await client.GetFromJsonAsync<JsonElement>(
            new Uri("/swagger/v1/swagger.json", UriKind.Relative));

        document.GetProperty("info").GetProperty("title").GetString().ShouldBe("Cadence API");
        document.GetProperty("components")
            .GetProperty("securitySchemes")
            .GetProperty("Bearer")
            .GetProperty("scheme")
            .GetString()
            .ShouldBe("bearer");
    }

    [Fact]
    public async Task SecurityHeadersAreApplied()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/live", UriKind.Relative));

        response.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        response.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");
    }

    private const string CorrelationHeader = "X-Correlation-Id";
}

/// <summary>
/// Hosts the API in-process with configuration that needs no external services.
/// </summary>
public sealed class CadenceApiFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // A well-formed string EF can parse. Nothing connects: AddDbContext is lazy, and
                // liveness deliberately consults no dependency.
                ["ConnectionStrings:Postgres"] =
                    "Host=localhost;Port=5432;Database=cadence-smoke;Username=cadence;Password=cadence",

                // Off, or the host would try to migrate a database that is not there.
                ["Database:MigrateOnStartup"] = "false",

                // CorsOptions requires at least one origin and validates on start, so the host
                // would refuse to boot without this — which is the intended behaviour.
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",

                // Likewise for auth: JwtOptions rejects a key under 32 characters and
                // GoogleAuthOptions requires a client id, both at startup rather than at first use.
                ["Jwt:SigningKey"] = "smoke-test-signing-key-that-is-long-enough-to-pass",
                ["Jwt:Issuer"] = "cadence-test",
                ["Jwt:Audience"] = "cadence-test-client",
                ["Google:ClientId"] = "smoke-test.apps.googleusercontent.com",
            }));

        return base.CreateHost(builder);
    }
}
