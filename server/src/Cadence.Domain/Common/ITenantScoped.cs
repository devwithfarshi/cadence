namespace Cadence.Domain.Common;

/// <summary>
/// Marks an entity that belongs to exactly one organization.
/// </summary>
/// <remarks>
/// <para>
/// This interface is what the tenant global query filter binds to. <c>CadenceDbContext</c> applies
/// the filter to every implementor by walking the model, so adding a tenant-scoped entity opts it in
/// automatically — there is no per-entity registration anyone can forget.
/// </para>
/// <para>
/// <b>A missing tenant filter is a cross-tenant data leak</b> (blueprint §3.3), which is why this is
/// a marker on the entity rather than a predicate each query has to remember to add.
/// </para>
/// <para>
/// <c>User</c> and <c>Organization</c> deliberately do <b>not</b> implement it: one person may belong
/// to several organizations, so identity is global and membership lives on
/// <c>OrganizationMember</c>.
/// </para>
/// </remarks>
public interface ITenantScoped
{
    Guid OrganizationId { get; }
}
