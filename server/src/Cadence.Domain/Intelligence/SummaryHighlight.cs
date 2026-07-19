using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Intelligence;

/// <summary>
/// A decision, risk, open question or notable moment pulled out of a meeting.
/// </summary>
/// <remarks>
/// <see cref="SourceSegmentId"/> is the point of this entity: every claim links back to the
/// transcript line it came from, so a reader can verify it rather than trusting the model. A
/// highlight with no traceable source is far less useful and is allowed only when the model could
/// not attribute it.
/// </remarks>
public sealed class SummaryHighlight : Entity
{
    private SummaryHighlight()
    {
        Text = null!;
    }

    private SummaryHighlight(
        Guid summaryId,
        SummaryHighlightKind kind,
        string text,
        Guid? sourceSegmentId,
        int? atSeconds)
    {
        SummaryId = summaryId;
        Kind = kind;
        Text = text;
        SourceSegmentId = sourceSegmentId;
        AtSeconds = atSeconds;
    }

    public Guid SummaryId { get; private set; }

    public SummaryHighlightKind Kind { get; private set; }

    public string Text { get; private set; }

    /// <summary>The transcript line this was drawn from, so the claim can be verified.</summary>
    public Guid? SourceSegmentId { get; private set; }

    public int? AtSeconds { get; private set; }

    internal static SummaryHighlight Create(
        Guid summaryId,
        SummaryHighlightKind kind,
        string text,
        Guid? sourceSegmentId,
        int? atSeconds)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(text), "Highlight text is required.");

        return new SummaryHighlight(summaryId, kind, text.Trim(), sourceSegmentId, atSeconds);
    }
}
