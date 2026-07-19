using Cadence.Domain.Common;

namespace Cadence.Domain.Meetings.Events;

// Events are how the meetings module talks to the rest of the system (blueprint §7.1). Meetings
// knows nothing about transcription, summarisation or notifications — those modules subscribe.

public sealed record MeetingCreated(Guid MeetingId, Guid OrganizationId, Guid OrganizerId) : DomainEvent;

/// <summary>
/// A meeting has ended. This is what starts the processing pipeline (§14.3):
/// transcribe → summarise → extract action items.
/// </summary>
public sealed record MeetingCompleted(
    Guid MeetingId,
    Guid OrganizationId,
    Guid OrganizerId,
    int DurationSeconds) : DomainEvent;

/// <summary>Raised once a transcript exists, which is the precondition for summarisation.</summary>
public sealed record TranscriptReady(Guid MeetingId, Guid OrganizationId, int SegmentCount) : DomainEvent;
