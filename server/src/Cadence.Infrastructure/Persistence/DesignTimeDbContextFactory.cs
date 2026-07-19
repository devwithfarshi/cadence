using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> construct the context without booting the application.
/// </summary>
/// <remarks>
/// <para>
/// The context needs an <see cref="ICurrentUser"/>, which at design time does not exist — nobody is
/// signed in when a migration is scaffolded. This supplies an inert one.
/// </para>
/// <para>
/// The connection string is read from <c>CADENCE_MIGRATIONS_CONNECTION</c> when present, falling back
/// to a local default. <b>No connection is opened to scaffold a migration</b> — EF only needs the
/// provider to know how to translate the model — so the fallback being wrong is harmless. It matters
/// only for <c>dotnet ef database update</c>, which is a local convenience; production migrations run
/// as a gated job with a real connection string (§8.4).
/// </para>
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CadenceDbContext>
{
    private const string DefaultConnection =
        "Host=localhost;Port=5432;Database=cadence;Username=cadence;Password=cadence";

    public CadenceDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("CADENCE_MIGRATIONS_CONNECTION") ?? DefaultConnection;

        var options = new DbContextOptionsBuilder<CadenceDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CadenceDbContext(options, new DesignTimeUser());
    }

    /// <summary>
    /// An unauthenticated principal. Query filters resolve to <see cref="Guid.Empty"/>, which is
    /// irrelevant for scaffolding — filters shape queries, not schema.
    /// </summary>
    private sealed class DesignTimeUser : ICurrentUser
    {
        public Guid? Id => null;

        public Guid? OrganizationId => null;

        public string? Email => null;

        public UserRole? Role => null;

        public bool IsAuthenticated => false;

        public Guid RequireId() =>
            throw new InvalidOperationException("There is no current user at design time.");

        public Guid RequireOrganizationId() =>
            throw new InvalidOperationException("There is no current organization at design time.");

        public bool IsAtLeast(UserRole role) => false;
    }
}
