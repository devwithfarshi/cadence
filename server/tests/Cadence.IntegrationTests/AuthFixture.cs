using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;

namespace Cadence.IntegrationTests;

/// <summary>
/// A throwaway Postgres plus the real API, wired together.
/// </summary>
/// <remarks>
/// A real database rather than an in-memory provider. The in-memory provider honours neither
/// constraints, transactions nor query filters, so tests pass against it while production breaks —
/// which is exactly the class of defect these tests exist to catch (§18).
/// </remarks>
public sealed class AuthFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    // The image goes to the constructor; the parameterless PostgreSqlBuilder() is obsolete.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("cadence")
        .WithUsername("cadence")
        .WithPassword("cadence")
        .Build();

    /// <summary>
    /// Substituted at the port boundary, so a test can sign in without a real Google token.
    /// </summary>
    /// <remarks>
    /// The only thing faked. Everything below it — provisioning, token issuance, rotation, the
    /// database — is the production path.
    /// </remarks>
    public FakeGoogleIdTokenValidator Google { get; } = new();

    /// <summary>
    /// The AI model, substituted at the port.
    /// </summary>
    /// <remarks>
    /// The second and last thing faked in this suite, and for the same reason as the Google
    /// validator: it is a port to a paid third party, and calling it from tests would be
    /// non-deterministic, slow and billed. Everything below it — the job, the handler, the
    /// persistence, the status transitions — is the production path.
    /// </remarks>
    public FakeLlmProvider Llm { get; } = new();

    /// <summary>
    /// The file store, substituted at the port.
    /// </summary>
    /// <remarks>
    /// The third and last fake, for the same reason as the other two: a port to a paid third party.
    /// Everything below it — the signature handshake, the verification at registration, the indexing
    /// job and the destroy-on-delete — is the production path.
    /// </remarks>
    public FakeFileStorage Files { get; } = new();

    // Implemented explicitly: xunit's IAsyncLifetime returns Task, while WebApplicationFactory
    // already defines a ValueTask-returning DisposeAsync. Without this the two collide.
    async Task IAsyncLifetime.InitializeAsync()
    {
        await _postgres.StartAsync();

        // The schema is created by running the real migration, so a migration that does not apply
        // fails these tests rather than being discovered at deploy time.
        await using var scope = Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();
        await context.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    /// <summary>Opens a scope on the real context, for arranging and asserting on stored state.</summary>
    public AsyncServiceScope CreateDbScope() => Services.CreateAsyncScope();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(configuration => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = _postgres.GetConnectionString(),
                ["Database:MigrateOnStartup"] = "false",
                ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                ["Jwt:SigningKey"] = "integration-test-signing-key-long-enough",
                ["Jwt:Issuer"] = "cadence-test",
                ["Jwt:Audience"] = "cadence-test-client",
                ["Google:ClientId"] = "integration-test.apps.googleusercontent.com",

                // No worker: jobs are enqueued but never executed by a background thread, so a
                // test drives the job itself and asserts without racing it.
                ["Jobs:RunWorker"] = "false",

                // Deliberately empty. The real provider is replaced below; leaving a key here would
                // mean a mis-registration silently talked to a paid API from the test suite.
                ["Ai:ApiKey"] = string.Empty,
            }));

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IGoogleIdTokenValidator>();
            services.AddSingleton<IGoogleIdTokenValidator>(Google);

            services.RemoveAll<ILlmProvider>();
            services.AddSingleton<ILlmProvider>(Llm);

            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(Files);
        });

        return base.CreateHost(builder);
    }
}

/// <summary>
/// Returns whatever identity a test stages, for whatever token string it is given.
/// </summary>
public sealed class FakeGoogleIdTokenValidator : IGoogleIdTokenValidator
{
    private readonly Dictionary<string, GoogleIdentity> _identities = new(StringComparer.Ordinal);

    /// <summary>Makes <paramref name="idToken"/> resolve to a verified identity.</summary>
    public GoogleIdentity Stage(
        string idToken,
        string email = "alex@northwind.io",
        string name = "Alex Rivera",
        bool emailVerified = true,
        string? subject = null)
    {
        var identity = new GoogleIdentity(
            subject ?? $"google-sub-{email}",
            email,
            emailVerified,
            name,
            PictureUrl: null,
            HostedDomain: null);

        _identities[idToken] = identity;
        return identity;
    }

    public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(_identities.GetValueOrDefault(idToken));
}

/// <summary>
/// Shares one fixture — and therefore one container and one host — across every test class that
/// needs a database.
/// </summary>
/// <remarks>
/// <c>IClassFixture</c> would build a fixture per class, and those classes run in parallel, so each
/// suite would start its own Postgres at the same moment. That is slower and, on a loaded machine,
/// flaky: a container that loses the race simply exits. A collection fixture also serialises the
/// classes, so they cannot interfere through shared rows.
/// </remarks>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<AuthFixture>
{
    public const string Name = "database";
}
