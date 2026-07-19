using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Common;
using Cadence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using NSubstitute;
using Shouldly;

namespace Cadence.UnitTests.Infrastructure;

/// <summary>
/// Builds the EF model offline — no database, no container.
/// </summary>
/// <remarks>
/// A model that cannot be built fails at the first request in production and at the first
/// <c>dotnet ef migrations add</c> in development. Catching it here means the failure arrives as a
/// build-time test failure with a usable message instead.
/// </remarks>
public class ModelBuildTests
{
    private static IModel Model => Build().Model;

    [Fact]
    public void TheModelBuilds()
    {
        Should.NotThrow(() => Model.GetEntityTypes().ToArray());
    }

    [Fact]
    public void EveryTenantScopedEntity_HasAQueryFilter()
    {
        // This is the cross-tenant leak guard. An entity that gains ITenantScoped but somehow ends
        // up without a filter is the one bug this whole mechanism exists to prevent.
        var unfiltered = Model.GetEntityTypes()
            .Where(entity => typeof(ITenantScoped).IsAssignableFrom(entity.ClrType))
            .Where(entity => entity.GetDeclaredQueryFilters().Count == 0)
            .Select(entity => entity.ClrType.Name)
            .ToList();

        unfiltered.ShouldBeEmpty();
    }

    [Fact]
    public void EverySoftDeletableEntity_HasAQueryFilter()
    {
        var unfiltered = Model.GetEntityTypes()
            .Where(entity => typeof(ISoftDeletable).IsAssignableFrom(entity.ClrType))
            .Where(entity => entity.GetDeclaredQueryFilters().Count == 0)
            .Select(entity => entity.ClrType.Name)
            .ToList();

        unfiltered.ShouldBeEmpty();
    }

    [Fact]
    public void IdentityIsGlobal_NotTenantScoped()
    {
        // One person may belong to several organizations, so filtering users by organization would
        // make sign-in fail for everyone but their first workspace (§3.3).
        typeof(ITenantScoped).IsAssignableFrom(typeof(Cadence.Domain.Identity.User)).ShouldBeFalse();
        typeof(ITenantScoped).IsAssignableFrom(typeof(Cadence.Domain.Identity.Organization)).ShouldBeFalse();
    }

    [Fact]
    public void TablesAndColumnsAreSnakeCase()
    {
        var meeting = Model.FindEntityType(typeof(Cadence.Domain.Meetings.Meeting))!;

        meeting.GetTableName().ShouldBe("meeting");
        meeting.GetProperty(nameof(Cadence.Domain.Meetings.Meeting.OrganizationId))
            .GetColumnName()
            .ShouldBe("organization_id");
    }

    [Fact]
    public void NoDuplicateShadowForeignKeys()
    {
        // Declaring a relationship from the dependent with `HasOne<T>().WithMany()` when the
        // principal already exposes a navigation makes EF treat it as a second relationship and add
        // a shadow `user_id1` column beside the real FK. It builds, migrates and only shows up as a
        // stray nullable column and a duplicate index — so assert on it rather than eyeballing DDL.
        var offenders = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties()
                .Where(property => property.IsShadowProperty())
                .Where(property => char.IsDigit(property.Name[^1]))
                .Select(property => $"{entity.ClrType.Name}.{property.Name}"))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void EveryNavigationCollection_ReadsItsBackingField()
    {
        // The collections are exposed as IReadOnlyCollection over a private list. If EF binds to the
        // property instead of the field, materialisation fails at runtime on the first query that
        // includes the navigation.
        var offenders = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetNavigations())
            .Where(navigation => navigation.IsCollection)
            .Where(navigation => navigation.GetPropertyAccessMode() != PropertyAccessMode.Field)
            .Select(navigation => $"{navigation.DeclaringEntityType.ClrType.Name}.{navigation.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void EveryConstraintAndIndexIsLowerSnakeCase()
    {
        // Unquoted identifiers fold to lowercase in Postgres while EF quotes them, so a mixed-case
        // constraint name is one a DBA cannot reference without remembering the quotes.
        var names = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetDeclaredForeignKeys()
                .Select(foreignKey => foreignKey.GetConstraintName())
                .Concat(entity.GetDeclaredIndexes().Select(index => index.GetDatabaseName()))
                .Concat(entity.GetDeclaredKeys().Select(key => key.GetName())))
            .Where(name => name is not null)
            .ToList();

        names.ShouldNotBeEmpty();
        names.ShouldAllBe(name => name == name!.ToLowerInvariant());
    }

    [Fact]
    public void ForeignKeyNamesUseTheSingularTableName()
    {
        // Guards the load-order trap: a relationship built before its principal's table was renamed
        // keeps the pluralised name, so the same rule ends up spelled two ways.
        var offenders = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetDeclaredForeignKeys())
            .Select(foreignKey => foreignKey.GetConstraintName())
            .Where(name => name is not null && (name.Contains("_meetings_", StringComparison.Ordinal)
                || name.Contains("_conversations_", StringComparison.Ordinal)
                || name.Contains("_users_", StringComparison.Ordinal)))
            .ToList();

        offenders.ShouldBeEmpty();
    }

    [Fact]
    public void TimestampsAreTimestamptz_NeverNaiveTimestamp()
    {
        // A naive `timestamp` silently drops the offset, and the API is UTC end to end (§3.1).
        var offenders = Model.GetEntityTypes()
            .SelectMany(entity => entity.GetProperties())
            .Where(property => property.ClrType == typeof(DateTimeOffset)
                || property.ClrType == typeof(DateTimeOffset?))
            .Where(property => property.GetColumnType() is { } type
                && !type.StartsWith("timestamp with time zone", StringComparison.Ordinal))
            .Select(property => $"{property.DeclaringType.ClrType.Name}.{property.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }

    private static CadenceDbContext Build()
    {
        var currentUser = Substitute.For<ICurrentUser>();
        currentUser.OrganizationId.Returns(Guid.CreateVersion7());

        var options = new DbContextOptionsBuilder<CadenceDbContext>()
            .UseNpgsql("Host=localhost;Database=cadence-model-only")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new CadenceDbContext(options, currentUser);
    }
}
