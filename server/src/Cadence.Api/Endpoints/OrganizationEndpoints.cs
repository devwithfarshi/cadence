using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Organizations;
using Cadence.Application.Modules.Users;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// Workspaces, the workspace switcher, settings and membership.
/// </summary>
/// <remarks>
/// Routes naming a workspace by id are gated twice: a policy for coarse capability, then a check in
/// the handler against the loaded aggregate. The policy alone is not enough, because the
/// <c>role</c> claim describes the caller in their <i>current</i> workspace — an owner of their own
/// personal workspace satisfies <c>RequireOwner</c> on a request naming somebody else's (§4.5).
/// </remarks>
public static class OrganizationEndpoints
{
    public static IEndpointRouteBuilder MapOrganizationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/organizations")
            .WithTags("Organizations")
            .RequireAuthorization();

        // Authenticated rather than RequireMember. A guest's role claim fails that policy, and a
        // guest still has to be able to see and enter the workspace they are a guest of — the
        // handler's membership check is both stricter and correctly scoped to the target workspace.
        group.MapGet("/", ListAsync)
            .WithName("ListOrganizations")
            .WithSummary("Every workspace the caller belongs to")
            .Produces<IReadOnlyList<OrganizationDto>>(StatusCodes.Status200OK);

        group.MapPost("/", CreateAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireMember)
            .WithName("CreateOrganization")
            .WithSummary("Create a workspace, owned by the caller")
            .WithDescription("The caller is not moved into it; switching is an explicit act.")
            .Produces<OrganizationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{organizationId:guid}", RenameAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireAdmin)
            .WithName("RenameOrganization")
            .WithSummary("Rename a workspace")
            .WithDescription("The slug does not change: it is the workspace's stable identifier.")
            .Produces<OrganizationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{organizationId:guid}", DeleteAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireOwner)
            .WithName("DeleteOrganization")
            .WithSummary("Soft-delete a workspace the caller owns")
            .WithDescription("Refused for the workspace the caller is currently in; switch first.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{organizationId:guid}/switch", SwitchAsync)
            .WithName("SwitchOrganization")
            .WithSummary("Move this session into another workspace")
            .WithDescription(
                "Returns a new access token carrying the new `org` claim, and re-scopes the "
                + "session so the choice survives the next refresh. Other devices are unaffected.")
            .Produces<AuthResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/current/settings", GetSettingsAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireMember)
            .WithName("GetWorkspaceSettings")
            .WithSummary("Settings for the current workspace")
            .Produces<WorkspaceSettingsDto>(StatusCodes.Status200OK);

        group.MapPut("/current/settings", UpdateSettingsAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireAdmin)
            .WithName("UpdateWorkspaceSettings")
            .WithSummary("Replace the current workspace's settings")
            .Produces<WorkspaceSettingsDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        // The same projection as the user directory, because a member of the current workspace and
        // a user in the directory are the same row seen from the same angle. One handler, so the two
        // cannot drift into disagreeing about somebody's role.
        group.MapGet("/current/members", ListMembersAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireMember)
            .WithName("ListMembers")
            .WithSummary("Members of the current workspace")
            .Produces<IReadOnlyList<UserDto>>(StatusCodes.Status200OK);

        group.MapPatch("/current/members/{userId:guid}", UpdateMemberAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireAdmin)
            .WithName("UpdateMember")
            .WithSummary("Change a member's role or status in the current workspace")
            .WithDescription(
                "Status is the member's standing in this workspace only — it never touches their "
                + "access to another. Granting ownership requires the caller to be an owner.")
            .Produces<UserDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapDelete("/current/members/{userId:guid}", RemoveMemberAsync)
            .RequireAuthorization(AuthenticationConfiguration.RequireAdmin)
            .WithName("RemoveMember")
            .WithSummary("Remove someone from the current workspace")
            .WithDescription("Deletes the membership, not the account: their history stays.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListOrganizationsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateOrganizationRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateOrganizationCommand(request), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/organizations/{result.Value.Id}", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> RenameAsync(
        Guid organizationId,
        [FromBody] RenameOrganizationRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RenameOrganizationCommand(organizationId, request),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> DeleteAsync(
        Guid organizationId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteOrganizationCommand(organizationId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    private static async Task<IResult> SwitchAsync(
        Guid organizationId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new SwitchOrganizationCommand(organizationId), cancellationToken);

        // No cookie is set: the refresh token is re-pointed, not replaced, so the one the client
        // already holds stays valid and keeps its rotation chain intact.
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> GetSettingsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetWorkspaceSettingsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> UpdateSettingsAsync(
        [FromBody] WorkspaceSettingsDto request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateWorkspaceSettingsCommand(request), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> ListMembersAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null,
        [FromQuery] UserRole? role = null,
        [FromQuery] UserStatus? status = null)
    {
        var result = await sender.Send(new ListUsersQuery(search, role, status), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> UpdateMemberAsync(
        Guid userId,
        [FromBody] UpdateMemberRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateMemberCommand(userId, request), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid userId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RemoveMemberCommand(userId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }
}
