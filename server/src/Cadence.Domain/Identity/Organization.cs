using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// A tenant. Every tenant-scoped row carries this aggregate's id (blueprint §3.3).
/// </summary>
public sealed class Organization : AggregateRoot, ISoftDeletable
{
    /// <summary>Trailing hex digits of the id appended to a personal workspace's slug.</summary>
    private const int DiscriminatorLength = 6;

    private readonly List<OrganizationMember> _members = [];

    private Organization()
    {
        Name = null!;
        Slug = null!;
        Settings = null!;
    }

    private Organization(string name, string slug, Guid ownerId)
    {
        Name = name;
        Slug = slug;
        OwnerId = ownerId;
        Plan = OrganizationPlan.Free;
        Settings = WorkspaceSettings.Default(name);
    }

    public string Name { get; private set; }

    /// <summary>URL-safe identifier. Unique among non-deleted organizations.</summary>
    public string Slug { get; private set; }

    public OrganizationPlan Plan { get; private set; }

    public Guid OwnerId { get; private set; }

    public WorkspaceSettings Settings { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public IReadOnlyCollection<OrganizationMember> Members => _members.AsReadOnly();

    /// <summary>
    /// Creates a workspace with a clean slug derived from its name.
    /// </summary>
    /// <remarks>
    /// The slug may collide with an existing one, and the partial unique index is what says so. That
    /// is deliberate for a deliberately-named workspace: someone typing "Northwind" should be told
    /// the name is taken, not silently given "northwind-a91f4c".
    /// </remarks>
    public static Organization Create(string name, Guid ownerId)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Organization name is required.");

        var organization = new Organization(name.Trim(), Slugify(name), ownerId);
        organization.AddMember(ownerId, UserRole.Owner);
        return organization;
    }

    /// <summary>
    /// Creates the personal workspace a user gets on first sign-in, with a slug that cannot collide.
    /// </summary>
    /// <remarks>
    /// Names repeat — two people called Alex Rivera both provision "Alex Rivera's workspace" — and
    /// without a discriminator the second sign-up hits the unique index and fails. Here a collision
    /// is not worth telling anyone about, so six hex digits of the id are appended. The index remains
    /// the backstop.
    /// <para>
    /// The <b>last</b> six, not the first. A UUIDv7 leads with a 48-bit timestamp, so its opening
    /// digits are identical for everything created in the same stretch of time — a prefix would
    /// reproduce exactly the collision it is meant to prevent.
    /// </para>
    /// </remarks>
    public static Organization CreatePersonal(string name, Guid ownerId)
    {
        var organization = Create(name, ownerId);

        var discriminator = organization.Id.ToString("n")[^DiscriminatorLength..];
        organization.Slug = $"{organization.Slug}-{discriminator}";

        return organization;
    }

    public void Rename(string name)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Organization name cannot be empty.");
        Name = name.Trim();
    }

    public OrganizationMember AddMember(Guid userId, UserRole role)
    {
        DomainException.ThrowIf(
            _members.Any(member => member.UserId == userId),
            "That person is already a member of this organization.");

        var member = OrganizationMember.Create(Id, userId, role);
        _members.Add(member);
        return member;
    }

    public void ChangeMemberRole(Guid userId, UserRole role)
    {
        var member = _members.FirstOrDefault(candidate => candidate.UserId == userId)
            ?? throw new DomainException("That person is not a member of this organization.");

        // An organization with no owner cannot be administered — nobody could restore access.
        // This is a domain invariant precisely so the API upholds it, not just the UI.
        if (member.Role == UserRole.Owner && role != UserRole.Owner && OwnerCount() == 1)
        {
            throw new DomainException("This is the only owner. Promote someone else first.");
        }

        member.ChangeRole(role);

        if (role == UserRole.Owner)
        {
            OwnerId = userId;
        }
    }

    public void RemoveMember(Guid userId)
    {
        var member = _members.FirstOrDefault(candidate => candidate.UserId == userId)
            ?? throw new DomainException("That person is not a member of this organization.");

        DomainException.ThrowIf(
            member.Role == UserRole.Owner && OwnerCount() == 1,
            "The only owner cannot be removed. Transfer ownership first.");

        _members.Remove(member);
    }

    public void UpdateSettings(WorkspaceSettings settings) => Settings = settings;

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

    private int OwnerCount() => _members.Count(member => member.Role == UserRole.Owner);

    private static string Slugify(string value)
    {
        var lowered = value.Trim().ToLowerInvariant();
        var builder = new System.Text.StringBuilder(lowered.Length);

        foreach (var character in lowered)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
