using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// Membership of one user in one organization, carrying their role there.
/// </summary>
/// <remarks>
/// Role lives here rather than on <see cref="User"/> because the same person can be an owner of one
/// organization and a guest in another. This is the join that makes multi-tenancy real.
/// </remarks>
public sealed class OrganizationMember : Entity
{
    private OrganizationMember()
    {
    }

    private OrganizationMember(Guid organizationId, Guid userId, UserRole role)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        JoinedAt = DateTimeOffset.UtcNow;
    }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public UserRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    internal static OrganizationMember Create(Guid organizationId, Guid userId, UserRole role) =>
        new(organizationId, userId, role);

    internal void ChangeRole(UserRole role) => Role = role;
}
