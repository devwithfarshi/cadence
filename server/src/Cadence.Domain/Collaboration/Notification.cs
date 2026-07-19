using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Collaboration;

/// <summary>
/// One entry in a user's inbox.
/// </summary>
/// <remarks>
/// Not soft-deletable (blueprint §3.7): a dismissed notification carries no historical value, so
/// deleting it should actually free the row. High-volume tables that only ever grow are a
/// maintenance problem for no benefit.
/// </remarks>
public sealed class Notification : Entity
{
    private Notification()
    {
        Title = null!;
        Body = null!;
    }

    private Notification(
        Guid userId,
        Guid organizationId,
        NotificationKind kind,
        string title,
        string body,
        string? href,
        Guid? actorId)
    {
        UserId = userId;
        OrganizationId = organizationId;
        Kind = kind;
        Title = title;
        Body = body;
        Href = href;
        ActorId = actorId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid UserId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public NotificationKind Kind { get; private set; }

    public string Title { get; private set; }

    public string Body { get; private set; }

    /// <summary>In-app route this deep-links to.</summary>
    public string? Href { get; private set; }

    /// <summary>Who caused it; null for system-generated notifications.</summary>
    public Guid? ActorId { get; private set; }

    public bool IsRead { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Notification Create(
        Guid userId,
        Guid organizationId,
        NotificationKind kind,
        string title,
        string body,
        string? href = null,
        Guid? actorId = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Notification title is required.");

        return new Notification(userId, organizationId, kind, title.Trim(), body.Trim(), href, actorId);
    }

    public void SetRead(bool isRead) => IsRead = isRead;

    public void SetArchived(bool isArchived) => IsArchived = isArchived;
}
