using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Organizations;

/// <summary>
/// Moves the calling session into another workspace it belongs to.
/// </summary>
/// <remarks>
/// A new access token is issued rather than an organization id being accepted per-request. The
/// <c>org</c> claim is signed; a caller-supplied id in a header or query string is an assertion, and
/// the tenant filter would happily honour it. This is the difference between multi-tenancy and a
/// suggestion.
/// </remarks>
public sealed record SwitchOrganizationCommand(Guid OrganizationId) : ICommand<Result<AuthResponse>>;

public sealed class SwitchOrganizationHandler(
    ICadenceDbContext context,
    ICurrentUser currentUser,
    ITokenService tokens)
    : ICommandHandler<SwitchOrganizationCommand, Result<AuthResponse>>
{
    public async ValueTask<Result<AuthResponse>> Handle(
        SwitchOrganizationCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await OrganizationAccess.RequireMembershipAsync(
            context,
            currentUser,
            command.OrganizationId,
            UserRole.Guest,
            cancellationToken);

        if (membership.IsFailure)
        {
            return Result.Failure<AuthResponse>(membership.Error);
        }

        var userId = currentUser.RequireId();

        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);

        if (user is null)
        {
            return Result.Failure<AuthResponse>(
                Error.NotFound("user.not_found", "Your account could not be loaded."));
        }

        var sessionId = currentUser.SessionId;

        if (sessionId is null)
        {
            // No `sid` means there is no session row to re-scope, so the new workspace would survive
            // exactly one access token and then silently revert at the next refresh. Better to
            // refuse than to appear to work for 15 minutes.
            return Result.Failure<AuthResponse>(Error.Unauthorized(
                "auth.no_session",
                "This token cannot switch workspace. Sign in again."));
        }

        await ReScopeSessionAsync(userId, sessionId.Value, command.OrganizationId, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // The refresh token itself is untouched — only what it points at changes — so no new cookie
        // is set and rotation carries on from where it was. Re-issuing it here would invalidate the
        // cookie the caller is holding for no gain.
        var access = tokens.CreateAccessToken(new AccessTokenRequest(
            user.Id,
            user.Email,
            user.Name,
            user.AvatarUrl,
            membership.Value.OrganizationId,
            membership.Value.Role,
            sessionId.Value));

        return Result.Success(new AuthResponse(
            access.Value,
            access.ExpiresInSeconds,
            user.ToDto(membership.Value)));
    }

    /// <summary>
    /// Points this session's live refresh tokens at the new workspace.
    /// </summary>
    /// <remarks>
    /// Scoped to the caller's own user id as well as the family, for the reason session revocation
    /// is: without it, a family id is all anyone would need to move somebody else's session into a
    /// workspace of their choosing.
    /// <para>
    /// Only this session moves. A different device stays where it was, which is the point of holding
    /// the workspace on the session rather than on the user.
    /// </para>
    /// </remarks>
    private async Task ReScopeSessionAsync(
        Guid userId,
        Guid sessionId,
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var family = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.UserId == userId
                && token.FamilyId == sessionId
                && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in family)
        {
            token.ScopeTo(organizationId);
        }
    }
}
