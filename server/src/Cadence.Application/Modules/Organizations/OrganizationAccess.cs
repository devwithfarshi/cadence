using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Organizations;

/// <summary>
/// Resolves the caller's standing in a workspace named by the URL.
/// </summary>
/// <remarks>
/// <b>The role policies are not enough on their own here.</b> <c>RequireAdmin</c> reads the
/// <c>role</c> claim, which describes the caller in their <i>current</i> workspace — so an owner of
/// their own personal workspace satisfies <c>RequireOwner</c> on a request naming somebody else's
/// organization. The policy is the coarse gate; this is the resource check §4.5 says has to happen
/// against the loaded aggregate.
/// <para>
/// Every failure is reported as "not found", never "forbidden". Telling a caller that a workspace
/// exists but is not theirs is a membership oracle — enough to enumerate which organizations exist
/// by id, and to confirm that a particular company is a customer.
/// </para>
/// </remarks>
internal static class OrganizationAccess
{
    private static readonly Error Unknown =
        Error.NotFound("organization.not_found", "That workspace could not be found.");

    /// <summary>
    /// Loads the caller's membership in <paramref name="organizationId"/>, requiring at least
    /// <paramref name="minimumRole"/>.
    /// </summary>
    public static async Task<Result<OrganizationMember>> RequireMembershipAsync(
        ICadenceDbContext context,
        ICurrentUser currentUser,
        Guid organizationId,
        UserRole minimumRole,
        CancellationToken cancellationToken)
    {
        var userId = currentUser.RequireId();

        // IgnoreQueryFilters because the target workspace is by definition not the current tenant on
        // a switch or a cross-workspace rename. The UserId predicate is what keeps it safe.
        var membership = await context.OrganizationMembers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                member => member.UserId == userId && member.OrganizationId == organizationId,
                cancellationToken);

        if (membership is null || !membership.IsActive)
        {
            return Result.Failure<OrganizationMember>(Unknown);
        }

        // Roles are declared most-privileged first, so "at least admin" is an ordinal comparison.
        if (membership.Role > minimumRole)
        {
            return Result.Failure<OrganizationMember>(Error.Forbidden(
                "organization.insufficient_role",
                "You do not have permission to do that in this workspace."));
        }

        return Result.Success(membership);
    }

    /// <summary>Loads a workspace the caller may act on, tracked for mutation.</summary>
    public static async Task<Result<Organization>> RequireOrganizationAsync(
        ICadenceDbContext context,
        ICurrentUser currentUser,
        Guid organizationId,
        UserRole minimumRole,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(
            context,
            currentUser,
            organizationId,
            minimumRole,
            cancellationToken);

        if (membership.IsFailure)
        {
            return Result.Failure<Organization>(membership.Error);
        }

        // Members are included because the aggregate's invariants — last-owner protection above all
        // — are evaluated against the loaded collection. Loading the root alone would let
        // ChangeMemberRole see an empty member list and conclude there are no owners to protect.
        //
        // IgnoreQueryFilters is required and subtle: a global filter applies to Include'd
        // navigations too, so loading workspace B while the token says workspace A would attach an
        // *empty* member collection rather than failing. The invariants would then be evaluated
        // against nothing and quietly permit demoting the last owner.
        //
        // Dropping the filters also drops soft delete, so `deleted_at IS NULL` is restated by hand.
        // The tenant predicate it replaces is the caller's own membership, already proven above.
        var organization = await context.Organizations
            .IgnoreQueryFilters()
            .Include(candidate => candidate.Members)
            .FirstOrDefaultAsync(
                candidate => candidate.Id == organizationId && candidate.DeletedAt == null,
                cancellationToken);

        return organization is null
            ? Result.Failure<Organization>(Unknown)
            : Result.Success(organization);
    }
}
