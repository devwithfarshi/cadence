using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Organizations;

/// <summary>Invitations for the current workspace, newest first.</summary>
public sealed record ListInvitationsQuery : IQuery<Result<IReadOnlyList<InvitationDto>>>;

/// <summary>Invites an email address to the current workspace.</summary>
public sealed record InviteMemberCommand(InviteMemberRequest Invitation)
    : ICommand<Result<InvitationDto>>;

/// <summary>Issues a fresh token and expiry for an invitation that has gone stale.</summary>
public sealed record ResendInvitationCommand(Guid InvitationId) : ICommand<Result<InvitationDto>>;

/// <summary>Withdraws a pending invitation.</summary>
public sealed record RevokeInvitationCommand(Guid InvitationId) : ICommand<Result<InvitationDto>>;

internal sealed class InviteMemberValidator : AbstractValidator<InviteMemberCommand>
{
    public InviteMemberValidator()
    {
        RuleFor(command => command.Invitation.Email)
            .NotEmpty().WithMessage("Enter an email address.")
            .MaximumLength(320)
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(command => command.Invitation.Role)
            .IsInEnum()
            .NotEqual(UserRole.Owner)
            .WithMessage("Ownership is transferred, not invited.");
    }
}

public sealed class ListInvitationsHandler(ICadenceDbContext context, IDateTime clock)
    : IQueryHandler<ListInvitationsQuery, Result<IReadOnlyList<InvitationDto>>>
{
    public async ValueTask<Result<IReadOnlyList<InvitationDto>>> Handle(
        ListInvitationsQuery query,
        CancellationToken cancellationToken)
    {
        // No tenant predicate: Invitation is ITenantScoped, so the global filter has already scoped
        // this to the caller's workspace. Adding one here would be redundant, and writing it by hand
        // is how the filter eventually gets relied upon in a place that forgot it (§3.3).
        var invitations = await context.Invitations
            .AsNoTracking()
            .OrderByDescending(invitation => invitation.CreatedAt)
            .ToListAsync(cancellationToken);

        var now = clock.UtcNow;

        return Result.Success<IReadOnlyList<InvitationDto>>(
            [.. invitations.Select(invitation => ToDto(invitation, now))]);
    }

    /// <summary>
    /// Projects an invitation, reporting expiry as a status.
    /// </summary>
    /// <remarks>
    /// Expiry is evaluated here rather than persisted by a scheduled job. A row whose
    /// <c>expires_at</c> has passed is expired whether or not anything has run, so deriving it on
    /// read means the list can never disagree with what redemption would actually do.
    /// </remarks>
    internal static InvitationDto ToDto(Invitation invitation, DateTimeOffset now)
    {
        var status = invitation.Status == InvitationStatus.Pending && now >= invitation.ExpiresAt
            ? InvitationStatus.Expired
            : invitation.Status;

        return new InvitationDto(
            invitation.Id,
            invitation.Email,
            invitation.Role,
            status,
            invitation.InvitedById,
            invitation.ExpiresAt,
            invitation.CreatedAt);
    }
}

public sealed class InviteMemberHandler(
    ICadenceDbContext context,
    ICurrentUser currentUser,
    ITokenService tokens,
    IDateTime clock)
    : ICommandHandler<InviteMemberCommand, Result<InvitationDto>>
{
    /// <summary>How long an invitation stays redeemable, matching the client's mock layer.</summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public async ValueTask<Result<InvitationDto>> Handle(
        InviteMemberCommand command,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.RequireOrganizationId();
        var email = command.Invitation.Email.Trim().ToLowerInvariant();
        var now = clock.UtcNow;

        var alreadyMember = await context.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => user.Email == email)
            .Join(
                context.OrganizationMembers.IgnoreQueryFilters()
                    .Where(member => member.OrganizationId == organizationId),
                user => user.Id,
                member => member.UserId,
                (user, member) => member.Id)
            .AnyAsync(cancellationToken);

        if (alreadyMember)
        {
            return Result.Failure<InvitationDto>(Error.Conflict(
                "invitation.already_member",
                "That person is already a member of this workspace."));
        }

        var pending = await context.Invitations
            .FirstOrDefaultAsync(
                invitation => invitation.Email == email
                    && invitation.Status == InvitationStatus.Pending,
                cancellationToken);

        if (pending is not null)
        {
            // A still-live invitation is a conflict; an expired one is not, and re-inviting is the
            // natural thing to do once it lapses. The partial unique index only covers `pending`,
            // so the expired row is left in place as a record that it was sent.
            if (pending.IsRedeemable(now))
            {
                return Result.Failure<InvitationDto>(Error.Conflict(
                    "invitation.already_pending",
                    "An invitation is already pending for that address."));
            }

            pending.MarkExpired(now);
        }

        var secret = tokens.CreateOpaqueToken();

        var invitation = Invitation.Create(
            organizationId,
            email,
            command.Invitation.Role,
            currentUser.RequireId(),
            secret.Hash,
            now.Add(Lifetime),
            now);

        await context.Invitations.AddAsync(invitation, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // The plaintext token is dropped here on purpose. Delivery is email (§23.5, deferred), and
        // redemption matches the Google-verified address rather than the token, so nothing in the
        // API needs to see it. Returning it in the response would put a redeemable secret in the
        // browser of every admin who opens the team screen.
        return Result.Success(ListInvitationsHandler.ToDto(invitation, now));
    }
}

public sealed class ResendInvitationHandler(
    ICadenceDbContext context,
    ITokenService tokens,
    IDateTime clock)
    : ICommandHandler<ResendInvitationCommand, Result<InvitationDto>>
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public async ValueTask<Result<InvitationDto>> Handle(
        ResendInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var invitation = await context.Invitations
            .FirstOrDefaultAsync(candidate => candidate.Id == command.InvitationId, cancellationToken);

        if (invitation is null)
        {
            return Result.Failure<InvitationDto>(Error.NotFound(
                "invitation.not_found",
                "That invitation could not be found."));
        }

        var now = clock.UtcNow;
        var secret = tokens.CreateOpaqueToken();

        try
        {
            // A new token, not just a later expiry. Resending because the first link went astray
            // should invalidate the one that went astray.
            invitation.Reissue(secret.Hash, now.Add(Lifetime), now);
        }
        catch (DomainException exception)
        {
            return Result.Failure<InvitationDto>(Error.Conflict("invitation.invariant", exception.Message));
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ListInvitationsHandler.ToDto(invitation, now));
    }
}

public sealed class RevokeInvitationHandler(ICadenceDbContext context, IDateTime clock)
    : ICommandHandler<RevokeInvitationCommand, Result<InvitationDto>>
{
    public async ValueTask<Result<InvitationDto>> Handle(
        RevokeInvitationCommand command,
        CancellationToken cancellationToken)
    {
        var invitation = await context.Invitations
            .FirstOrDefaultAsync(candidate => candidate.Id == command.InvitationId, cancellationToken);

        if (invitation is null)
        {
            return Result.Failure<InvitationDto>(Error.NotFound(
                "invitation.not_found",
                "That invitation could not be found."));
        }

        try
        {
            invitation.Revoke();
        }
        catch (DomainException exception)
        {
            return Result.Failure<InvitationDto>(Error.Conflict("invitation.invariant", exception.Message));
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(ListInvitationsHandler.ToDto(invitation, clock.UtcNow));
    }
}
