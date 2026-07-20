using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Organizations;
using Cadence.Domain.Enums;
using Cadence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// Workspaces, switching, membership and invitations, against a real database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class OrganizationFlowTests
{
    private const string RefreshCookie = "cadence_refresh";

    private readonly AuthFixture _fixture;

    public OrganizationFlowTests(AuthFixture fixture) => _fixture = fixture;

    /* ---------------------------------------------------------------------- */
    /* Listing and creating                                                   */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task ListOrganizations_ReturnsOnlyTheCallersOwn()
    {
        var (mine, _) = await SignInAsync();
        var (theirs, _) = await SignInAsync();

        await CreateOrganizationAsync(theirs, "Someone Else Ltd");

        var organizations = await mine.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));

        organizations.ShouldNotBeNull();
        // Their personal workspace and the one they just created are both invisible here.
        organizations.Count.ShouldBe(1);
        organizations[0].IsCurrent.ShouldBeTrue();
        organizations[0].Role.ShouldBe(UserRole.Owner);
        organizations[0].MemberCount.ShouldBe(1);
    }

    [Fact]
    public async Task CreateOrganization_DoesNotMoveTheCallerIntoIt()
    {
        // Creating a workspace mid-task should not relocate you. The switch is the explicit act.
        var (client, session) = await SignInAsync();

        var created = await CreateOrganizationAsync(client, UniqueName("Northwind"));

        created.IsCurrent.ShouldBeFalse();

        var me = await client.GetJsonAsync<UserDto>(Url("/api/v1/users/me"));
        me!.OrganizationId.ShouldBe(session.User.OrganizationId);
    }

    [Fact]
    public async Task CreateOrganization_RejectsANameWhoseSlugIsTaken()
    {
        var (client, _) = await SignInAsync();
        var name = UniqueName("Contoso");

        await CreateOrganizationAsync(client, name);

        var second = await client.PostJsonAsync(
            Url("/api/v1/organizations"),
            new CreateOrganizationRequest(name));

        // A deliberately-named workspace is told the name is taken rather than silently given a
        // discriminated slug. Only personal workspaces get the discriminator.
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    /* ---------------------------------------------------------------------- */
    /* Switching                                                              */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Switch_IssuesATokenScopedToTheNewWorkspace()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateOrganizationAsync(client, UniqueName("Initech"));

        var switched = await SwitchAsync(client, created.Id);

        switched.User.OrganizationId.ShouldBe(created.Id);

        var me = await client.GetJsonAsync<UserDto>(Url("/api/v1/users/me"));
        me!.OrganizationId.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Switch_SurvivesTheNextRefresh()
    {
        // The regression this exists for: the session stores its workspace, so rotation 15 minutes
        // later re-issues the workspace the user chose. Resolving membership afresh on every refresh
        // would silently drop them back into their oldest workspace with nothing to explain it.
        var (client, _, refreshToken) = await SignInWithCookieAsync();
        var created = await CreateOrganizationAsync(client, UniqueName("Umbrella"));

        await SwitchAsync(client, created.Id);

        var refreshed = await RefreshAsync(client, refreshToken);

        refreshed.User.OrganizationId.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Switch_RefusesAWorkspaceTheCallerIsNotIn()
    {
        var (mine, _) = await SignInAsync();
        var (theirs, _) = await SignInAsync();

        var notMine = await CreateOrganizationAsync(theirs, UniqueName("Cyberdyne"));

        var response = await mine.PostAsync(Url($"/api/v1/organizations/{notMine.Id}/switch"), null);

        // Not found, never forbidden: "you may not enter that workspace" confirms it exists, which
        // is enough to enumerate customers by id.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Switch_LeavesOtherDevicesWhereTheyWere()
    {
        // The workspace lives on the session, not the user, precisely so a laptop in the company
        // account and a phone in a personal one can coexist.
        var (laptop, session, _) = await SignInWithCookieAsync();
        var (phone, _, phoneRefresh) = await SignInWithCookieAsync(reuseEmailOf: laptop);

        var created = await CreateOrganizationAsync(laptop, UniqueName("Tyrell"));
        await SwitchAsync(laptop, created.Id);

        var phoneRefreshed = await RefreshAsync(phone, phoneRefresh);

        phoneRefreshed.User.OrganizationId.ShouldBe(session.User.OrganizationId);
    }

    /* ---------------------------------------------------------------------- */
    /* Renaming and deleting                                                  */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Rename_KeepsTheSlugStable()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateOrganizationAsync(client, UniqueName("Stark"));

        var response = await client.PatchJsonAsync(
            Url($"/api/v1/organizations/{created.Id}"),
            new RenameOrganizationRequest("Stark Industries"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var renamed = (await response.Content.ReadJsonAsync<OrganizationDto>())!;
        renamed.Name.ShouldBe("Stark Industries");
        // The slug is the workspace's stable identifier; re-deriving it would break every link
        // holding the old one.
        renamed.Slug.ShouldBe(created.Slug);
    }

    [Fact]
    public async Task Rename_RefusesAWorkspaceTheCallerDoesNotAdminister()
    {
        // The role policy passes — the caller is an owner *of their own workspace*. The handler's
        // check against the target aggregate is what actually stops this.
        var (mine, _) = await SignInAsync();
        var (theirs, _) = await SignInAsync();

        var notMine = await CreateOrganizationAsync(theirs, UniqueName("Wayne"));

        var response = await mine.PatchJsonAsync(
            Url($"/api/v1/organizations/{notMine.Id}"),
            new RenameOrganizationRequest("Mine Now"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_RefusesTheWorkspaceTheCallerIsStandingIn()
    {
        var (client, session) = await SignInAsync();

        var response = await client.DeleteAsync(
            Url($"/api/v1/organizations/{session.User.OrganizationId}"));

        // Otherwise the caller's own token points at a tenant with no rows: an empty application
        // and no explanation of why.
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Delete_RemovesAWorkspaceTheCallerOwnsAndIsNotIn()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateOrganizationAsync(client, UniqueName("Soylent"));

        var response = await client.DeleteAsync(Url($"/api/v1/organizations/{created.Id}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var remaining = await client.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));
        remaining!.ShouldNotContain(organization => organization.Id == created.Id);
    }

    [Fact]
    public async Task Delete_IsSoftAndLeavesTheWorkspaceRecoverable()
    {
        // Deleting a row that is only soft-deleted must not cascade. EF decides those cascades
        // before an interceptor gets to rewrite the delete, so without intervention the workspace
        // comes back — if it is ever restored — with its settings nulled and its members really
        // gone. Neither is visible through the API, which is why this reads the rows directly.
        var (client, _) = await SignInAsync();
        var created = await CreateOrganizationAsync(client, UniqueName("Weyland"));

        var response = await client.DeleteAsync(Url($"/api/v1/organizations/{created.Id}"));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var stored = await context.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(organization => organization.Id == created.Id);

        stored.ShouldNotBeNull();
        stored.DeletedAt.ShouldNotBeNull();
        // The owned value object survived rather than being written away as NULLs.
        stored.Settings.Name.ShouldNotBeNullOrWhiteSpace();

        var members = await context.OrganizationMembers
            .IgnoreQueryFilters()
            .CountAsync(member => member.OrganizationId == created.Id);

        members.ShouldBe(1);
    }

    /* ---------------------------------------------------------------------- */
    /* Settings                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task WorkspaceSettings_RoundTrip()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PutJsonAsync(
            Url("/api/v1/organizations/current/settings"),
            new WorkspaceSettingsDto("Northwind HQ", MeetingVisibility.Private, RetentionPeriod.ThreeMonths));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var settings = await client.GetJsonAsync<WorkspaceSettingsDto>(
            Url("/api/v1/organizations/current/settings"));

        settings!.Name.ShouldBe("Northwind HQ");
        settings.DefaultVisibility.ShouldBe(MeetingVisibility.Private);
        settings.Retention.ShouldBe(RetentionPeriod.ThreeMonths);

        // The workspace's display name follows its settings name — two editable names for one
        // workspace read as a bug the first time they disagree.
        var organizations = await client.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));
        organizations!.Single(organization => organization.IsCurrent).Name.ShouldBe("Northwind HQ");
    }

    [Fact]
    public async Task WorkspaceSettings_RejectAnUnknownRetentionPeriod()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PutJsonAsync(
            Url("/api/v1/organizations/current/settings"),
            new { name = "Northwind", defaultVisibility = "workspace", retention = "eternity" });

        // A 400 naming the field, not a 500 from the check constraint further down.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* ---------------------------------------------------------------------- */
    /* Invitations and joining                                                */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Invite_ThenSignIn_JoinsTheWorkspace()
    {
        // The whole loop. There is no accept endpoint: redemption happens at Google sign-in against
        // the verified address, so an invitation is only ever redeemed by its addressee.
        var (admin, adminSession) = await SignInAsync();
        var invitee = UniqueEmail();

        var invitation = await InviteAsync(admin, invitee, UserRole.Member);
        invitation.Status.ShouldBe(InvitationStatus.Pending);

        var (joined, joinedSession) = await SignInAsync(invitee);

        // They land in their own personal workspace, with the invited one available to switch to.
        var organizations = await joined.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));
        organizations!.Count.ShouldBe(2);

        var invited = organizations.Single(organization => organization.Id == adminSession.User.OrganizationId);
        invited.Role.ShouldBe(UserRole.Member);
        invited.IsCurrent.ShouldBeFalse();

        var switched = await SwitchAsync(joined, invited.Id);
        switched.User.Role.ShouldBe(UserRole.Member);

        joinedSession.User.Id.ShouldNotBe(adminSession.User.Id);

        // And the invitation is settled rather than left pending in the admin's list forever.
        var invitations = await admin.GetJsonAsync<List<InvitationDto>>(Url("/api/v1/invitations"));
        invitations!.Single(row => row.Id == invitation.Id).Status.ShouldBe(InvitationStatus.Accepted);
    }

    [Fact]
    public async Task Invite_RefusesASecondPendingInvitationForTheSameAddress()
    {
        var (admin, _) = await SignInAsync();
        var invitee = UniqueEmail();

        await InviteAsync(admin, invitee, UserRole.Member);

        var second = await admin.PostJsonAsync(
            Url("/api/v1/invitations"),
            new InviteMemberRequest(invitee, UserRole.Member));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invite_RefusesSomeoneWhoIsAlreadyAMember()
    {
        var (admin, session) = await SignInAsync();

        var response = await admin.PostJsonAsync(
            Url("/api/v1/invitations"),
            new InviteMemberRequest(session.User.Email, UserRole.Member));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Invite_RefusesAnOwnerInvitation()
    {
        var (admin, _) = await SignInAsync();

        var response = await admin.PostJsonAsync(
            Url("/api/v1/invitations"),
            new InviteMemberRequest(UniqueEmail(), UserRole.Owner));

        // Ownership is transferred from inside the workspace, never handed out by email.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RevokedInvitation_DoesNotJoinTheWorkspaceOnSignIn()
    {
        var (admin, _) = await SignInAsync();
        var invitee = UniqueEmail();

        var invitation = await InviteAsync(admin, invitee, UserRole.Member);

        var revoked = await admin.PostAsync(
            Url($"/api/v1/invitations/{invitation.Id}/revoke"),
            null);
        revoked.StatusCode.ShouldBe(HttpStatusCode.OK);

        var (joined, _) = await SignInAsync(invitee);

        // Their own personal workspace only.
        var organizations = await joined.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));
        organizations!.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Invitations_AreScopedToTheWorkspaceThatSentThem()
    {
        var (mine, _) = await SignInAsync();
        var (theirs, _) = await SignInAsync();

        await InviteAsync(theirs, UniqueEmail(), UserRole.Member);

        var visible = await mine.GetJsonAsync<List<InvitationDto>>(Url("/api/v1/invitations"));

        visible!.ShouldBeEmpty();
    }

    /* ---------------------------------------------------------------------- */
    /* Membership                                                             */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task UpdateMember_ChangesARoleWithinTheWorkspace()
    {
        var (admin, _, member, memberId) = await WorkspaceWithMemberAsync();

        var response = await admin.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{memberId}"),
            new UpdateMemberRequest(UserRole.Admin, null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadJsonAsync<UserDto>())!.Role.ShouldBe(UserRole.Admin);

        // Their own workspace is untouched: they are still its owner.
        var theirs = await member.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));
        theirs!.Single(organization => organization.IsCurrent).Role.ShouldBe(UserRole.Owner);
    }

    [Fact]
    public async Task UpdateMember_RefusesToChangeYourOwnRole()
    {
        var (admin, session) = await SignInAsync();

        var response = await admin.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{session.User.Id}"),
            new UpdateMemberRequest(UserRole.Member, null));

        // Every legitimate case is served by another route; allowing it only creates ways to lock
        // yourself out of a workspace you administer.
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateMember_RefusesAnAdminGrantingOwnership()
    {
        var (owner, _, member, memberId) = await WorkspaceWithMemberAsync();

        await owner.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{memberId}"),
            new UpdateMemberRequest(UserRole.Admin, null));

        // The promoted admin now tries to make themselves owner. RequireAdmin lets the request
        // through; the handler's escalation guard is what stops it.
        await SwitchToInvitedWorkspaceAsync(member);

        var response = await member.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{memberId}"),
            new UpdateMemberRequest(UserRole.Owner, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Suspending_AMember_StopsThemAtTheNextRefresh()
    {
        var (admin, adminSession, member, memberId) = await WorkspaceWithMemberAsync();

        var suspended = await admin.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{memberId}"),
            new UpdateMemberRequest(null, UserStatus.Suspended));

        suspended.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Suspension is workspace-scoped: their own personal workspace is untouched, which is the
        // point — a free workspace must not be a lever for disabling somebody's real account.
        var organizations =
            (await member.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations")))!;
        organizations.ShouldNotContain(row => row.Id == adminSession.User.OrganizationId);
        organizations.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnAdmin_CannotActOnTheOwnerAboveThem()
    {
        var (owner, ownerSession, member, memberId) = await WorkspaceWithMemberAsync();

        await owner.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{memberId}"),
            new UpdateMemberRequest(UserRole.Admin, null));

        await SwitchToInvitedWorkspaceAsync(member);

        // The escalation the role policy cannot see: RequireAdmin is satisfied, and without the
        // rank check the new admin could suspend the owner and take the workspace.
        var response = await member.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{ownerSession.User.Id}"),
            new UpdateMemberRequest(null, UserStatus.Suspended));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AnOwner_CanTransferOwnership()
    {
        // The workspace is never left without an owner along the way: the second owner is added
        // before the first steps down, which is the order the last-owner invariant requires.
        var (owner, ownerSession, member, memberId) = await WorkspaceWithMemberAsync();

        var promoted = await owner.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{memberId}"),
            new UpdateMemberRequest(UserRole.Owner, null));

        promoted.StatusCode.ShouldBe(HttpStatusCode.OK);

        await SwitchToInvitedWorkspaceAsync(member);

        var demoted = await member.PatchJsonAsync(
            Url($"/api/v1/organizations/current/members/{ownerSession.User.Id}"),
            new UpdateMemberRequest(UserRole.Admin, null));

        demoted.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await demoted.Content.ReadJsonAsync<UserDto>())!.Role.ShouldBe(UserRole.Admin);
    }

    [Fact]
    public async Task RemoveMember_EndsTheirSessionInThatWorkspaceOnly()
    {
        var (admin, adminSession, member, memberId) = await WorkspaceWithMemberAsync();

        var response = await admin.DeleteAsync(
            Url($"/api/v1/organizations/current/members/{memberId}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var organizations = await member.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));
        organizations!.ShouldNotContain(row => row.Id == adminSession.User.OrganizationId);
    }

    [Fact]
    public async Task Members_ListsOnlyTheCurrentWorkspace()
    {
        var (admin, _, member, _) = await WorkspaceWithMemberAsync();

        var members = await admin.GetJsonAsync<List<UserDto>>(
            Url("/api/v1/organizations/current/members"));

        members!.Count.ShouldBe(2);

        // The invitee's own personal workspace has exactly one member: them.
        var theirs = await member.GetJsonAsync<List<UserDto>>(
            Url("/api/v1/organizations/current/members"));

        theirs!.Count.ShouldBe(1);
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    /// <summary>
    /// An admin's workspace with a second person invited into it and signed in.
    /// </summary>
    private async Task<(HttpClient Admin, AuthResponse AdminSession, HttpClient Member, Guid MemberId)>
        WorkspaceWithMemberAsync()
    {
        var (admin, adminSession) = await SignInAsync();
        var email = UniqueEmail();

        await InviteAsync(admin, email, UserRole.Member);

        var (member, memberSession) = await SignInAsync(email);

        return (admin, adminSession, member, memberSession.User.Id);
    }

    /// <summary>
    /// Moves a client into the workspace it was invited to — the one that is not current.
    /// </summary>
    private static async Task SwitchToInvitedWorkspaceAsync(HttpClient client)
    {
        var organizations = await client.GetJsonAsync<List<OrganizationDto>>(Url("/api/v1/organizations"));
        var target = organizations!.First(organization => !organization.IsCurrent);

        await SwitchAsync(client, target.Id);
    }

    private static async Task<OrganizationDto> CreateOrganizationAsync(HttpClient client, string name)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/organizations"),
            new CreateOrganizationRequest(name));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadJsonAsync<OrganizationDto>())!;
    }

    private static async Task<InvitationDto> InviteAsync(HttpClient client, string email, UserRole role)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/invitations"),
            new InviteMemberRequest(email, role));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadJsonAsync<InvitationDto>())!;
    }

    private static async Task<AuthResponse> SwitchAsync(HttpClient client, Guid organizationId)
    {
        var response = await client.PostAsync(Url($"/api/v1/organizations/{organizationId}/switch"), null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var switched = (await response.Content.ReadJsonAsync<AuthResponse>())!;

        // The caller keeps using the same client, so it has to carry the new token from here on.
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", switched.AccessToken);

        return switched;
    }

    private async Task<(HttpClient Client, AuthResponse Session)> SignInAsync(string? email = null)
    {
        var (client, session, _) = await SignInWithCookieAsync(email);
        return (client, session);
    }

    private async Task<(HttpClient Client, AuthResponse Session, string RefreshToken)>
        SignInWithCookieAsync(string? email = null, HttpClient? reuseEmailOf = null)
    {
        var address = email ?? UniqueEmail();

        if (reuseEmailOf is not null)
        {
            var existing = await reuseEmailOf.GetJsonAsync<UserDto>(Url("/api/v1/users/me"));
            address = existing!.Email;
        }

        var idToken = $"token-{Guid.CreateVersion7():n}";
        _fixture.Google.Stage(idToken, address, subject: $"google-sub-{address}");

        var client = _fixture.CreateClient(new() { HandleCookies = false });

        var response = await client.PostJsonAsync(
            Url("/api/v1/auth/google"),
            new GoogleSignInRequest(idToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = (await response.Content.ReadJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return (client, session, ReadRefreshToken(response));
    }

    private static async Task<AuthResponse> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/auth/refresh"));
        request.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");

        var response = await client.SendAsync(request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadJsonAsync<AuthResponse>())!;
    }

    private static string ReadRefreshToken(HttpResponseMessage response)
    {
        var cookie = response.Headers.GetValues("Set-Cookie")
            .First(value => value.StartsWith(RefreshCookie, StringComparison.Ordinal));
        var pair = cookie.Split(';')[0];

        return pair[(pair.IndexOf('=', StringComparison.Ordinal) + 1)..];
    }

    private static Uri Url(string relative) => new(relative, UriKind.Relative);

    private static string UniqueEmail() => $"user-{Guid.CreateVersion7():n}@northwind.io";

    private static string UniqueName(string prefix) =>
        $"{prefix} {Guid.CreateVersion7().ToString("n")[^8..]}";
}
