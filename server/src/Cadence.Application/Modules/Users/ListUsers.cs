using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Users;

/// <summary>
/// The member directory for the current workspace.
/// </summary>
/// <remarks>
/// Everyone in the workspace, not every user in the system. The join through
/// <c>organization_member</c> is what scopes it — <c>User</c> itself is global, so it carries no
/// tenant filter of its own and a query against it alone would return the entire user table (§3.3).
/// </remarks>
public sealed record ListUsersQuery(string? Search, UserRole? Role, UserStatus? Status)
    : IQuery<Result<IReadOnlyList<UserDto>>>;

public sealed class ListUsersHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : IQueryHandler<ListUsersQuery, Result<IReadOnlyList<UserDto>>>
{
    public async ValueTask<Result<IReadOnlyList<UserDto>>> Handle(
        ListUsersQuery query,
        CancellationToken cancellationToken)
    {
        var organizationId = currentUser.RequireOrganizationId();

        var members = context.OrganizationMembers
            .AsNoTracking()
            .Where(member => member.OrganizationId == organizationId);

        if (query.Role is { } role)
        {
            members = members.Where(member => member.Role == role);
        }

        var joined = from member in members
                     join user in context.Users.AsNoTracking() on member.UserId equals user.Id
                     select new { member, user };

        if (query.Status is { } status)
        {
            joined = joined.Where(row => row.user.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // `lower(x) LIKE '%term%'` rather than Npgsql's ILike: Application is provider-neutral,
            // and an architecture test fails the build if Npgsql appears here. Nothing is lost —
            // a leading wildcard cannot use a btree index either way, and real search is the GIN
            // tsvector index in §3.6.
            var term = query.Search.Trim().ToLowerInvariant();

            joined = joined.Where(row =>
                row.user.Name.ToLower().Contains(term)
                || row.user.Email.ToLower().Contains(term)
                || row.user.JobTitle.ToLower().Contains(term)
                || row.user.Department.ToLower().Contains(term));
        }

        var rows = await joined
            .OrderBy(row => row.user.Name)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<UserDto>>(
            [.. rows.Select(row => row.user.ToDto(row.member))]);
    }
}
