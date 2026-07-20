using Cadence.Domain.Identity;

namespace Cadence.Application.Modules.Auth;

internal static class UserMapping
{
    /// <summary>
    /// Projects a user together with the membership that gives them a role.
    /// </summary>
    /// <remarks>
    /// The membership is a required argument rather than an optional one: role and organization are
    /// properties of <i>this person in this workspace</i>, not of the person. Defaulting them would
    /// let a caller ship a <see cref="UserDto"/> claiming a role nobody granted.
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
            user.Status,
            membership.Role,
            membership.OrganizationId,
            user.LastActiveAt);
}
