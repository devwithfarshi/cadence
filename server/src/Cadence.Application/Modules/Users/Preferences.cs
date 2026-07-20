using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Identity;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Users;

public sealed record GetPreferencesQuery : IQuery<Result<PreferencesDto>>;

public sealed record UpdatePreferencesCommand(PreferencesDto Preferences)
    : ICommand<Result<PreferencesDto>>;

internal sealed class UpdatePreferencesValidator : AbstractValidator<UpdatePreferencesCommand>
{
    public UpdatePreferencesValidator()
    {
        RuleFor(command => command.Preferences.Language)
            .NotEmpty()
            .MaximumLength(10);

        RuleFor(command => command.Preferences.Ai.OutputLanguage)
            .NotEmpty()
            .MaximumLength(10);

        RuleFor(command => command.Preferences.Notifications).NotNull();
        RuleFor(command => command.Preferences.Ai).NotNull();
    }
}

public sealed class GetPreferencesHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : IQueryHandler<GetPreferencesQuery, Result<PreferencesDto>>
{
    public async ValueTask<Result<PreferencesDto>> Handle(
        GetPreferencesQuery query,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();

        var preferences = await context.UserPreferences
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        // Provisioning creates a row on first sign-in, so this is the account-predates-preferences
        // case. Defaults are returned rather than a 404: the settings screen should render.
        return Result.Success((preferences ?? UserPreferences.CreateDefault(userId)).ToDto());
    }
}

public sealed class UpdatePreferencesHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<UpdatePreferencesCommand, Result<PreferencesDto>>
{
    public async ValueTask<Result<PreferencesDto>> Handle(
        UpdatePreferencesCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();

        var preferences = await context.UserPreferences
            .FirstOrDefaultAsync(candidate => candidate.UserId == userId, cancellationToken);

        if (preferences is null)
        {
            preferences = UserPreferences.CreateDefault(userId);
            await context.UserPreferences.AddAsync(preferences, cancellationToken);
        }

        preferences.Apply(command.Preferences);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(preferences.ToDto());
    }
}
