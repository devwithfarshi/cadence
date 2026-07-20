namespace Cadence.Application.Modules.Transcripts;

/// <summary>
/// Pushes live meeting events to the clients watching a meeting.
/// </summary>
/// <remarks>
/// <para>
/// A port, so Application can announce something happened without knowing that SignalR exists — it
/// may not reference ASP.NET at all, and an architecture test fails the build if it does. The
/// adapter lives in the Api layer, which is the only layer that can see a hub.
/// </para>
/// <para>
/// It sits in this module rather than in <c>Common/Abstractions</c> because it speaks in this
/// module's DTOs. A port in <c>Common</c> that referenced a module's types would point the
/// dependency backwards.
/// </para>
/// <para>
/// <b>Every method is best-effort.</b> A broadcast that fails must not fail the write that caused
/// it: the transcript is the record of what was said, and a dropped frame on a websocket is a
/// client that reconnects and re-reads. Implementations swallow and log.
/// </para>
/// </remarks>
public interface IMeetingBroadcaster
{
    /// <summary>Sends newly captured lines to everyone watching the meeting.</summary>
    Task SegmentsAppendedAsync(
        Guid meetingId,
        IReadOnlyList<TranscriptSegmentDto> segments,
        CancellationToken cancellationToken = default);

    /// <summary>Tells watchers the recording has finished and the processing pipeline has started.</summary>
    Task MeetingEndedAsync(Guid meetingId, CancellationToken cancellationToken = default);
}
