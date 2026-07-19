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
    private static readonly Assembly Application = typeof(Cadence.Application.DependencyInjection).Assembly;

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
    public void Application_DoesNotDependOnInfrastructureOrApi()
    {
        // Application declares ports; Infrastructure supplies adapters. The moment Application can
        // see Infrastructure, someone will reach for a concrete adapter and the inversion is gone.
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny(InfrastructureNamespace, ApiNamespace)
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void Application_DoesNotDependOnAspNetOrEntityFrameworkProviders()
    {
        // Handlers take ICurrentUser, not HttpContext, which is what lets a background job run the
        // same handler with a system principal. Npgsql in Application would likewise pin the use
        // cases to one database.
        var result = Types.InAssembly(Application)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore", "Npgsql", "StackExchange.Redis", "Hangfire")
            .GetResult();

        result.FailingTypeNames.ShouldBeNull();
    }

    [Fact]
    public void PortsAreInterfaces_NotConcreteClasses()
    {
        // A port that is a class cannot be substituted, which defeats the point of declaring it.
        var offenders = Types.InAssembly(Application)
            .That()
            .ResideInNamespace($"{ApplicationNamespace}.Common.Abstractions")
            .And()
            .HaveNameStartingWith("I")
            .GetTypes()
            .Where(type => !type.IsInterface)
            .Select(type => type.Name)
            .ToList();

        offenders.ShouldBeEmpty();
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
