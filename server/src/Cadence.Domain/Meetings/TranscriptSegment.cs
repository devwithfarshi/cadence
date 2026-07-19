using Cadence.Domain.Common;

namespace Cadence.Domain.Meetings;

/// <summary>
/// One utterance in a meeting transcript.
/// </summary>
/// <remarks>
/// Offsets are stored in <b>milliseconds</b>, not seconds: speech-to-text providers emit
/// millisecond boundaries, and rounding at ingest would make segments overlap or leave gaps that
/// are impossible to recover later. The API converts to seconds for display.
/// <para>
/// <see cref="SpeakerName"/> is denormalised alongside <see cref="SpeakerId"/> (blueprint §3.9) so
/// the transcript still reads correctly after a name change, and so external speakers who have no
/// user row can still be labelled.
/// </para>
/// </remarks>
public sealed class TranscriptSegment : Entity
{
    private TranscriptSegment()
    {
        SpeakerName = null!;
        Content = null!;
    }

    private TranscriptSegment(
        Guid meetingId,
        Guid? speakerId,
        string speakerName,
        int startMs,
        int endMs,
        string content,
        double confidence)
    {
        MeetingId = meetingId;
        SpeakerId = speakerId;
        SpeakerName = speakerName;
        StartMs = startMs;
        EndMs = endMs;
        Content = content;
        Confidence = confidence;
    }

    public Guid MeetingId { get; private set; }

    /// <summary>Null for a speaker who is not a Cadence user.</summary>
    public Guid? SpeakerId { get; private set; }

    public string SpeakerName { get; private set; }

    public int StartMs { get; private set; }

    public int EndMs { get; private set; }

    public string Content { get; private set; }

    /// <summary>Model confidence 0–1. Low values are surfaced with a marker rather than hidden.</summary>
    public double Confidence { get; private set; }

    /// <summary>Set when the AI flagged this line as containing a commitment.</summary>
    public bool IsActionItem { get; private set; }

    public int DurationMs => EndMs - StartMs;

    public static TranscriptSegment Create(
        Guid meetingId,
        Guid? speakerId,
        string speakerName,
        int startMs,
        int endMs,
        string content,
        double confidence)
    {
        DomainException.ThrowIf(startMs < 0, "Segment start cannot be negative.");
        DomainException.ThrowIf(endMs < startMs, "Segment end cannot precede its start.");
        DomainException.ThrowIf(confidence is < 0 or > 1, "Confidence must be between 0 and 1.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(speakerName), "Speaker name is required.");

        return new TranscriptSegment(
            meetingId,
            speakerId,
            speakerName.Trim(),
            startMs,
            endMs,
            content.Trim(),
            confidence);
    }

    public void FlagAsActionItem() => IsActionItem = true;

    public void ClearActionItemFlag() => IsActionItem = false;
}
