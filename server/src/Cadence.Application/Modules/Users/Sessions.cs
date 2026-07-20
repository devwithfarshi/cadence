using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Users;

/// <summary>Active sign-ins for the current user.</summary>
public sealed record GetSessionsQuery : IQuery<Result<IReadOnlyList<SessionDto>>>;

/// <summary>Revokes one session by its family id.</summary>
public sealed record RevokeSessionCommand(Guid SessionId) : ICommand<Result>;

/// <summary>Signs out everywhere except the session making the request.</summary>
public sealed record RevokeOtherSessionsCommand : ICommand<Result>;

public sealed class GetSessionsHandler(
    ICadenceDbContext context,
    ICurrentUser currentUser,
    IDateTime clock)
    : IQueryHandler<GetSessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    public async ValueTask<Result<IReadOnlyList<SessionDto>>> Handle(
        GetSessionsQuery query,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();
        var currentSessionId = currentUser.SessionId;
        var now = clock.UtcNow;

        // A session is a family of rotated tokens, so the rows are grouped by family and collapsed
        // into one entry — otherwise a month-old session would appear as hundreds of "sign-ins".
        var tokens = await context.RefreshTokens
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(token => token.UserId == userId && token.RevokedAt == null && token.ExpiresAt > now)
            .ToListAsync(cancellationToken);

        var sessions = tokens
            .GroupBy(token => token.FamilyId)
            .Select(family => new SessionDto(
                family.Key,
                // The newest token in the family carries the most recent device and address, which
                // is what makes an unfamiliar session recognisable.
                family.MaxBy(token => token.LastUsedAt)!.Device,
                family.MaxBy(token => token.LastUsedAt)!.IpAddress,
                family.Min(token => token.CreatedAt),
                family.Max(token => token.LastUsedAt),
                family.Max(token => token.ExpiresAt),
                family.Key == currentSessionId))
            .OrderByDescending(session => session.LastUsedAt)
            .ToList();

        return Result.Success<IReadOnlyList<SessionDto>>(sessions);
    }
}

public sealed class RevokeSessionHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<RevokeSessionCommand, Result>
{
    public async ValueTask<Result> Handle(
        RevokeSessionCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();

        // Scoped to this user's own tokens. Without the UserId predicate, anyone holding a valid
        // token could sign out any session in the system by guessing a family id.
        var family = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.UserId == userId
                && token.FamilyId == command.SessionId
                && token.RevokedAt == null)
            .ToListAsync(cancellationToken);

        if (family.Count == 0)
        {
            // Already gone, or never theirs. Reported identically either way, so this cannot be used
            // to discover whether a family id exists.
            return Result.Failure(Error.NotFound("session.not_found", "That session no longer exists."));
        }

        foreach (var token in family)
        {
            token.Revoke();
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class RevokeOtherSessionsHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<RevokeOtherSessionsCommand, Result>
{
    public async ValueTask<Result> Handle(
        RevokeOtherSessionsCommand command,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();
        var currentSessionId = currentUser.SessionId;

        var others = await context.RefreshTokens
            .IgnoreQueryFilters()
            .Where(token => token.UserId == userId
                && token.RevokedAt == null
                && token.FamilyId != currentSessionId)
            .ToListAsync(cancellationToken);

        foreach (var token in others)
        {
            token.Revoke();
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
