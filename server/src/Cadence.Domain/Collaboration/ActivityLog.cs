using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Collaboration;

/// <summary>
/// One line in the workspace activity feed.
/// </summary>
/// <remarks>
/// Append-only: an activity entry is a record of something that happened, so it is never edited and
/// never soft-deleted. That is also why <see cref="Summary"/> is stored already rendered — the feed
/// must still read correctly after the thing it refers to is renamed or deleted, and resolving names
/// at read time would either produce dangling rows or require joining every module.
/// </remarks>
public sealed class ActivityLog : Entity
{
    private ActivityLog()
    {
        Summary = null!;
    }

    private ActivityLog(
        Guid organizationId,
        Guid actorId,
        ActivityKind kind,
        string summary,
        Guid? targetId,
        string? href)
    {
        OrganizationId = organizationId;
        ActorId = actorId;
        Kind = kind;
        Summary = summary;
        TargetId = targetId;
        Href = href;
        OccurredAt = DateTimeOffset.UtcNow;
    }

    public Guid OrganizationId { get; private set; }

    public Guid ActorId { get; private set; }

    public ActivityKind Kind { get; private set; }

    /// <summary>Display text, resolved at write time. See the remarks on the class.</summary>
    public string Summary { get; private set; }

    /// <summary>The meeting, action item or document this refers to, when there is one.</summary>
    public Guid? TargetId { get; private set; }

    public string? Href { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public static ActivityLog Record(
        Guid organizationId,
        Guid actorId,
        ActivityKind kind,
        string summary,
        Guid? targetId = null,
        string? href = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(summary), "Activity summary is required.");

        return new ActivityLog(organizationId, actorId, kind, summary.Trim(), targetId, href);
    }
}
