using System.Net;
using System.Net.Http.Json;
using Cadence.Application.Modules.Auth;
using Cadence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// The sign-in, rotation and reuse-detection path, end to end against a real database.
/// </summary>
public sealed class AuthFlowTests : IClassFixture<AuthFixture>
{
    private const string RefreshCookie = "cadence_refresh";

    private readonly AuthFixture _fixture;

    public AuthFlowTests(AuthFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task FirstSignIn_ProvisionsAUserAWorkspaceAndPreferences()
    {
        // A user without a workspace can see nothing and one without preferences breaks settings,
        // so a partial provision would be worse than a failed one.
        using var client = CreateClient();
        var email = UniqueEmail();
        _fixture.Google.Stage("token-provision", email);

        var response = await SignInAsync(client, "token-provision");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var auth = await response.Content.ReadFromJsonAsync<AuthResponse>();
        auth.ShouldNotBeNull();
        auth.User.Email.ShouldBe(email);
        auth.User.OrganizationId.ShouldNotBe(Guid.Empty);

        await using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var user = await context.Users.IgnoreQueryFilters()
            .SingleAsync(candidate => candidate.Email == email);

        (await context.UserPreferences.IgnoreQueryFilters()
            .AnyAsync(preferences => preferences.UserId == user.Id)).ShouldBeTrue();
        (await context.OrganizationMembers.IgnoreQueryFilters()
            .AnyAsync(member => member.UserId == user.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task SigningInTwice_DoesNotCreateASecondAccount()
    {
        using var client = CreateClient();
        var email = UniqueEmail();
        _fixture.Google.Stage("token-repeat", email);

        await SignInAsync(client, "token-repeat");
        await SignInAsync(client, "token-repeat");

        await using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        (await context.Users.IgnoreQueryFilters().CountAsync(user => user.Email == email)).ShouldBe(1);
    }

    [Fact]
    public async Task AnUnverifiedEmail_DoesNotLinkToAnExistingAccount()
    {
        // The account-takeover check. Anyone able to mint an unverified token for an existing
        // address would otherwise inherit that account (§4.2).
        using var client = CreateClient();
        var email = UniqueEmail();
        _fixture.Google.Stage("token-original", email, subject: "sub-original");
        await SignInAsync(client, "token-original");

        _fixture.Google.Stage("token-attacker", email, emailVerified: false, subject: "sub-attacker");
        var attacker = await SignInAsync(client, "token-attacker");

        // Refused outright. Not given an account of its own either — email is the identity key, so
        // two accounts sharing one address would break the premise the whole model rests on.
        attacker.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var logins = await context.ExternalLogins.IgnoreQueryFilters()
            .Where(login => login.EmailAtProvider == email)
            .ToListAsync();

        logins.ShouldHaveSingleItem().Subject.ShouldBe("sub-original");
    }

    [Fact]
    public async Task TheRefreshTokenIsAnHttpOnlyCookie_NotInTheBody()
    {
        using var client = CreateClient();
        _fixture.Google.Stage("token-cookie", UniqueEmail());

        var response = await SignInAsync(client, "token-cookie");

        var cookie = SetCookie(response);
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);

        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain("refresh", Case.Insensitive);
    }

    [Fact]
    public async Task Refresh_RotatesTheTokenAndIssuesANewAccessToken()
    {
        using var client = CreateClient();
        _fixture.Google.Stage("token-rotate", UniqueEmail());
        var signIn = await SignInAsync(client, "token-rotate");
        var first = ReadRefreshToken(signIn);

        var refreshed = await RefreshAsync(client, first);

        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        ReadRefreshToken(refreshed).ShouldNotBe(first);
    }

    [Fact]
    public async Task ARotatedTokenIsRejected_AndRevokesTheWholeFamily()
    {
        // Presenting an already-rotated token means two parties hold tokens from one session. We
        // cannot tell which is the thief, so the session ends for both (§4.3).
        using var client = CreateClient();
        _fixture.Google.Stage("token-reuse", UniqueEmail());
        var signIn = await SignInAsync(client, "token-reuse");
        var first = ReadRefreshToken(signIn);

        var refreshed = await RefreshAsync(client, first);
        var successor = ReadRefreshToken(refreshed);

        // The thief replays the token the legitimate client already spent.
        var replay = await RefreshAsync(client, first);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // And the successor the legitimate client is holding is dead too.
        (await RefreshAsync(client, successor)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AFailedRefresh_ClearsTheCookie()
    {
        // Otherwise the client keeps retrying a doomed refresh on every request.
        using var client = CreateClient();

        var response = await RefreshAsync(client, "not-a-real-token");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        SetCookie(response).ShouldContain(RefreshCookie);
    }

    [Fact]
    public async Task RefreshWithNoCookie_IsRejected()
    {
        using var client = CreateClient();

        var response = await client.PostAsync(new Uri("/api/v1/auth/refresh", UriKind.Relative), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task TheAccessTokenAuthenticatesASubsequentRequest()
    {
        using var client = CreateClient();
        _fixture.Google.Stage("token-bearer", UniqueEmail());
        var auth = await (await SignInAsync(client, "token-bearer")).Content.ReadFromJsonAsync<AuthResponse>();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/v1/auth/logout", UriKind.Relative));
        request.Headers.Authorization = new("Bearer", auth!.AccessToken);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task LogoutWithoutATokenIsRejected()
    {
        using var client = CreateClient();

        var response = await client.PostAsync(new Uri("/api/v1/auth/logout", UriKind.Relative), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_RevokesTheSessionSoItsRefreshTokenStopsWorking()
    {
        using var client = CreateClient();
        _fixture.Google.Stage("token-logout", UniqueEmail());
        var signIn = await SignInAsync(client, "token-logout");
        var refreshToken = ReadRefreshToken(signIn);
        var auth = await signIn.Content.ReadFromJsonAsync<AuthResponse>();

        using var logout = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/v1/auth/logout", UriKind.Relative));
        logout.Headers.Authorization = new("Bearer", auth!.AccessToken);
        logout.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");

        (await client.SendAsync(logout)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RefreshAsync(client, refreshToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnInvalidGoogleToken_IsRejected()
    {
        using var client = CreateClient();

        var response = await SignInAsync(client, "never-staged");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RefreshTokensAreStoredHashed_NeverInPlaintext()
    {
        // A database leak must not hand over live sessions.
        using var client = CreateClient();
        _fixture.Google.Stage("token-hashed", UniqueEmail());
        var refreshToken = ReadRefreshToken(await SignInAsync(client, "token-hashed"));

        await using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        (await context.RefreshTokens.IgnoreQueryFilters()
            .AnyAsync(token => token.TokenHash == refreshToken)).ShouldBeFalse();
    }

    private HttpClient CreateClient() =>
        _fixture.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            // Cookies are set and read explicitly, so each test controls exactly which token it
            // presents — a shared cookie jar would hide a rotation bug behind the client's own state.
            HandleCookies = false,
        });

    private static Task<HttpResponseMessage> SignInAsync(HttpClient client, string idToken) =>
        client.PostAsJsonAsync(
            new Uri("/api/v1/auth/google", UriKind.Relative),
            new GoogleSignInRequest(idToken));

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri("/api/v1/auth/refresh", UriKind.Relative));
        request.Headers.Add("Cookie", $"{RefreshCookie}={refreshToken}");

        return await client.SendAsync(request);
    }

    private static string ReadRefreshToken(HttpResponseMessage response)
    {
        var cookie = SetCookie(response);
        var value = cookie.Split(';')[0];

        return value[(value.IndexOf('=', StringComparison.Ordinal) + 1)..];
    }

    private static string SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.First(value => value.StartsWith(RefreshCookie, StringComparison.Ordinal))
            : throw new InvalidOperationException("The response set no refresh cookie.");

    private static string UniqueEmail() => $"user-{Guid.CreateVersion7():n}@northwind.io";
}
