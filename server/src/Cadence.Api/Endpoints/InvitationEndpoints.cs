using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Application.Modules.Organizations;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// Invitations to the current workspace.
/// </summary>
/// <remarks>
/// There is no accept endpoint, deliberately. An invitation is redeemed by signing in with Google
/// and matching the <b>verified</b> address on it — the emailed token identifies the invitation but
/// authenticates nobody, so a forwarded link cannot be redeemed by whoever received it (§5.6).
/// </remarks>
public static class InvitationEndpoints
{
    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin throughout, and safe as a claim check alone: every route here acts on the workspace
        // the token already names, so there is no second workspace for the claim to be wrong about.
        var group = app.MapGroup("/api/v1/invitations")
            .WithTags("Invitations")
            .RequireAuthorization(AuthenticationConfiguration.RequireAdmin);

        group.MapGet("/", ListAsync)
            .WithName("ListInvitations")
            .WithSummary("Invitations for the current workspace, newest first")
            .WithDescription("The token is never returned; only its hash is stored.")
            .Produces<IReadOnlyList<InvitationDto>>(StatusCodes.Status200OK);

        group.MapPost("/", InviteAsync)
            .WithName("InviteMember")
            .WithSummary("Invite an email address to the current workspace")
            .Produces<InvitationDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{invitationId:guid}/resend", ResendAsync)
            .WithName("ResendInvitation")
            .WithSummary("Issue a fresh token and expiry")
            .WithDescription("The previous link stops working.")
            .Produces<InvitationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{invitationId:guid}/revoke", RevokeAsync)
            .WithName("RevokeInvitation")
            .WithSummary("Withdraw a pending invitation")
            .Produces<InvitationDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListInvitationsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> InviteAsync(
        [FromBody] InviteMemberRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new InviteMemberCommand(request), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/invitations/{result.Value.Id}", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> ResendAsync(
        Guid invitationId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ResendInvitationCommand(invitationId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> RevokeAsync(
        Guid invitationId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RevokeInvitationCommand(invitationId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }
}
