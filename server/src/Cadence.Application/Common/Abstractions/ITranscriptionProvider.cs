namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Speech-to-text with speaker attribution.
/// </summary>
/// <remarks>
/// Separate from <see cref="ILlmProvider"/> because the two are chosen independently — a workspace
/// may want a specialised STT vendor and a general-purpose model for summarisation (§23.1).
/// </remarks>
public interface ITranscriptionProvider
{
    Task<TranscriptionResult> TranscribeAsync(
        Uri audioUrl,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>Hints for one transcription run.</summary>
/// <remarks>
/// <c>ExpectedSpeakers</c> is a hint, not a constraint. Diarisation is markedly more accurate when
/// the count is known, and it usually is — the participant list.
/// </remarks>
public sealed record TranscriptionOptions(string? Language = null, int? ExpectedSpeakers = null);

public sealed record TranscriptionResult(IReadOnlyList<TranscribedSegment> Segments, string? DetectedLanguage);

/// <summary>One attributed line of speech.</summary>
/// <remarks>
/// Offsets are in <b>milliseconds</b> from the start of the recording — the same unit the transcript
/// entity stores, so nothing has to convert and lose precision. <c>SpeakerLabel</c> is the provider's
/// own label (<c>Speaker 1</c>); mapping it to a participant is Cadence's job, not the provider's.
/// <c>Confidence</c> is 0–1, and low-confidence lines are surfaced rather than quietly dropped.
/// </remarks>
public sealed record TranscribedSegment(
    int StartMs,
    int EndMs,
    string SpeakerLabel,
    string Text,
    double Confidence);
