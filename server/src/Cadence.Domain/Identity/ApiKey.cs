using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// A programmatic credential for one organization.
/// </summary>
/// <remarks>
/// <para>
/// The client mock keeps the secret in plain text so the demo can re-reveal it. The server does not
/// and will not: only <see cref="KeyHash"/> is stored, the plaintext is returned exactly once from
/// the create endpoint, and a lost key is rotated rather than recovered. A readable key column is a
/// database leak that hands over every caller's credentials.
/// </para>
/// <para>
/// <see cref="Prefix"/> exists so the UI can identify a key in a list and so a leaked key found in
/// a log can be traced back to a row without knowing the secret.
/// </para>
/// </remarks>
public sealed class ApiKey : AggregateRoot
{
    private readonly List<ApiKeyScope> _scopes = [];

    private ApiKey()
    {
        Name = null!;
        Prefix = null!;
        KeyHash = null!;
    }

    private ApiKey(Guid organizationId, Guid createdById, string name, string prefix, string keyHash)
    {
        OrganizationId = organizationId;
        CreatedById = createdById;
        Name = name;
        Prefix = prefix;
        KeyHash = keyHash;
    }

    public Guid OrganizationId { get; private set; }

    public Guid CreatedById { get; private set; }

    public string Name { get; private set; }

    /// <summary>Non-secret leading segment, e.g. <c>cad_live_9f2c</c>.</summary>
    public string Prefix { get; private set; }

    /// <summary>SHA-256 of the full key. The key itself is never persisted.</summary>
    public string KeyHash { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public IReadOnlyCollection<ApiKeyScope> Scopes => _scopes.AsReadOnly();

    public bool IsActive => RevokedAt is null;

    public static ApiKey Issue(
        Guid organizationId,
        Guid createdById,
        string name,
        string prefix,
        string keyHash,
        IEnumerable<ApiKeyScope> scopes)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "An API key needs a name.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(keyHash), "An API key needs a hash.");

        var key = new ApiKey(organizationId, createdById, name.Trim(), prefix.Trim(), keyHash);
        key._scopes.AddRange(scopes.Distinct());

        DomainException.ThrowIf(key._scopes.Count == 0, "An API key needs at least one scope.");

        return key;
    }

    public bool HasScope(ApiKeyScope scope) => IsActive && _scopes.Contains(scope);

    /// <summary>
    /// Written on use so an unused key can be spotted and retired. Updated out of band rather than
    /// on the request path — see the write-behind note in blueprint §14.
    /// </summary>
    public void RecordUsage(DateTimeOffset usedAt) => LastUsedAt = usedAt;

    public void Revoke(DateTimeOffset revokedAt) => RevokedAt ??= revokedAt;
}
