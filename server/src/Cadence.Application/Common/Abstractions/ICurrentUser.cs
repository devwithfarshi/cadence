using Cadence.Domain.Enums;

namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Who is making this request, and in which workspace.
/// </summary>
/// <remarks>
/// <para>
/// Handlers depend on this rather than on <c>HttpContext</c>, which is what keeps the Application
/// layer free of ASP.NET and lets a background job run the same handler with a system principal.
/// </para>
/// <para>
/// <see cref="OrganizationId"/> is the multi-tenancy anchor: the EF global query filter reads it,
/// so a handler that forgets to filter by tenant still cannot see another workspace's rows (§5.5).
/// </para>
/// </remarks>
public interface ICurrentUser
{
    /// <summary>Null for an unauthenticated request; endpoints that need it are already gated.</summary>
    Guid? Id { get; }

    /// <summary>The workspace this request is scoped to. Comes from the token, never the URL.</summary>
    Guid? OrganizationId { get; }

    string? Email { get; }

    UserRole? Role { get; }

    bool IsAuthenticated { get; }

    /// <summary>Convenience for handlers that are only reachable when authenticated.</summary>
    Guid RequireId();

    Guid RequireOrganizationId();

    /// <summary>True when the caller's role is at or above <paramref name="role"/>.</summary>
    bool IsAtLeast(UserRole role);
}
