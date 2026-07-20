using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// Membership of one user in one organization, carrying their role and standing there.
/// </summary>
/// <remarks>
/// Role lives here rather than on <see cref="User"/> because the same person can be an owner of one
/// organization and a guest in another. This is the join that makes multi-tenancy real.
/// </remarks>
public sealed class OrganizationMember : Entity, ITenantScoped
{
    private OrganizationMember()
    {
    }

    private OrganizationMember(Guid organizationId, Guid userId, UserRole role)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Role = role;
        Status = UserStatus.Active;
        JoinedAt = DateTimeOffset.UtcNow;
    }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public UserRole Role { get; private set; }

    /// <summary>
    /// This person's standing <i>in this workspace</i>.
    /// </summary>
    /// <remarks>
    /// Deliberately not <see cref="User.Status"/>, which is global. A workspace admin suspending a
    /// member must not lock that person out of a different workspace they also belong to — otherwise
    /// anyone able to create a free workspace and get an invitation accepted could disable a
    /// colleague's real account. The workspace-level lever stops at the workspace boundary;
    /// <see cref="User.Status"/> stays a platform-level flag no tenant can set.
    /// </remarks>
    public UserStatus Status { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    /// <summary>True when this membership currently grants access to the workspace.</summary>
    public bool IsActive => Status != UserStatus.Suspended;

    internal static OrganizationMember Create(Guid organizationId, Guid userId, UserRole role) =>
        new(organizationId, userId, role);

    internal void ChangeRole(UserRole role) => Role = role;

    internal void ChangeStatus(UserStatus status) => Status = status;
}
