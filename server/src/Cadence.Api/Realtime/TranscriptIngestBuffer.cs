using System.Collections.Concurrent;
using Cadence.Application.Modules.Transcripts;

namespace Cadence.Api.Realtime;

/// <summary>
/// Accumulates live transcript lines so they reach Postgres in batches, not one insert per
/// utterance (§14.4).
/// </summary>
/// <remarks>
/// <para>
/// A singleton holding an in-memory queue per meeting. <b>This is deliberately lossy under a hard
/// crash</b>: up to one flush interval of transcript can be lost if the process dies. That is the
/// accepted trade — the alternative is a durable queue in front of the database, which is
/// substantially more infrastructure to protect a few seconds of a recording that the provider can
/// re-deliver anyway.
/// </para>
/// <para>
/// It is <i>not</i> lossy under ordinary shutdown: the flush service drains on stop.
/// </para>
/// <para>
/// One consequence of the in-memory design worth stating plainly: this does not survive more than
/// one API instance handling the same meeting. Scaling out needs a SignalR backplane and this
/// buffer moved behind one, which is the point at which Redis stops being just a cache.
/// </para>
/// </remarks>
public sealed class TranscriptIngestBuffer
{
    /// <summary>Flush a meeting early once it has this many lines waiting.</summary>
    /// <remarks>
    /// Bounds how much a busy meeting can accumulate between ticks, and keeps a single batch well
    /// inside the command's own limit.
    /// </remarks>
    public const int FlushThreshold = 100;

    private readonly ConcurrentDictionary<Guid, PendingMeeting> _pending = new();

    /// <summary>Meetings with lines waiting to be written.</summary>
    public IReadOnlyCollection<Guid> PendingMeetings => [.. _pending.Keys];

    /// <summary>
    /// Queues a line and reports whether the meeting is now worth flushing immediately.
    /// </summary>
    /// <remarks>
    /// <paramref name="organizationId"/> is the workspace the hub already established this caller
    /// could see. It is carried so the flush, which has no caller of its own, can act inside that
    /// tenant rather than none — see <c>ScopedPrincipal.ForOrganization</c>.
    /// </remarks>
    public bool Enqueue(Guid meetingId, Guid organizationId, AppendSegmentRequest segment)
    {
        var pending = _pending.GetOrAdd(meetingId, _ => new PendingMeeting(organizationId));
        pending.Segments.Enqueue(segment);

        return pending.Segments.Count >= FlushThreshold;
    }

    /// <summary>
    /// Takes everything currently queued for a meeting, with the workspace it belongs to.
    /// </summary>
    /// <remarks>
    /// Draining and writing are separate steps on purpose: the queue is released before the database
    /// call, so lines arriving during a slow write go into the next batch rather than blocking the
    /// hub method that produced them.
    /// <para>
    /// The empty queue is left in the dictionary rather than removed. Removing it races with an
    /// <see cref="Enqueue"/> that has already fetched it and is about to write into an orphan — the
    /// classic concurrent-dictionary bug, and the symptom is a silently dropped line.
    /// </para>
    /// </remarks>
    public (Guid OrganizationId, IReadOnlyList<AppendSegmentRequest> Segments) Drain(Guid meetingId)
    {
        if (!_pending.TryGetValue(meetingId, out var pending))
        {
            return (Guid.Empty, []);
        }

        var drained = new List<AppendSegmentRequest>();

        while (pending.Segments.TryDequeue(out var segment))
        {
            drained.Add(segment);
        }

        return (pending.OrganizationId, drained);
    }

    private sealed class PendingMeeting(Guid organizationId)
    {
        public Guid OrganizationId { get; } = organizationId;

        public ConcurrentQueue<AppendSegmentRequest> Segments { get; } = new();
    }
}
