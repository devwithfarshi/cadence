using Cadence.Domain.Common;

namespace Cadence.Domain.Intelligence.Events;

/// <summary>A summary exists. Notifications and the knowledge base subscribe to this.</summary>
public sealed record SummaryReady(Guid MeetingId, Guid OrganizationId, Guid SummaryId) : DomainEvent;

/// <summary>
/// The model flagged commitments in a transcript.
/// </summary>
/// <remarks>
/// Carries <i>candidates</i>, not created tasks. Extraction accuracy is imperfect, so when the
/// organization has review enabled these are held for confirmation rather than written straight
/// into someone's task list (blueprint §5.5, §23.3).
/// </remarks>
public sealed record ActionItemsDetected(
    Guid MeetingId,
    Guid OrganizationId,
    IReadOnlyList<DetectedActionItem> Items) : DomainEvent;

/// <param name="Title">What was committed to.</param>
/// <param name="AssigneeId">Best-guess owner; null when the model could not attribute it.</param>
/// <param name="SourceSegmentId">The transcript line it came from, so a reviewer can check it.</param>
public sealed record DetectedActionItem(string Title, Guid? AssigneeId, Guid? SourceSegmentId);
