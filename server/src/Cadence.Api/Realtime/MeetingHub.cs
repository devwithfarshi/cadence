using Cadence.Application.Common.Abstractions;
using Cadence.Application.Modules.Transcripts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Api.Realtime;

/// <summary>
/// The live meeting channel: transcript lines, speaker changes and detected commitments (§14.4).
/// </summary>
/// <remarks>
/// <para>
/// Clients join a group per meeting, so a broadcast reaches the people watching that meeting and
/// nobody else. <b>Group membership is the authorization boundary</b> — SignalR groups are named by
/// a string the server chooses, and joining one is what grants the stream, so
/// <see cref="JoinMeeting"/> checks visibility before adding the connection rather than trusting the
/// meeting id it was handed.
/// </para>
/// <para>
/// The tenant filter does that checking: the meeting is looked up through the filtered set, so a
/// meeting id from another workspace is simply not found. That makes a guessed id useless even
/// though hub methods take one as a parameter.
/// </para>
/// </remarks>
[Authorize]
public sealed class MeetingHub(
    ICadenceDbContext context,
    ICurrentUser currentUser,
    TranscriptIngestBuffer buffer,
    TranscriptFlushService flush,
    ILogger<MeetingHub> logger)
    : Hub
{
    /// <summary>The group name a meeting's watchers share.</summary>
    /// <remarks>
    /// Derived from the id in one place, so the hub and the broadcaster cannot disagree about it —
    /// a mismatch would not fail anywhere, it would just deliver to an empty group.
    /// </remarks>
    public static string GroupFor(Guid meetingId) => $"meeting:{meetingId}";

    /// <summary>Subscribes this connection to a meeting it is allowed to see.</summary>
    public async Task JoinMeeting(Guid meetingId)
    {
        if (!await CanSeeAsync(meetingId))
        {
            // An exception, not a silent no-op: the client needs to know it will receive nothing,
            // or it sits waiting for a stream that is never coming.
            throw new HubException("That meeting could not be found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(meetingId));

        logger.LogDebug(
            "Connection {ConnectionId} joined meeting {MeetingId}",
            Context.ConnectionId,
            meetingId);
    }

    public async Task LeaveMeeting(Guid meetingId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(meetingId));

    /// <summary>
    /// Accepts a live transcript line: broadcast now, persisted on the next flush.
    /// </summary>
    /// <remarks>
    /// The two halves are deliberately different speeds. Watchers get the line immediately, because
    /// a live transcript that lags by the flush interval is not live. The database write is batched,
    /// because one insert and one transaction per utterance is write amplification for nothing
    /// (§14.4).
    /// <para>
    /// Visibility is re-checked rather than assumed from an earlier <see cref="JoinMeeting"/>: a
    /// connection can call this method without ever having joined, and "they must have joined first"
    /// is an assumption about a client we do not control.
    /// </para>
    /// </remarks>
    public async Task PushSegment(Guid meetingId, AppendSegmentRequest segment)
    {
        if (!await CanSeeAsync(meetingId))
        {
            throw new HubException("That meeting could not be found.");
        }

        var shouldFlushNow = buffer.Enqueue(meetingId, currentUser.RequireOrganizationId(), segment);

        // Echoed to the group straight away, with no id: it does not have one until it is written.
        // The client renders it as provisional and reconciles when the flush broadcasts the stored
        // rows.
        await Clients.Group(GroupFor(meetingId)).SendAsync("SegmentCaptured", meetingId, segment);

        if (shouldFlushNow)
        {
            // Not awaited on the hub call's own path — the caller is a recorder streaming audio and
            // should not wait on a database write it did not ask for. Failures are logged inside.
            _ = flush.FlushAsync(meetingId, CancellationToken.None);
        }
    }

    /// <summary>
    /// True when the caller's workspace can see this meeting.
    /// </summary>
    /// <remarks>
    /// No explicit organization predicate: <c>Meeting</c> is <c>ITenantScoped</c>, so the global
    /// filter has already applied the caller's workspace — the same rule the HTTP endpoints get, via
    /// the same <c>ICurrentUser</c> reading the same claims off the hub's principal.
    /// </remarks>
    private async Task<bool> CanSeeAsync(Guid meetingId) =>
        await context.Meetings
            .AsNoTracking()
            .AnyAsync(meeting => meeting.Id == meetingId, Context.ConnectionAborted);
}
