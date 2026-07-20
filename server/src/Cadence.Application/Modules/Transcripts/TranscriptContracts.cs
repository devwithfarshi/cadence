namespace Cadence.Application.Modules.Transcripts;

/// <summary>
/// One utterance, as the transcript view and the player render it.
/// </summary>
/// <remarks>
/// <b>Seconds here, milliseconds in the database.</b> The entity stores millisecond boundaries
/// because that is what speech-to-text emits and rounding at ingest makes segments overlap or leave
/// gaps that cannot be recovered later. The client seeks a media element, which takes seconds — so
/// the conversion happens once, at this boundary, rather than in every component.
/// <para>
/// <c>Text</c> and <c>SpeakerId</c> match the client's field names; the entity calls them
/// <c>Content</c> and keeps <c>SpeakerId</c> nullable for speakers who are not Cadence users.
/// </para>
/// </remarks>
public sealed record TranscriptSegmentDto(
    Guid Id,
    Guid MeetingId,
    Guid? SpeakerId,
    string SpeakerName,
    double StartSeconds,
    double EndSeconds,
    string Text,
    double Confidence,
    bool IsActionItem);

/// <summary>
/// One utterance arriving from a live recording.
/// </summary>
/// <remarks>
/// Offsets are milliseconds, matching the entity and the transcription provider. This is a
/// machine-to-machine shape — the recorder produces it — so it does not adopt the display units the
/// read model uses.
/// </remarks>
public sealed record AppendSegmentRequest(
    Guid? SpeakerId,
    string SpeakerName,
    int StartMs,
    int EndMs,
    string Text,
    double Confidence,
    bool IsActionItem);
