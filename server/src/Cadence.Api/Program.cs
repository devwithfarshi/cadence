// Minimal host. The full pipeline — Serilog, ProblemDetails, Swagger, health checks,
// CORS, auth and rate limiting — lands in blueprint §22 task 6.
var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapGet("/health/live", () => TypedResults.Ok(new { status = "healthy" }))
    .WithName("Liveness")
    .WithTags("Health");

app.Run();

/// <summary>Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can host the API in tests (§18).</summary>
public partial class Program;
