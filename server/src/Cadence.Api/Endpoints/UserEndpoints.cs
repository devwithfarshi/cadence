using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Users;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// Profile, preferences, sessions and the member directory.
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users")
            .WithTags("Users")
            .RequireAuthorization();

        group.MapGet("/me", GetMeAsync)
            .WithName("GetCurrentUser")
            .WithSummary("The signed-in user, with their role in the current workspace")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPatch("/me", UpdateMeAsync)
            .WithName("UpdateProfile")
            .WithSummary("Update the editable parts of the signed-in user's profile")
            .WithDescription("Email is not editable: it is the Google-owned identity key.")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/me/preferences", GetPreferencesAsync)
            .WithName("GetPreferences")
            .WithSummary("The signed-in user's settings")
            .Produces<PreferencesDto>(StatusCodes.Status200OK);

        group.MapPut("/me/preferences", UpdatePreferencesAsync)
            .WithName("UpdatePreferences")
            .WithSummary("Replace the signed-in user's settings")
            .WithDescription(
                "A full replace. Recent meetings and searches are server-maintained usage history "
                + "and are ignored if supplied.")
            .Produces<PreferencesDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/me/sessions", GetSessionsAsync)
            .WithName("GetSessions")
            .WithSummary("Active sign-ins for the current user")
            .WithDescription("One entry per sign-in, not per rotated token.")
            .Produces<IReadOnlyList<SessionDto>>(StatusCodes.Status200OK);

        group.MapDelete("/me/sessions/{sessionId:guid}", RevokeSessionAsync)
            .WithName("RevokeSession")
            .WithSummary("Sign out one session")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/me/sessions", RevokeOtherSessionsAsync)
            .WithName("RevokeOtherSessions")
            .WithSummary("Sign out everywhere except this device")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireMember)
            .WithName("ListUsers")
            .WithSummary("The member directory for the current workspace")
            .Produces<IReadOnlyList<UserDto>>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> GetMeAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);

        // TypedResults, never Results: the latter erases the type and OpenAPI documents the response
        // as `object`, which makes a generated client useless (§17.2).
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> UpdateMeAsync(
        [FromBody] UpdateProfileRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateProfileCommand(request), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> GetPreferencesAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetPreferencesQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> UpdatePreferencesAsync(
        [FromBody] PreferencesDto request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdatePreferencesCommand(request), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> GetSessionsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSessionsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> RevokeSessionAsync(
        Guid sessionId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RevokeSessionCommand(sessionId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RevokeOtherSessionsCommand(), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    /// <remarks>
    /// <paramref name="role"/> and <paramref name="status"/> bind as strings rather than enums:
    /// query-string binding bypasses the JSON converter and parses case-sensitively, so
    /// <c>?role=admin</c> — the spelling the rest of the API uses — would be rejected as a malformed
    /// request. See <see cref="EnumQuery"/>.
    /// </remarks>
    private static async Task<IResult> ListAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] string? status = null)
    {
        if (!string.IsNullOrWhiteSpace(role) && !EnumQuery.TryParse<UserRole>(role, out _))
        {
            return Result
                .Failure(Error.Validation(
                    "user.invalid_role",
                    $"'{role}' is not a role. Expected one of: {EnumQuery.Allowed<UserRole>()}."))
                .ToProblem(context);
        }

        if (!string.IsNullOrWhiteSpace(status) && !EnumQuery.TryParse<UserStatus>(status, out _))
        {
            return Result
                .Failure(Error.Validation(
                    "user.invalid_status",
                    $"'{status}' is not a status. Expected one of: {EnumQuery.Allowed<UserStatus>()}."))
                .ToProblem(context);
        }

        var result = await sender.Send(
            new ListUsersQuery(
                search,
                EnumQuery.TryParse<UserRole>(role, out var parsedRole) ? parsedRole : null,
                EnumQuery.TryParse<UserStatus>(status, out var parsedStatus) ? parsedStatus : null),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }
}
