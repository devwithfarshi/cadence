using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Auth;

/// <summary>
/// Exchanges a refresh token for a new access token, rotating the refresh token.
/// </summary>
/// <remarks>
/// Rotation with reuse detection (§4.3): every refresh invalidates the presented token. Presenting
/// one that has <i>already</i> been rotated is evidence of theft — the legitimate client would be
/// holding its successor — so the whole family is revoked and everyone signs in again.
/// </remarks>
public sealed record RefreshSessionCommand(string RefreshToken, SessionContext Session)
    : ICommand<Result<AuthResult>>;

public sealed class RefreshSessionHandler(
    ICadenceDbContext context,
    ITokenService tokens,
    IDateTime clock,
    ILogger<RefreshSessionHandler> logger)
    : ICommandHandler<RefreshSessionCommand, Result<AuthResult>>
{
    // One message for every failure mode. Telling a caller whether a token was unknown, expired or
    // reused hands them a probe for which stolen tokens are still worth trying.
    private static readonly Error Rejected =
        Error.Unauthorized("auth.invalid_refresh", "Your session has expired. Please sign in again.");

    public async ValueTask<Result<AuthResult>> Handle(
        RefreshSessionCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure<AuthResult>(Rejected);
        }

        // Looked up by hash: the plaintext was never stored, so a database leak yields no usable
        // session.
        var presentedHash = tokens.HashRefreshToken(command.RefreshToken);

        var stored = await context.RefreshTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(token => token.TokenHash == presentedHash, cancellationToken);

        if (stored is null)
        {
            return Result.Failure<AuthResult>(Rejected);
        }

        if (stored.IsRotated)
        {
            await RevokeFamilyAsync(stored, cancellationToken);
            return Result.Failure<AuthResult>(Rejected);
        }

        if (!stored.IsActive)
        {
            return Result.Failure<AuthResult>(Rejected);
        }

        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == stored.UserId, cancellationToken);

        if (user is null || user.Status == UserStatus.Suspended)
        {
            // A suspension has to take effect at the next refresh, or a suspended user keeps working
            // for as long as they hold a valid refresh token.
            stored.Revoke();
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthResult>(Rejected);
        }

        var membership = await context.OrganizationMembers
            .IgnoreQueryFilters()
            .Where(member => member.UserId == user.Id)
            .OrderBy(member => member.JoinedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            // Removed from every workspace since the token was issued.
            stored.Revoke();
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure<AuthResult>(Rejected);
        }

        var result = await RotateAsync(stored, user, membership, command.Session, cancellationToken);

        user.Touch();
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(result);
    }

    /// <summary>
    /// Revokes every token in a family after a reuse is detected.
    /// </summary>
    /// <remarks>
    /// Revoking only the reused token would leave the thief's successor working. Since we cannot
    /// tell which party is the attacker, the safe move is to end the session for both and make them
    /// sign in again — an inconvenience against a live account takeover.
    /// </remarks>
    private async Task RevokeFamilyAsync(RefreshToken reused, CancellationToken cancellationToken)
    {
        var family = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.FamilyId == reused.FamilyId && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.Revoke();
        }

        // Warning, not information: this is the signal that a refresh token leaked.
        logger.LogWarning(
            "Refresh token reuse detected for user {UserId}; revoked {Count} tokens in family {FamilyId}",
            reused.UserId,
            family.Count,
            reused.FamilyId);

        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<AuthResult> RotateAsync(
        RefreshToken current,
        User user,
        OrganizationMember membership,
        SessionContext sessionContext,
        CancellationToken cancellationToken)
    {
        var refresh = tokens.CreateRefreshToken();

        var successor = RefreshToken.Issue(
            user.Id,
            refresh.Hash,
            refresh.ExpiresAt - clock.UtcNow,
            // Same family: this is the same session continuing, which is what makes reuse
            // detectable and what keeps the session's identity stable in the sessions list.
            familyId: current.FamilyId,
            device: sessionContext.Device ?? current.Device,
            ipAddress: sessionContext.IpAddress ?? current.IpAddress);

        await context.RefreshTokens.AddAsync(successor, cancellationToken);
        current.RotateTo(successor);

        // Role and organization are re-read here, so a role change takes effect at the next refresh
        // — the bounded staleness that stateless authorization trades for (§4.4).
        var access = tokens.CreateAccessToken(new AccessTokenRequest(
            user.Id,
            user.Email,
            user.Name,
            user.AvatarUrl,
            membership.OrganizationId,
            membership.Role,
            successor.FamilyId));

        return new AuthResult(
            new AuthResponse(access.Value, access.ExpiresInSeconds, user.ToDto(membership)),
            refresh.Value,
            refresh.ExpiresAt);
    }
}
