using System.Security.Claims;
using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Enums;

namespace Cadence.Api.Common;

/// <summary>
/// Reads the caller's identity from the validated access token.
/// </summary>
/// <remarks>
/// <para>
/// Everything here comes from claims — never from a route parameter, a header or a query string. An
/// <c>organizationId</c> the caller supplies is an assertion; the one in the token was signed by us
/// (§5.5). Trusting the former is how a tenant boundary is crossed by editing a URL.
/// </para>
/// <para>
/// Registered scoped, so one request sees one identity for its whole lifetime.
/// </para>
/// </remarks>
public sealed class CurrentUser(IHttpContextAccessor accessor, ScopedPrincipal scoped) : ICurrentUser
{
    /// <summary>
    /// The HTTP request's principal, or the one a hub invocation staged.
    /// </summary>
    /// <remarks>
    /// <c>HttpContext</c> first, because that is the ordinary case and it is the authoritative one
    /// while a request is in flight. <see cref="ScopedPrincipal"/> covers SignalR, where there is no
    /// live <c>HttpContext</c> — see the note on that type for why the fallback is not optional.
    /// </remarks>
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User ?? scoped.Principal;

    public Guid? Id => ReadGuid(ClaimTypes.NameIdentifier) ?? ReadGuid("sub");

    public Guid? OrganizationId => ReadGuid(CadenceClaims.OrganizationId);

    public string? Email => Principal?.FindFirstValue(ClaimTypes.Email);

    public Guid? SessionId => ReadGuid(CadenceClaims.SessionId);

    public UserRole? Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(CadenceClaims.Role), ignoreCase: true, out var role)
            ? role
            : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public Guid RequireId() =>
        Id ?? throw new UnauthorizedAccessException("This action requires a signed-in user.");

    public Guid RequireOrganizationId() =>
        OrganizationId
        ?? throw new UnauthorizedAccessException("This action requires an active workspace.");

    /// <summary>
    /// Role comparison by rank, so a check reads "at least Admin" rather than enumerating every
    /// role above it and going stale the moment one is added.
    /// </summary>
    public bool IsAtLeast(UserRole role) => Role is { } actual && Rank(actual) >= Rank(role);

    private static int Rank(UserRole role) => role switch
    {
        UserRole.Owner => 4,
        UserRole.Admin => 3,
        UserRole.Member => 2,
        _ => 1,
    };

    private Guid? ReadGuid(string claimType) =>
        Guid.TryParse(Principal?.FindFirstValue(claimType), out var value) ? value : null;
}
