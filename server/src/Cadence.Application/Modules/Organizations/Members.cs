using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Organizations;

/// <summary>Changes a member's role or their standing in the current workspace.</summary>
public sealed record UpdateMemberCommand(Guid UserId, UpdateMemberRequest Change)
    : ICommand<Result<UserDto>>;

/// <summary>Removes someone from the current workspace, keeping their account and their history.</summary>
public sealed record RemoveMemberCommand(Guid UserId) : ICommand<Result>;

internal sealed class UpdateMemberValidator : AbstractValidator<UpdateMemberCommand>
{
    public UpdateMemberValidator()
    {
        RuleFor(command => command.Change.Role).IsInEnum().When(command => command.Change.Role.HasValue);
        RuleFor(command => command.Change.Status).IsInEnum().When(command => command.Change.Status.HasValue);

        RuleFor(command => command.Change)
            .Must(change => change.Role.HasValue || change.Status.HasValue)
            .WithMessage("Supply a role or a status to change.");
    }
}

public sealed class UpdateMemberHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<UpdateMemberCommand, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(
        UpdateMemberCommand command,
        CancellationToken cancellationToken)
    {
        var loaded = await OrganizationAccess.RequireOrganizationAsync(
            context,
            currentUser,
            currentUser.RequireOrganizationId(),
            UserRole.Admin,
            cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<UserDto>(loaded.Error);
        }

        var organization = loaded.Value;
        var actor = organization.Members.Single(member => member.UserId == currentUser.RequireId());

        var target = organization.Members.FirstOrDefault(member => member.UserId == command.UserId);

        if (target is null)
        {
            return Result.Failure<UserDto>(Error.NotFound(
                "member.not_found",
                "That person is not a member of this workspace."));
        }

        var permitted = CheckPermission(actor, target, command.Change);

        if (permitted.IsFailure)
        {
            return Result.Failure<UserDto>(permitted.Error);
        }

        try
        {
            // The aggregate owns the invariants — last-owner protection above all — so both changes
            // go through it rather than being applied to the membership row directly.
            if (command.Change.Role is { } role)
            {
                organization.ChangeMemberRole(command.UserId, role);
            }

            if (command.Change.Status is { } status)
            {
                organization.SetMemberStatus(command.UserId, status);
            }
        }
        catch (DomainException exception)
        {
            // A refused invariant is a conflict, not a crash: "this is the only owner" is something
            // the user can act on, and the message is written to be shown to them.
            return Result.Failure<UserDto>(Error.Conflict("member.invariant", exception.Message));
        }

        await context.SaveChangesAsync(cancellationToken);

        var user = await context.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.UserId, cancellationToken);

        return user is null
            ? Result.Failure<UserDto>(Error.NotFound("member.not_found", "That member could not be loaded."))
            : Result.Success(user.ToDto(target));
    }

    /// <summary>
    /// The two escalation guards the role policy cannot express.
    /// </summary>
    /// <remarks>
    /// <c>RequireAdmin</c> proves the caller is an admin. It says nothing about <i>who they are
    /// acting on</i>, and both gaps it leaves are privilege escalation:
    /// <list type="number">
    /// <item>
    /// An admin granting ownership — to anyone, including themselves — would make "only an owner may
    /// transfer ownership" (§5.4) a one-step self-promotion.
    /// </item>
    /// <item>
    /// An admin acting on an owner would let them suspend the person above them and take the
    /// workspace.
    /// </item>
    /// </list>
    /// Self-mutation is refused outright. Every legitimate case is served by another route, and
    /// allowing it only creates ways to lock yourself out of a workspace you administer.
    /// </remarks>
    private static Result CheckPermission(
        OrganizationMember actor,
        OrganizationMember target,
        UpdateMemberRequest change)
    {
        if (actor.UserId == target.UserId)
        {
            return Result.Failure(Error.Forbidden(
                "member.self",
                "You cannot change your own role or status."));
        }

        // Roles are declared most-privileged first, so a *larger* value is a lesser role.
        if (target.Role < actor.Role)
        {
            return Result.Failure(Error.Forbidden(
                "member.outranked",
                "You cannot change the membership of someone with a higher role."));
        }

        if (change.Role == UserRole.Owner && actor.Role != UserRole.Owner)
        {
            return Result.Failure(Error.Forbidden(
                "member.owner_transfer",
                "Only an owner can transfer ownership."));
        }

        return Result.Success();
    }
}

public sealed class RemoveMemberHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<RemoveMemberCommand, Result>
{
    public async ValueTask<Result> Handle(
        RemoveMemberCommand command,
        CancellationToken cancellationToken)
    {
        var loaded = await OrganizationAccess.RequireOrganizationAsync(
            context,
            currentUser,
            currentUser.RequireOrganizationId(),
            UserRole.Admin,
            cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Error);
        }

        var organization = loaded.Value;
        var actor = organization.Members.Single(member => member.UserId == currentUser.RequireId());
        var target = organization.Members.FirstOrDefault(member => member.UserId == command.UserId);

        if (target is null)
        {
            return Result.Failure(Error.NotFound(
                "member.not_found",
                "That person is not a member of this workspace."));
        }

        if (actor.UserId == target.UserId)
        {
            return Result.Failure(Error.Forbidden(
                "member.self",
                "You cannot remove yourself from a workspace you administer."));
        }

        if (target.Role < actor.Role)
        {
            return Result.Failure(Error.Forbidden(
                "member.outranked",
                "You cannot remove someone with a higher role."));
        }

        try
        {
            // Removal deletes the membership, never the user (§5.3). Their meetings, comments and
            // completed work stay with the workspace's history, and action items assigned to them
            // are unassigned by the SET NULL on the foreign key rather than deleted with them.
            organization.RemoveMember(command.UserId);
        }
        catch (DomainException exception)
        {
            return Result.Failure(Error.Conflict("member.invariant", exception.Message));
        }

        await RevokeSessionsAsync(command.UserId, organization.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// Ends the removed person's sessions in this workspace.
    /// </summary>
    /// <remarks>
    /// Their access token stays valid for up to fifteen minutes regardless — that is the accepted
    /// cost of stateless authorization (§4.4) — but leaving the refresh token pointed here would let
    /// it keep minting new ones indefinitely, so removal would never actually take effect.
    /// </remarks>
    private async Task RevokeSessionsAsync(
        Guid userId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var sessions = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.UserId == userId
                && token.OrganizationId == organizationId
                && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke();
        }
    }
}
