using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Intelligence.Events;

namespace Cadence.Domain.Intelligence;

/// <summary>
/// The AI-generated summary of one meeting. At most one per meeting.
/// </summary>
/// <remarks>
/// <see cref="KeyPoints"/> is persisted as <c>jsonb</c> rather than a child table (blueprint §3.9):
/// it is an ordered list that is always read whole and never queried by element, so a table would
/// add a join and an ordering column for nothing. <see cref="SummaryHighlight"/> <i>is</i> a real
/// entity, because highlights are filtered by kind and joined back to transcript segments.
/// <para>
/// <see cref="Model"/> is recorded so the UI can state which model produced the text. Presenting
/// generated content without that provenance invites readers to treat it as human-authored.
/// </para>
/// </remarks>
public sealed class AiSummary : AggregateRoot, ITenantScoped
{
    private readonly List<string> _keyPoints = [];
    private readonly List<SummaryHighlight> _highlights = [];

    private AiSummary()
    {
        ExecutiveSummary = null!;
        Model = null!;
    }

    private AiSummary(Guid meetingId, Guid organizationId, string executiveSummary, string model)
    {
        MeetingId = meetingId;
        OrganizationId = organizationId;
        ExecutiveSummary = executiveSummary;
        Model = model;
        GeneratedAt = DateTimeOffset.UtcNow;
    }

    public Guid MeetingId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public string ExecutiveSummary { get; private set; }

    /// <summary>Which model produced this, shown in the provenance line.</summary>
    public string Model { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }

    public IReadOnlyCollection<string> KeyPoints => _keyPoints.AsReadOnly();

    public IReadOnlyCollection<SummaryHighlight> Highlights => _highlights.AsReadOnly();

    public static AiSummary Create(
        Guid meetingId,
        Guid organizationId,
        string executiveSummary,
        string model,
        IEnumerable<string>? keyPoints = null)
    {
        DomainException.ThrowIf(
            string.IsNullOrWhiteSpace(executiveSummary),
            "A summary cannot be empty. If generation failed, mark the meeting failed instead.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(model), "Model is required for provenance.");

        var summary = new AiSummary(meetingId, organizationId, executiveSummary.Trim(), model);

        if (keyPoints is not null)
        {
            summary._keyPoints.AddRange(keyPoints.Where(point => !string.IsNullOrWhiteSpace(point)));
        }

        summary.Raise(new SummaryReady(meetingId, organizationId, summary.Id));
        return summary;
    }

    public SummaryHighlight AddHighlight(
        SummaryHighlightKind kind,
        string text,
        Guid? sourceSegmentId,
        int? atSeconds)
    {
        var highlight = SummaryHighlight.Create(Id, kind, text, sourceSegmentId, atSeconds);
        _highlights.Add(highlight);
        return highlight;
    }

    /// <summary>
    /// Replaces the content in place on regeneration.
    /// </summary>
    /// <remarks>
    /// Jobs are at-least-once, so summarisation <i>will</i> occasionally run twice for one meeting.
    /// Replacing rather than inserting keeps that idempotent — a second run must not leave the
    /// meeting with two summaries (§14.3).
    /// </remarks>
    public void Replace(string executiveSummary, string model, IEnumerable<string> keyPoints)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(executiveSummary), "A summary cannot be empty.");

        ExecutiveSummary = executiveSummary.Trim();
        Model = model;
        GeneratedAt = DateTimeOffset.UtcNow;

        _keyPoints.Clear();
        _keyPoints.AddRange(keyPoints.Where(point => !string.IsNullOrWhiteSpace(point)));
        _highlights.Clear();
    }
}
