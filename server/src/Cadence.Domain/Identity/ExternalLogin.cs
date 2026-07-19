using Cadence.Domain.Common;

namespace Cadence.Domain.Identity;

/// <summary>
/// Proof that a user controls an identity at an external provider.
/// </summary>
/// <remarks>
/// Sign-in resolves <c>(provider, subject)</c> to a user. The <b>subject</b> — Google's <c>sub</c>
/// claim — is the stable key, not the email: a person can change their Google email address and
/// must remain the same user.
/// <para>
/// <see cref="EmailAtProvider"/> is recorded only for support and first-link matching. Linking by
/// email is gated on Google reporting <c>email_verified</c>; skipping that check is a known
/// account-takeover vector (blueprint §4.2).
/// </para>
/// </remarks>
public sealed class ExternalLogin : Entity
{
    private ExternalLogin()
    {
        Provider = null!;
        Subject = null!;
        EmailAtProvider = null!;
    }

    private ExternalLogin(Guid userId, string provider, string subject, string emailAtProvider)
    {
        UserId = userId;
        Provider = provider;
        Subject = subject;
        EmailAtProvider = emailAtProvider;
        LinkedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }

    /// <summary>Currently only <c>google</c> — the single permitted method (§4.1).</summary>
    public string Provider { get; private set; }

    /// <summary>The provider's stable subject identifier (Google's <c>sub</c>).</summary>
    public string Subject { get; private set; }

    public string EmailAtProvider { get; private set; }

    public DateTimeOffset LinkedAt { get; private set; }

    public static ExternalLogin ForGoogle(Guid userId, string subject, string email)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(subject), "Google subject is required.");

        return new ExternalLogin(userId, "google", subject, email.Trim().ToLowerInvariant());
    }
}
