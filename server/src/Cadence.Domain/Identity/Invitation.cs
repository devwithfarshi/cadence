using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// A pending invitation to join an organization.
/// </summary>
/// <remarks>
/// The invitation is addressed to an email, but acceptance is still gated on Google sign-in: the
/// token in the email only identifies the invitation, it never authenticates anyone. Whoever follows
/// the link signs in with Google first, and the invitation is matched against the verified Google
/// email — so a forwarded link cannot be redeemed by the wrong person (blueprint §5.6).
/// </remarks>
public sealed class Invitation : AggregateRoot, ITenantScoped
{
    private Invitation()
    {
        Email = null!;
        TokenHash = null!;
    }

    private Invitation(
        Guid organizationId,
        string email,
        UserRole role,
        Guid invitedById,
        string tokenHash,
        DateTimeOffset expiresAt)
    {
        OrganizationId = organizationId;
        Email = email;
        Role = role;
        InvitedById = invitedById;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        Status = InvitationStatus.Pending;
    }

    public Guid OrganizationId { get; private set; }

    public string Email { get; private set; }

    public UserRole Role { get; private set; }

    public InvitationStatus Status { get; private set; }

    public Guid InvitedById { get; private set; }

    /// <summary>SHA-256 of the emailed token. The token itself is never stored.</summary>
    public string TokenHash { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    /// <summary>The user that redeemed it, which need not have existed when it was sent.</summary>
    public Guid? AcceptedByUserId { get; private set; }

    /// <summary>
    /// <paramref name="now"/> is passed rather than read from the ambient clock so the whole
    /// lifecycle — creation, redemption, expiry — is driven by one caller-supplied time.
    /// </summary>
    public static Invitation Create(
        Guid organizationId,
        string email,
        UserRole role,
        Guid invitedById,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(email), "An invitation needs an email address.");
        DomainException.ThrowIf(
            role == UserRole.Owner,
            "Ownership is transferred, not invited.");
        DomainException.ThrowIf(
            expiresAt <= now,
            "An invitation cannot be created already expired.");

        return new Invitation(
            organizationId,
            email.Trim().ToLowerInvariant(),
            role,
            invitedById,
            tokenHash,
            expiresAt);
    }

    /// <summary>Expiry is evaluated on read; nothing has to run on a schedule for it to hold.</summary>
    public bool IsRedeemable(DateTimeOffset now) =>
        Status == InvitationStatus.Pending && now < ExpiresAt;

    public void Accept(Guid userId, string verifiedEmail, DateTimeOffset now)
    {
        DomainException.ThrowIf(
            !IsRedeemable(now),
            "This invitation is no longer valid.");
        DomainException.ThrowIf(
            !string.Equals(verifiedEmail.Trim(), Email, StringComparison.OrdinalIgnoreCase),
            "This invitation was issued to a different email address.");

        Status = InvitationStatus.Accepted;
        AcceptedAt = now;
        AcceptedByUserId = userId;
    }

    public void Revoke()
    {
        DomainException.ThrowIf(
            Status == InvitationStatus.Accepted,
            "An accepted invitation cannot be revoked; remove the member instead.");

        Status = InvitationStatus.Revoked;
    }

    /// <summary>Materialises expiry so the list view can show it; <see cref="IsRedeemable"/> is the guard.</summary>
    public void MarkExpired(DateTimeOffset now)
    {
        if (Status == InvitationStatus.Pending && now >= ExpiresAt)
        {
            Status = InvitationStatus.Expired;
        }
    }
}
