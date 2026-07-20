using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Organizations;

/// <summary>Settings for the workspace the caller's token names.</summary>
public sealed record GetWorkspaceSettingsQuery : IQuery<Result<WorkspaceSettingsDto>>;

/// <summary>Replaces the current workspace's settings.</summary>
public sealed record UpdateWorkspaceSettingsCommand(WorkspaceSettingsDto Settings)
    : ICommand<Result<WorkspaceSettingsDto>>;

internal sealed class UpdateWorkspaceSettingsValidator
    : AbstractValidator<UpdateWorkspaceSettingsCommand>
{
    public UpdateWorkspaceSettingsValidator()
    {
        RuleFor(command => command.Settings.Name)
            .NotEmpty().WithMessage("The workspace name cannot be empty.")
            .MaximumLength(200);

        // Enums are validated rather than trusted: an unknown value would otherwise reach the check
        // constraint and surface as a 500 instead of a 400 naming the field.
        RuleFor(command => command.Settings.DefaultVisibility).IsInEnum();
        RuleFor(command => command.Settings.Retention).IsInEnum();
    }
}

public sealed class GetWorkspaceSettingsHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : IQueryHandler<GetWorkspaceSettingsQuery, Result<WorkspaceSettingsDto>>
{
    public async ValueTask<Result<WorkspaceSettingsDto>> Handle(
        GetWorkspaceSettingsQuery query,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.RequireOrganizationId();

        var settings = await context.Organizations
            .AsNoTracking()
            .Where(organization => organization.Id == organizationId)
            .Select(organization => new WorkspaceSettingsDto(
                organization.Settings.Name,
                organization.Settings.DefaultVisibility,
                organization.Settings.Retention))
            .FirstOrDefaultAsync(cancellationToken);

        return settings is null
            ? Result.Failure<WorkspaceSettingsDto>(Error.NotFound(
                "organization.not_found",
                "That workspace could not be found."))
            : Result.Success(settings);
    }
}

public sealed class UpdateWorkspaceSettingsHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<UpdateWorkspaceSettingsCommand, Result<WorkspaceSettingsDto>>
{
    public async ValueTask<Result<WorkspaceSettingsDto>> Handle(
        UpdateWorkspaceSettingsCommand command,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.RequireOrganizationId();

        // The current workspace, so the ordinary tenant-free lookup on Organization plus the
        // caller's own claim is the scope. The role gate is the RequireAdmin policy on the endpoint,
        // which is sufficient here precisely because the id comes from the token and not the URL.
        var organization = await context.Organizations
            .FirstOrDefaultAsync(candidate => candidate.Id == organizationId, cancellationToken);

        if (organization is null)
        {
            return Result.Failure<WorkspaceSettingsDto>(Error.NotFound(
                "organization.not_found",
                "That workspace could not be found."));
        }

        organization.UpdateSettings(Domain.Identity.WorkspaceSettings.Create(
            command.Settings.Name,
            command.Settings.DefaultVisibility,
            command.Settings.Retention));

        // The display name follows the settings name. Two editable names for one workspace — one on
        // the row, one in its settings — read as a bug the first time they disagree.
        organization.Rename(command.Settings.Name);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new WorkspaceSettingsDto(
            organization.Settings.Name,
            organization.Settings.DefaultVisibility,
            organization.Settings.Retention));
    }
}
