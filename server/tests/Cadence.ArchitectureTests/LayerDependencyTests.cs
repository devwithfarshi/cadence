using System.Reflection;
using NetArchTest.Rules;
using Shouldly;

namespace Cadence.ArchitectureTests;

/// <summary>
/// Executable architecture boundaries (blueprint §1.3, §18).
/// </summary>
/// <remarks>
/// Clean Architecture's dependency rule is only real if something enforces it. A code review
/// catches the first violation; it does not catch the twentieth. These tests fail the build
/// instead, which is what keeps the layering from quietly eroding.
/// </remarks>
public class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(Cadence.Domain.Common.Entity).Assembly;

    private const string DomainNamespace = "Cadence.Domain";
    private const string ApplicationNamespace = "Cadence.Application";
    private const string InfrastructureNamespace = "Cadence.Infrastructure";
    private const string ApiNamespace = "Cadence.Api";

    [Fact]
    public void Domain_DependsOnNothing()
    {
        // The domain is the one layer with zero outward dependencies — that is what makes business
        // rules testable without a database, a web host, or any NuGet package at all.
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(ApplicationNamespace, InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Domain_DoesNotDependOnEntityFrameworkOrAspNet()
    {
        // Persistence and transport concerns leaking into the domain is the single most common way
        // Clean Architecture degrades into an anaemic N-tier app.
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Entities_AreNotPubliclyMutable()
    {
        // Public setters let any caller put an aggregate into an invalid state, bypassing the
        // invariants the aggregate exists to enforce. State changes go through methods.
        var offenders = Types.InAssembly(Domain)
            .That()
            .Inherit(typeof(Cadence.Domain.Common.Entity))
            .GetTypes()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => $"{property.DeclaringType?.Name}.{property.Name}")
            .ToList();

        offenders.ShouldBeEmpty();
    }
}
