using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Organizations;

/// <summary>
/// Every workspace the caller belongs to, for the switcher.
/// </summary>
/// <remarks>
/// One of the few reads that deliberately reaches outside the current tenant, so it is one of the
/// few that calls <c>IgnoreQueryFilters()</c>. The filter it drops is replaced by an explicit
/// <c>UserId</c> predicate — the caller sees the workspaces <i>they</i> are in, never a list of
/// workspaces that exist.
/// </remarks>
public sealed record ListOrganizationsQuery : IQuery<Result<IReadOnlyList<OrganizationDto>>>;

public sealed class ListOrganizationsHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : IQueryHandler<ListOrganizationsQuery, Result<IReadOnlyList<OrganizationDto>>>
{
    public async ValueTask<Result<IReadOnlyList<OrganizationDto>>> Handle(
        ListOrganizationsQuery query,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();
        var currentOrganizationId = currentUser.RequireOrganizationId();

        var memberships = await context.OrganizationMembers
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(member => member.UserId == userId && member.Status != UserStatus.Suspended)
            .Select(member => new { member.OrganizationId, member.Role })
            .ToListAsync(cancellationToken);

        var organizationIds = memberships.Select(membership => membership.OrganizationId).ToList();

        // Not IgnoreQueryFilters: Organization is soft-deletable but not tenant-scoped, so the only
        // filter on it is `deleted_at IS NULL` — which is exactly the one to keep.
        var organizations = await context.Organizations
            .AsNoTracking()
            .Where(organization => organizationIds.Contains(organization.Id))
            .Select(organization => new
            {
                organization.Id,
                organization.Name,
                organization.Slug,
                organization.Plan,
                organization.OwnerId,
                organization.CreatedAt,
                // Counted in the database rather than by loading members. The switcher needs a
                // number, and fetching every membership row of every workspace to call .Count on it
                // is the N+1 this projection exists to avoid.
                MemberCount = organization.Members.Count(member => member.Status != UserStatus.Suspended),
            })
            .ToListAsync(cancellationToken);

        var roles = memberships.ToDictionary(
            membership => membership.OrganizationId,
            membership => membership.Role);

        var results = organizations
            .Select(organization => new OrganizationDto(
                organization.Id,
                organization.Name,
                organization.Slug,
                organization.Plan,
                organization.OwnerId,
                organization.MemberCount,
                organization.Id == currentOrganizationId,
                roles[organization.Id],
                organization.CreatedAt))
            // Current first, then alphabetical — the order the switcher reads best in, and the same
            // order the client's mock layer produced.
            .OrderByDescending(organization => organization.IsCurrent)
            .ThenBy(organization => organization.Name)
            .ToList();

        return Result.Success<IReadOnlyList<OrganizationDto>>(results);
    }
}
