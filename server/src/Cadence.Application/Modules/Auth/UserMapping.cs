using Cadence.Domain.Identity;

namespace Cadence.Application.Modules.Auth;

internal static class UserMapping
{
    /// <summary>
    /// Projects a user together with the membership that gives them a role.
    /// </summary>
    /// <remarks>
    /// The membership is a required argument rather than an optional one: role, status and
    /// organization are properties of <i>this person in this workspace</i>, not of the person.
    /// Defaulting them would let a caller ship a <see cref="UserDto"/> claiming a role nobody
    /// granted.
    /// </remarks>
    public static UserDto ToDto(this User user, OrganizationMember membership) =>
        new(
            user.Id,
            user.Email,
            user.Name,
            user.AvatarUrl,
            user.JobTitle,
            user.Department,
            user.Timezone,
            // The membership's standing, not User.Status. The latter is a platform-level flag; what
            // a workspace screen means by "suspended" is suspended *here*.
            membership.Status,
            membership.Role,
            membership.OrganizationId,
            user.LastActiveAt);
}
