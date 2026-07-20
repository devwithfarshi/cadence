using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Organizations;

/// <summary>Creates a workspace, with the caller as its owner.</summary>
public sealed record CreateOrganizationCommand(CreateOrganizationRequest Organization)
    : ICommand<Result<OrganizationDto>>;

/// <summary>Renames a workspace. The slug is not re-derived — see the handler.</summary>
public sealed record RenameOrganizationCommand(Guid OrganizationId, RenameOrganizationRequest Rename)
    : ICommand<Result<OrganizationDto>>;

/// <summary>Soft-deletes a workspace the caller owns.</summary>
public sealed record DeleteOrganizationCommand(Guid OrganizationId) : ICommand<Result>;

internal sealed class CreateOrganizationValidator : AbstractValidator<CreateOrganizationCommand>
{
    public CreateOrganizationValidator() =>
        RuleFor(command => command.Organization.Name)
            .NotEmpty().WithMessage("Give the workspace a name.")
            .MaximumLength(200);
}

internal sealed class RenameOrganizationValidator : AbstractValidator<RenameOrganizationCommand>
{
    public RenameOrganizationValidator() =>
        RuleFor(command => command.Rename.Name)
            .NotEmpty().WithMessage("The name cannot be empty.")
            .MaximumLength(200);
}

public sealed class CreateOrganizationHandler(
    ICadenceDbContext context,
    ICurrentUser currentUser,
    ILogger<CreateOrganizationHandler> logger)
    : ICommandHandler<CreateOrganizationCommand, Result<OrganizationDto>>
{
    public async ValueTask<Result<OrganizationDto>> Handle(
        CreateOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();

        var organization = Organization.Create(command.Organization.Name, userId);

        // Checked before the insert so the caller gets "that name is taken" rather than a 500 from
        // the unique index. The index stays the real guarantee — this check races, that one cannot.
        var slugTaken = await context.Organizations
            .AsNoTracking()
            .AnyAsync(candidate => candidate.Slug == organization.Slug, cancellationToken);

        if (slugTaken)
        {
            return Result.Failure<OrganizationDto>(Error.Conflict(
                "organization.slug_taken",
                "A workspace with that name already exists."));
        }

        await context.Organizations.AddAsync(organization, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "User {UserId} created workspace {OrganizationId}",
            userId,
            organization.Id);

        var membership = organization.Members.Single();

        // Not current: creating a workspace does not move the caller into it, matching the client's
        // behaviour. Switching is an explicit act, so a stray click cannot relocate someone
        // mid-task.
        return Result.Success(new OrganizationDto(
            organization.Id,
            organization.Name,
            organization.Slug,
            organization.Plan,
            organization.OwnerId,
            MemberCount: 1,
            IsCurrent: false,
            membership.Role,
            organization.CreatedAt));
    }
}

public sealed class RenameOrganizationHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<RenameOrganizationCommand, Result<OrganizationDto>>
{
    public async ValueTask<Result<OrganizationDto>> Handle(
        RenameOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var loaded = await OrganizationAccess.RequireOrganizationAsync(
            context,
            currentUser,
            command.OrganizationId,
            UserRole.Admin,
            cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure<OrganizationDto>(loaded.Error);
        }

        var organization = loaded.Value;

        // The slug deliberately does not follow the name. It is the workspace's stable identifier;
        // re-deriving it on every rename would break any link or bookmark holding the old one, and
        // silently free the old slug for someone else to claim.
        organization.Rename(command.Rename.Name);

        await context.SaveChangesAsync(cancellationToken);

        var membership = organization.Members.Single(member => member.UserId == currentUser.RequireId());

        return Result.Success(new OrganizationDto(
            organization.Id,
            organization.Name,
            organization.Slug,
            organization.Plan,
            organization.OwnerId,
            organization.Members.Count(member => member.Status != UserStatus.Suspended),
            organization.Id == currentUser.RequireOrganizationId(),
            membership.Role,
            organization.CreatedAt));
    }
}

public sealed class DeleteOrganizationHandler(
    ICadenceDbContext context,
    ICurrentUser currentUser,
    ILogger<DeleteOrganizationHandler> logger)
    : ICommandHandler<DeleteOrganizationCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        // Refused before anything else. Deleting the workspace you are standing in would leave your
        // own token pointing at a tenant with no rows — an empty application with no explanation of
        // why. The client requires a switch first for the same reason.
        if (command.OrganizationId == currentUser.RequireOrganizationId())
        {
            return Result.Failure(Error.Conflict(
                "organization.is_current",
                "Switch to another workspace before deleting this one."));
        }

        var loaded = await OrganizationAccess.RequireOrganizationAsync(
            context,
            currentUser,
            command.OrganizationId,
            UserRole.Owner,
            cancellationToken);

        if (loaded.IsFailure)
        {
            return Result.Failure(loaded.Error);
        }

        var organization = loaded.Value;

        // A soft delete, applied by the auditing interceptor. The rows stay, so the workspace's
        // meetings and the work attached to them remain recoverable (§3.7).
        context.Organizations.Remove(organization);

        await RevokeSessionsAsync(organization.Id, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "User {UserId} deleted workspace {OrganizationId}",
            currentUser.RequireId(),
            organization.Id);

        return Result.Success();
    }

    /// <summary>
    /// Ends every session scoped to the deleted workspace.
    /// </summary>
    /// <remarks>
    /// Soft delete means the cascade on <c>refresh_token.organization_id</c> never fires — no row is
    /// actually deleted. Other members left holding a session pointed here would keep refreshing
    /// into a workspace whose every query now returns nothing. Revoking sends them back through
    /// sign-in, which resolves them to a workspace that still exists.
    /// </remarks>
    private async Task RevokeSessionsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var sessions = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.OrganizationId == organizationId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var session in sessions)
        {
            session.Revoke();
        }
    }
}
