using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// A person. Deliberately <b>global</b>, not tenant-scoped (blueprint §3.3).
/// </summary>
/// <remarks>
/// One person may belong to several organizations, so membership and role live on
/// <see cref="OrganizationMember"/> rather than here. Putting a role on the user would silently
/// cap the product at one organization per person.
/// <para>
/// There is no password: Google is the only sign-in method (§4.1), so identity is proven by an
/// <see cref="ExternalLogin"/> row. That removes the entire credential-stuffing surface.
/// </para>
/// </remarks>
public sealed class User : AggregateRoot, ISoftDeletable
{
    private readonly List<ExternalLogin> _externalLogins = [];
    private readonly List<OrganizationMember> _memberships = [];

    private User()
    {
        // EF materialisation.
        Email = null!;
        Name = null!;
    }

    private User(string email, string name, string? avatarUrl, string timezone)
    {
        Email = email;
        Name = name;
        AvatarUrl = avatarUrl;
        Timezone = timezone;
        Status = UserStatus.Active;
        LastActiveAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The Google-owned identity key. Not editable — changing it would orphan the external login.</summary>
    public string Email { get; private set; }

    public string Name { get; private set; }

    public string? AvatarUrl { get; private set; }

    public string JobTitle { get; private set; } = string.Empty;

    public string Department { get; private set; } = string.Empty;

    public string Timezone { get; private set; } = "UTC";

    public UserStatus Status { get; private set; }

    public DateTimeOffset LastActiveAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public IReadOnlyCollection<ExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();

    public IReadOnlyCollection<OrganizationMember> Memberships => _memberships.AsReadOnly();

    public static User Create(string email, string name, string? avatarUrl = null, string timezone = "UTC")
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(email), "Email is required.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Name is required.");

        return new User(email.Trim().ToLowerInvariant(), name.Trim(), avatarUrl, timezone);
    }

    public void UpdateProfile(string name, string jobTitle, string department, string timezone)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Name cannot be empty.");

        Name = name.Trim();
        JobTitle = jobTitle.Trim();
        Department = department.Trim();
        Timezone = string.IsNullOrWhiteSpace(timezone) ? "UTC" : timezone.Trim();
    }

    public void UpdateAvatar(string? avatarUrl) => AvatarUrl = avatarUrl;

    public void Touch() => LastActiveAt = DateTimeOffset.UtcNow;

    public void Suspend()
    {
        DomainException.ThrowIf(Status == UserStatus.Suspended, "User is already suspended.");
        Status = UserStatus.Suspended;
    }

    public void Reactivate() => Status = UserStatus.Active;

    public void LinkExternalLogin(ExternalLogin login)
    {
        // A second Google account on the same user would make sign-in ambiguous.
        DomainException.ThrowIf(
            _externalLogins.Any(existing => existing.Provider == login.Provider),
            $"This account is already linked to {login.Provider}.");

        _externalLogins.Add(login);
    }

    public void MarkDeleted(DateTimeOffset deletedAt, Guid? deletedBy)
    {
        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }
}
