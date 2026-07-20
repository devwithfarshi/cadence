using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Users;
using Cadence.Domain.Enums;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// Profile, preferences, sessions and the directory, against a real database.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UserFlowTests
{
    private const string RefreshCookie = "cadence_refresh";

    private readonly AuthFixture _fixture;

    public UserFlowTests(AuthFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task GetMe_ReturnsTheSignedInUserWithTheirWorkspaceRole()
    {
        var (client, session) = await SignInAsync();

        var me = await client.GetJsonAsync<UserDto>(Url("/api/v1/users/me"));

        me.ShouldNotBeNull();
        me.Email.ShouldBe(session.User.Email);
        // Owner of the personal workspace provisioned on first sign-in.
        me.Role.ShouldBe(UserRole.Owner);
        me.OrganizationId.ShouldBe(session.User.OrganizationId);
    }

    [Fact]
    public async Task GetMe_RequiresAuthentication()
    {
        using var anonymous = _fixture.CreateClient();

        var response = await anonymous.GetAsync(Url("/api/v1/users/me"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateProfile_PersistsTheEditableFields()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PatchJsonAsync(
            Url("/api/v1/users/me"),
            new UpdateProfileRequest("Alex R.", "Staff Engineer", "Platform", "Europe/London", null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = await client.GetJsonAsync<UserDto>(Url("/api/v1/users/me"));
        updated!.Name.ShouldBe("Alex R.");
        updated.JobTitle.ShouldBe("Staff Engineer");
        updated.Timezone.ShouldBe("Europe/London");
    }

    [Fact]
    public async Task UpdateProfile_RejectsAnUnknownTimezone()
    {
        // Caught on write. A bad zone stored once produces a wrong time on every screen afterwards,
        // with nothing pointing back at where it came from.
        var (client, _) = await SignInAsync();

        var response = await client.PatchJsonAsync(
            Url("/api/v1/users/me"),
            new UpdateProfileRequest("Alex", "", "", "Mars/Olympus_Mons", null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateProfile_CannotChangeTheEmail()
    {
        // Email is the Google-owned identity key. It is absent from the request contract, so posting
        // one changes nothing rather than being silently accepted.
        var (client, session) = await SignInAsync();

        await client.PatchJsonAsync(
            Url("/api/v1/users/me"),
            new { name = "Alex", jobTitle = "", department = "", timezone = "UTC", email = "attacker@evil.io" });

        var updated = await client.GetJsonAsync<UserDto>(Url("/api/v1/users/me"));
        updated!.Email.ShouldBe(session.User.Email);
    }

    [Fact]
    public async Task Preferences_DefaultToSafeValues()
    {
        var (client, _) = await SignInAsync();

        var preferences = await client.GetJsonAsync<PreferencesDto>(Url("/api/v1/users/me/preferences"));

        preferences.ShouldNotBeNull();
        // The model must never silently assign work to a colleague.
        preferences.Ai.RequireActionItemReview.ShouldBeTrue();
        // Anything that sends mail is opt-in.
        preferences.Notifications.Email.Count.ShouldBeLessThan(preferences.Notifications.InApp.Count);
    }

    [Fact]
    public async Task Preferences_RoundTripThroughTheDatabase()
    {
        // The real point of this test: the owned value objects and their primitive collections
        // actually persist and read back. That is where the last set of EF defects lived.
        var (client, _) = await SignInAsync();
        var original = await client.GetJsonAsync<PreferencesDto>(Url("/api/v1/users/me/preferences"));

        var updated = original! with
        {
            Theme = ThemeMode.Dark,
            Density = UiDensity.Compact,
            TasksView = TasksView.Board,
            Language = "de",
            Notifications = new NotificationPreferencesDto(
                [NotificationKind.Mention],
                [NotificationKind.Mention, NotificationKind.SummaryReady]),
            Ai = original.Ai with { SummaryLength = SummaryLength.Detailed, OutputLanguage = "de" },
        };

        (await client.PutJsonAsync(Url("/api/v1/users/me/preferences"), updated))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var reloaded = await client.GetJsonAsync<PreferencesDto>(Url("/api/v1/users/me/preferences"));

        reloaded!.Theme.ShouldBe(ThemeMode.Dark);
        reloaded.Density.ShouldBe(UiDensity.Compact);
        reloaded.TasksView.ShouldBe(TasksView.Board);
        reloaded.Language.ShouldBe("de");
        reloaded.Notifications.InApp.ShouldBe([NotificationKind.Mention]);
        reloaded.Notifications.Email.Count.ShouldBe(2);
        reloaded.Ai.SummaryLength.ShouldBe(SummaryLength.Detailed);
        reloaded.Ai.OutputLanguage.ShouldBe("de");
    }

    [Fact]
    public async Task Preferences_RejectAnEmptyLanguage()
    {
        var (client, _) = await SignInAsync();
        var original = await client.GetJsonAsync<PreferencesDto>(Url("/api/v1/users/me/preferences"));

        var response = await client.PutJsonAsync(
            Url("/api/v1/users/me/preferences"),
            original! with { Language = "" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sessions_ListOneEntryPerSignIn_NotPerRotatedToken()
    {
        var (client, _, refreshToken) = await SignInWithCookieAsync();

        // Rotate twice. A session is a family, so this must still read as one sign-in.
        var first = await RefreshAsync(client, refreshToken);
        await RefreshAsync(client, first);

        var sessions = await client.GetJsonAsync<IReadOnlyList<SessionDto>>(
            Url("/api/v1/users/me/sessions"));

        sessions.ShouldNotBeNull();
        sessions.Count.ShouldBe(1);
        sessions[0].IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public async Task Sessions_MarkTheCallersOwnSession()
    {
        var (client, _, _) = await SignInWithCookieAsync();

        // A second sign-in for the same person opens a second session.
        var (_, _, _) = await SignInWithCookieAsync(reuseEmailOf: client);

        var sessions = await client.GetJsonAsync<IReadOnlyList<SessionDto>>(
            Url("/api/v1/users/me/sessions"));

        sessions!.Count(session => session.IsCurrent).ShouldBe(1);
    }

    [Fact]
    public async Task RevokingAnotherUsersSession_IsRefused()
    {
        // Without scoping the revoke to the caller's own tokens, anyone with a valid token could
        // sign out any session in the system by guessing a family id.
        var (mine, _, _) = await SignInWithCookieAsync();
        var (_, _, theirRefresh) = await SignInWithCookieAsync();

        var theirSessions = await SessionsFor(theirRefresh);

        var response = await mine.DeleteAsync(Url($"/api/v1/users/me/sessions/{theirSessions[0].Id}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevokingASession_StopsItsRefreshTokenWorking()
    {
        var (client, _, refreshToken) = await SignInWithCookieAsync();
        var sessions = await client.GetJsonAsync<IReadOnlyList<SessionDto>>(
            Url("/api/v1/users/me/sessions"));

        (await client.DeleteAsync(Url($"/api/v1/users/me/sessions/{sessions![0].Id}")))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var refresh = await RefreshRawAsync(client, refreshToken);
        refresh.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RevokingAnUnknownSession_Is404()
    {
        var (client, _) = await SignInAsync();

        var response = await client.DeleteAsync(
            Url($"/api/v1/users/me/sessions/{Guid.CreateVersion7()}"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SignOutEverywhereElse_KeepsTheCurrentSession()
    {
        var email = UniqueEmail();
        var (client, _, keptToken) = await SignInWithCookieAsync(email);
        var (_, _, otherToken) = await SignInWithCookieAsync(email);

        (await client.DeleteAsync(Url("/api/v1/users/me/sessions")))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // The caller's own session survives; the other one does not.
        (await RefreshRawAsync(client, keptToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await RefreshRawAsync(client, otherToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheDirectoryListsTheWorkspacesMembers()
    {
        var (client, session) = await SignInAsync();

        var members = await client.GetJsonAsync<IReadOnlyList<UserDto>>(Url("/api/v1/users"));

        // A personal workspace has exactly one member — and crucially not every user in the
        // database, which is what a query against the global User table alone would return.
        members.ShouldHaveSingleItem().Id.ShouldBe(session.User.Id);
    }

    [Fact]
    public async Task TheDirectorySearchIsCaseInsensitive()
    {
        var (client, _) = await SignInAsync();
        await client.PatchJsonAsync(
            Url("/api/v1/users/me"),
            new UpdateProfileRequest("Priya Nair", "Designer", "Product", "UTC", null));

        var hit = await client.GetJsonAsync<IReadOnlyList<UserDto>>(
            Url("/api/v1/users?search=PRIYA"));
        var miss = await client.GetJsonAsync<IReadOnlyList<UserDto>>(
            Url("/api/v1/users?search=nobody-by-that-name"));

        hit.ShouldHaveSingleItem();
        miss.ShouldBeEmpty();
    }

    private async Task<IReadOnlyList<SessionDto>> SessionsFor(string refreshToken)
    {
        using var client = _fixture.CreateClient(new() { HandleCookies = false });
        var refreshed = await RefreshRawAsync(client, refreshToken);
        var auth = await refreshed.Content.ReadJsonAsync<AuthResponse>();

        client.DefaultRequestHeaders.Authorization = new("Bearer", auth!.AccessToken);

        return (await client.GetJsonAsync<IReadOnlyList<SessionDto>>(
            Url("/api/v1/users/me/sessions")))!;
    }

    private async Task<(HttpClient Client, AuthResponse Session)> SignInAsync()
    {
        var (client, session, _) = await SignInWithCookieAsync();
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

        // A distinct token string per sign-in, so the fake validator can stage each independently
        // while they resolve to the same Google subject — which is what makes it the same account.
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

    private static async Task<string> RefreshAsync(HttpClient client, string refreshToken) =>
        ReadRefreshToken(await RefreshRawAsync(client, refreshToken));

    private static async Task<HttpResponseMessage> RefreshRawAsync(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/api/v1/auth/refresh"));
        request.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");

        return await client.SendAsync(request);
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
}
