using Cadence.Api.Common;
using Cadence.Application.Modules.Auth;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// Google sign-in, refresh and sign-out.
/// </summary>
public static class AuthEndpoints
{
    /// <summary>Cookie name for the refresh token. Never read by JavaScript.</summary>
    private const string RefreshCookie = "cadence_refresh";

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapPost("/google", SignInWithGoogleAsync)
            .AllowAnonymous()
            .WithName("SignInWithGoogle")
            .WithSummary("Exchange a Google ID token for a Cadence session")
            .WithDescription(
                "Obtain the ID token in the browser with Google Identity Services, then post it "
                + "here. Returns an access token in the body and sets an HttpOnly refresh cookie. "
                + "This is the only way to authenticate — there is no password grant.")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/refresh", RefreshAsync)
            .AllowAnonymous()
            .WithName("RefreshSession")
            .WithSummary("Rotate the refresh cookie and issue a new access token")
            .WithDescription(
                "Reads the refresh cookie; no request body. Presenting a token that has already "
                + "been rotated is treated as theft and revokes the whole session family.")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", SignOutAsync)
            .RequireAuthorization()
            .WithName("SignOut")
            .WithSummary("Revoke the current session and clear the refresh cookie")
            .Produces(StatusCodes.Status204NoContent);

        return app;
    }

    private static async Task<IResult> SignInWithGoogleAsync(
        [FromBody] GoogleSignInRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new SignInWithGoogleCommand(request.IdToken, context.ToSessionContext()),
            cancellationToken);

        return result.IsSuccess
            ? Issue(context, result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        // The cookie, never a body or header. A refresh token the client can read is a refresh
        // token an injected script can steal.
        var presented = context.Request.Cookies[RefreshCookie];

        var result = await sender.Send(
            new RefreshSessionCommand(presented ?? string.Empty, context.ToSessionContext()),
            cancellationToken);

        if (result.IsFailure)
        {
            // Clear it: the client is holding something that will never work again, and leaving it
            // in place means every subsequent request retries a doomed refresh.
            context.Response.Cookies.Delete(RefreshCookie);
            return result.ToProblem(context);
        }

        return Issue(context, result.Value);
    }

    private static async Task<IResult> SignOutAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new SignOutCommand(context.Request.Cookies[RefreshCookie]),
            cancellationToken);

        context.Response.Cookies.Delete(RefreshCookie);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    /// <summary>
    /// Returns the access token in the body and the refresh token as a cookie.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><c>HttpOnly</c> — script cannot read it, so an XSS flaw cannot exfiltrate the session.</item>
    /// <item><c>Secure</c> — never sent over plaintext HTTP.</item>
    /// <item>
    /// <c>SameSite=Lax</c> — sent on top-level navigation but not on cross-site subrequests, which
    /// blocks the CSRF shape that matters here. <c>Strict</c> would break returning from Google's
    /// consent screen with a session intact.
    /// </item>
    /// <item><c>Path=/api/v1/auth</c> — the cookie is only ever needed by these endpoints, so it is
    /// not attached to every other API call.</item>
    /// </list>
    /// </remarks>
    private static IResult Issue(HttpContext context, AuthResult result)
    {
        context.Response.Cookies.Append(RefreshCookie, result.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Path = "/api/v1/auth",
            Expires = result.RefreshExpiresAt,
        });

        return TypedResults.Ok(result.Response);
    }

    /// <summary>
    /// Captures who is signing in from where, so the sessions list is recognisable.
    /// </summary>
    private static SessionContext ToSessionContext(this HttpContext context) =>
        new(
            Truncate(context.Request.Headers.UserAgent.ToString(), 255),
            context.Connection.RemoteIpAddress?.ToString());

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Length <= maxLength ? value : value[..maxLength];
}
