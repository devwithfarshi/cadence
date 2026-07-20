using System.Security.Claims;
using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Cadence.Domain.Meetings;
using Cadence.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// The cross-tenant isolation suite required by §3.3.
/// </summary>
/// <remarks>
/// Two organizations are seeded with rows of every tenant-scoped kind, and every read is asserted to
/// see exactly its own. A failure here is a data leak between customers, not a bug — which is why
/// the filter is applied by walking the model in <c>CadenceDbContext</c> rather than remembered per
/// query, and why this suite tests the mechanism as well as the behaviour.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class TenantIsolationTests
{
    private readonly AuthFixture _fixture;

    public TenantIsolationTests(AuthFixture fixture) => _fixture = fixture;

    /// <summary>
    /// The mechanism: no tenant-scoped entity may exist without a query filter.
    /// </summary>
    /// <remarks>
    /// This is the test that keeps working as the schema grows. The behavioural tests below cover
    /// the entities that exist today; this one fails the build the day somebody adds a new
    /// <c>ITenantScoped</c> entity and the filter does not follow — which is exactly the moment the
    /// leak would otherwise be introduced, silently, by code that looks correct.
    /// </remarks>
    [Fact]
    public void EveryTenantScopedEntity_HasAQueryFilter()
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var tenantScoped = context.Model.GetEntityTypes()
            .Where(entity => typeof(ITenantScoped).IsAssignableFrom(entity.ClrType))
            .ToList();

        // Guards the guard: if the interface were renamed and this found nothing, every assertion
        // below would vacuously pass.
        tenantScoped.ShouldNotBeEmpty();

        var unfiltered = tenantScoped
            .Where(entity => entity.GetDeclaredQueryFilters().Count == 0)
            .Select(entity => entity.ClrType.Name)
            .ToList();

        unfiltered.ShouldBeEmpty(
            $"These tenant-scoped entities have no query filter and would leak across "
            + $"organizations: {string.Join(", ", unfiltered)}");
    }

    [Fact]
    public async Task AWorkspacesRows_AreInvisibleToAnotherWorkspace()
    {
        var left = await SeedWorkspaceAsync("left");
        var right = await SeedWorkspaceAsync("right");

        // Read as the left tenant. Every count is of that tenant's rows alone, even though the
        // right tenant's rows sit in the same tables.
        var visible = await ReadAsAsync(left, async context => new
        {
            Meetings = await context.Meetings.CountAsync(),
            Invitations = await context.Invitations.CountAsync(),
            Members = await context.OrganizationMembers.CountAsync(),
        });

        visible.Meetings.ShouldBe(1);
        visible.Invitations.ShouldBe(1);
        visible.Members.ShouldBe(1);

        // And specifically not the other tenant's, addressed by primary key — the shape a leak takes
        // when an id turns up in a URL.
        var leaked = await ReadAsAsync(left, async context => new
        {
            Meeting = await context.Meetings.FirstOrDefaultAsync(row => row.Id == right.MeetingId),
            Invitation = await context.Invitations.FirstOrDefaultAsync(row => row.Id == right.InvitationId),
        });

        leaked.Meeting.ShouldBeNull();
        leaked.Invitation.ShouldBeNull();
    }

    [Fact]
    public async Task AnUnauthenticatedContext_SeesNothingRatherThanEverything()
    {
        await SeedWorkspaceAsync("anonymous-probe");

        // CurrentOrganizationId falls back to Guid.Empty with no principal. The filter then matches
        // nothing — the safe direction. A filter that skipped itself when the tenant was unknown
        // would turn every unauthenticated code path into a full table read.
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        (await context.Meetings.CountAsync()).ShouldBe(0);
        (await context.Invitations.CountAsync()).ShouldBe(0);
        (await context.OrganizationMembers.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task IgnoringTheFilter_IsWhatItTakesToSeeAcrossTenants()
    {
        // The inverse assertion, and the reason it is worth writing: it proves the counts above are
        // the filter doing its job rather than the rows simply not being there.
        var left = await SeedWorkspaceAsync("proof-left");
        await SeedWorkspaceAsync("proof-right");

        var everything = await ReadAsAsync(
            left,
            async context => await context.Meetings.IgnoreQueryFilters().CountAsync());

        everything.ShouldBeGreaterThan(1);
    }

    /// <summary>
    /// Creates a workspace with one row of each tenant-scoped kind that has a factory today.
    /// </summary>
    private async Task<SeededWorkspace> SeedWorkspaceAsync(string label)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var user = User.Create($"{label}-{Guid.CreateVersion7():n}@northwind.io", $"{label} owner", null);
        var organization = Organization.CreatePersonal($"{label} workspace", user.Id);

        var meeting = Meeting.Schedule(
            organization.Id,
            user.Id,
            $"{label} planning",
            "Seeded by the isolation suite.",
            DateTimeOffset.UtcNow.AddHours(1),
            DateTimeOffset.UtcNow.AddHours(2),
            MeetingPlatform.Zoom);

        var invitation = Invitation.Create(
            organization.Id,
            $"invitee-{Guid.CreateVersion7():n}@northwind.io",
            UserRole.Member,
            user.Id,
            $"hash-{Guid.CreateVersion7():n}",
            DateTimeOffset.UtcNow.AddDays(14),
            DateTimeOffset.UtcNow);

        context.Users.Add(user);
        context.Organizations.Add(organization);
        context.Meetings.Add(meeting);
        context.Invitations.Add(invitation);

        await context.SaveChangesAsync();

        return new SeededWorkspace(organization.Id, user.Id, meeting.Id, invitation.Id);
    }

    /// <summary>
    /// Runs a read against a context that believes it is serving <paramref name="workspace"/>.
    /// </summary>
    /// <remarks>
    /// The tenant is installed the only way production installs it — as claims on the principal —
    /// so this exercises the real <c>CurrentUser</c> and the real filter rather than a test-only
    /// substitute that could diverge from either.
    /// </remarks>
    private async Task<T> ReadAsAsync<T>(
        SeededWorkspace workspace,
        Func<CadenceDbContext, Task<T>> read)
    {
        using var scope = _fixture.CreateDbScope();

        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        accessor.HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, workspace.UserId.ToString()),
                    new Claim(CadenceClaims.OrganizationId, workspace.OrganizationId.ToString()),
                    new Claim(CadenceClaims.Role, "owner"),
                ],
                authenticationType: "Test")),
        };

        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        return await read(context);
    }

    private sealed record SeededWorkspace(
        Guid OrganizationId,
        Guid UserId,
        Guid MeetingId,
        Guid InvitationId);
}
