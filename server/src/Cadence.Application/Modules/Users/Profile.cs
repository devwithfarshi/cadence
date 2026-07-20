using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Exceptions;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Users;

/// <summary>The signed-in user, with their role in the current workspace.</summary>
public sealed record GetCurrentUserQuery : IQuery<Result<UserDto>>;

/// <summary>Updates the editable parts of the signed-in user's profile.</summary>
public sealed record UpdateProfileCommand(UpdateProfileRequest Profile) : ICommand<Result<UserDto>>;

internal sealed class UpdateProfileValidator : AbstractValidator<UpdateProfileCommand>
{
    public UpdateProfileValidator()
    {
        RuleFor(command => command.Profile.Name)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200);

        RuleFor(command => command.Profile.JobTitle).MaximumLength(200);
        RuleFor(command => command.Profile.Department).MaximumLength(200);

        RuleFor(command => command.Profile.Timezone)
            .NotEmpty().WithMessage("Timezone is required.")
            .MaximumLength(64)
            // Rejected here rather than on read: a bad zone stored once produces a wrong time on
            // every screen thereafter, and nothing points back at where it came from.
            .Must(BeAKnownTimezone).WithMessage("That is not a recognised IANA timezone.");

        RuleFor(command => command.Profile.AvatarUrl)
            .MaximumLength(2048)
            .Must(BeAnAbsoluteHttpUrl).WithMessage("Avatar URL must be an absolute http(s) URL.")
            .When(command => !string.IsNullOrWhiteSpace(command.Profile.AvatarUrl));
    }

    private static bool BeAKnownTimezone(string timezone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _);

    private static bool BeAnAbsoluteHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && parsed.Scheme is "http" or "https";
}

public sealed class GetCurrentUserHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : IQueryHandler<GetCurrentUserQuery, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(
        GetCurrentUserQuery query,
        CancellationToken cancellationToken) =>
        await UserReads.LoadDtoAsync(context, currentUser, cancellationToken);
}

public sealed class UpdateProfileHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<UpdateProfileCommand, Result<UserDto>>
{
    public async ValueTask<Result<UserDto>> Handle(
        UpdateProfileCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();

        var user = await context.Users.FirstOrDefaultAsync(
            candidate => candidate.Id == userId,
            cancellationToken)
            ?? throw new NotFoundException("Your account could not be loaded.");

        user.UpdateProfile(
            command.Profile.Name,
            command.Profile.JobTitle,
            command.Profile.Department,
            command.Profile.Timezone);

        user.UpdateAvatar(command.Profile.AvatarUrl);

        await context.SaveChangesAsync(cancellationToken);

        return await UserReads.LoadDtoAsync(context, currentUser, cancellationToken);
    }
}

internal static class UserReads
{
    /// <summary>
    /// Loads the user together with their membership in the current workspace.
    /// </summary>
    /// <remarks>
    /// The membership is looked up for <i>this</i> organization rather than taking the first one:
    /// a person in three workspaces has three roles, and returning the wrong one would show them a
    /// UI granting permissions they do not have here.
    /// </remarks>
    public static async Task<Result<UserDto>> LoadDtoAsync(
        ICadenceDbContext context,
        ICurrentUser currentUser,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();
        var organizationId = currentUser.RequireOrganizationId();

        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<UserDto>(
                Error.NotFound("user.not_found", "Your account could not be loaded."));
        }

        var membership = await context.OrganizationMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                member => member.UserId == userId && member.OrganizationId == organizationId,
                cancellationToken);

        if (membership is null)
        {
            // The token names a workspace this person is no longer in — removed since it was issued.
            return Result.Failure<UserDto>(Error.Forbidden(
                "user.not_a_member",
                "You are no longer a member of this workspace."));
        }

        return Result.Success(user.ToDto(membership));
    }
}
