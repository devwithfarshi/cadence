using Cadence.Domain.Common;

namespace Cadence.Domain.Identity;

/// <summary>
/// One session. Rotated on every use, with reuse detection (blueprint §4.3).
/// </summary>
/// <remarks>
/// The token is stored <b>hashed</b>, for the same reason passwords are: a database leak must not
/// hand an attacker a set of live sessions.
/// <para>
/// Rotation works through <see cref="ReplacedByTokenId"/>. Presenting a token that has already
/// been rotated is evidence of theft — the legitimate client would be holding its successor — so
/// the whole <see cref="FamilyId"/> is revoked rather than just that one token.
/// </para>
/// </remarks>
public sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
        TokenHash = null!;
    }

    private RefreshToken(
        Guid userId,
        Guid organizationId,
        string tokenHash,
        Guid familyId,
        DateTimeOffset expiresAt,
        string? device,
        string? ipAddress)
    {
        UserId = userId;
        OrganizationId = organizationId;
        TokenHash = tokenHash;
        FamilyId = familyId;
        ExpiresAt = expiresAt;
        Device = device;
        IpAddress = ipAddress;
        CreatedAt = DateTimeOffset.UtcNow;
        LastUsedAt = CreatedAt;
    }

    public Guid UserId { get; private set; }

    /// <summary>
    /// The workspace this session is currently scoped to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The session remembers its workspace, so a refresh 15 minutes after switching re-issues the
    /// workspace the user chose rather than resolving one again from their membership list. Without
    /// it a switch would quietly undo itself at the next rotation and nothing would report why.
    /// </para>
    /// <para>
    /// It sits on the session rather than on the user because two devices may legitimately sit in
    /// two different workspaces at once — a laptop in the company account and a phone in a personal
    /// one. A single "last active organization" on <see cref="User"/> would have them fight over it.
    /// </para>
    /// <para>
    /// Note this entity is deliberately <b>not</b> <c>ITenantScoped</c>: refresh runs before any
    /// workspace is known, so a tenant query filter would hide the very row it needs to find.
    /// </para>
    /// </remarks>
    public Guid OrganizationId { get; private set; }

    /// <summary>SHA-256 of the token. The plaintext exists only in the client's cookie.</summary>
    public string TokenHash { get; private set; }

    /// <summary>Groups a token with its rotation ancestors, so reuse can revoke the whole chain.</summary>
    public Guid FamilyId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset LastUsedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    /// <summary>Shown in the sessions list so a user can recognise and revoke a device.</summary>
    public string? Device { get; private set; }

    public string? IpAddress { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAt;

    /// <summary>A token already swapped for a successor. Presenting one again means it leaked.</summary>
    public bool IsRotated => ReplacedByTokenId is not null;

    public bool IsActive => !IsRevoked && !IsExpired && !IsRotated;

    public static RefreshToken Issue(
        Guid userId,
        Guid organizationId,
        string tokenHash,
        TimeSpan lifetime,
        Guid? familyId = null,
        string? device = null,
        string? ipAddress = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(tokenHash), "Token hash is required.");
        DomainException.ThrowIf(
            organizationId == Guid.Empty,
            "A session must be scoped to a workspace.");

        return new RefreshToken(
            userId,
            organizationId,
            tokenHash,
            // A brand-new sign-in starts a new family; a rotation continues the existing one.
            familyId ?? Guid.CreateVersion7(),
            DateTimeOffset.UtcNow.Add(lifetime),
            device,
            ipAddress);
    }

    /// <summary>
    /// Re-points this session at another workspace, as an organization switch does.
    /// </summary>
    /// <remarks>
    /// The token is not rotated: nothing about the credential has changed, only which workspace it
    /// resolves to. Rotating would invalidate the cookie the client currently holds and buy nothing.
    /// </remarks>
    public void ScopeTo(Guid organizationId)
    {
        DomainException.ThrowIf(
            organizationId == Guid.Empty,
            "A session must be scoped to a workspace.");
        DomainException.ThrowIf(!IsActive, "Only an active session can change workspace.");

        OrganizationId = organizationId;
        LastUsedAt = DateTimeOffset.UtcNow;
    }

    public void RotateTo(RefreshToken successor)
    {
        DomainException.ThrowIf(!IsActive, "Only an active token can be rotated.");

        ReplacedByTokenId = successor.Id;
        LastUsedAt = DateTimeOffset.UtcNow;
    }

    public void Revoke()
    {
        // Idempotent: revoking an already-revoked token during family revocation is normal.
        RevokedAt ??= DateTimeOffset.UtcNow;
    }
}
