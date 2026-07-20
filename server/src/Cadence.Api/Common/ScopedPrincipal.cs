using System.Security.Claims;
using Cadence.Application.Common.Abstractions;

namespace Cadence.Api.Common;

/// <summary>
/// Carries the caller's identity for work that is not an HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CurrentUser"/> normally reads the principal off <c>HttpContext</c>. A SignalR hub
/// invocation has no live <c>HttpContext</c> — <c>IHttpContextAccessor</c> returns the connection's
/// original negotiate request at best, and null once the connection is a websocket. The failure is
/// silent and severe: <c>ICurrentUser.OrganizationId</c> comes back null, the tenant filter falls
/// back to <see cref="Guid.Empty"/>, and every query inside a hub matches nothing. Nothing throws;
/// the hub just never finds the meeting it was asked about.
/// </para>
/// <para>
/// So hub invocations put the principal here instead, and <see cref="CurrentUser"/> falls back to
/// it. Scoped, because SignalR creates a DI scope per invocation — the same lifetime the DbContext
/// and <c>ICurrentUser</c> already have, so one invocation cannot see another's identity.
/// </para>
/// </remarks>
public sealed class ScopedPrincipal
{
    public ClaimsPrincipal? Principal { get; set; }

    /// <summary>
    /// A principal for background work that acts inside one workspace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Background work has no caller, so it has no principal — and without one the tenant filter
    /// falls back to <see cref="Guid.Empty"/> and the job silently sees an empty database. The
    /// tempting fix is <c>IgnoreQueryFilters()</c> in the job, which trades a silent no-op for a
    /// job that can touch every tenant.
    /// </para>
    /// <para>
    /// This is the narrower option: a principal scoped to exactly one workspace, so a job stays
    /// inside the tenant it is working for and the global filter keeps doing its job. The caller
    /// supplies an <paramref name="organizationId"/> it has <b>already</b> established the real user
    /// could see — it is not a way to pick a tenant, it is a way to carry one forward.
    /// </para>
    /// <para>
    /// It deliberately carries no <c>role</c> claim: nothing here should satisfy an authorization
    /// policy. Background work runs handlers directly, past the endpoint gates.
    /// </para>
    /// </remarks>
    public static ClaimsPrincipal ForOrganization(Guid organizationId) =>
        new(new ClaimsIdentity(
            claims: [new Claim(CadenceClaims.OrganizationId, organizationId.ToString())],
            authenticationType: "System",
            nameType: ClaimTypes.NameIdentifier,
            roleType: CadenceClaims.Role));
}
