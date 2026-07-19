using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// A tenant. Every tenant-scoped row carries this aggregate's id (blueprint §3.3).
/// </summary>
public sealed class Organization : AggregateRoot, ISoftDeletable
{
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

    public static Organization Create(string name, Guid ownerId)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Organization name is required.");

        var organization = new Organization(name.Trim(), Slugify(name), ownerId);
        organization.AddMember(ownerId, UserRole.Owner);
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
