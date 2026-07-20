using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Auth;

/// <summary>
/// Ends the current session by revoking its whole refresh-token family.
/// </summary>
/// <remarks>
/// The family rather than the one token: rotation means several tokens can belong to one session,
/// and revoking only the current one would leave its predecessors usable (§5.2).
/// </remarks>
public sealed record SignOutCommand(string? RefreshToken) : ICommand<Result>;

public sealed class SignOutHandler(ICadenceDbContext context, ITokenService tokens)
    : ICommandHandler<SignOutCommand, Result>
{
    public async ValueTask<Result> Handle(SignOutCommand command, CancellationToken cancellationToken)
    {
        // Signing out always succeeds. A caller with no cookie, an expired token or one already
        // revoked has achieved what they asked for, and returning an error would leave the client
        // showing a failure for an action that cannot meaningfully fail.
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Success();
        }

        var presentedHash = tokens.HashRefreshToken(command.RefreshToken);

        var familyId = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.TokenHash == presentedHash)
            .Select(token => (Guid?)token.FamilyId)
            .FirstOrDefaultAsync(cancellationToken);

        if (familyId is null)
        {
            return Result.Success();
        }

        var family = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.FamilyId == familyId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.Revoke();
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
