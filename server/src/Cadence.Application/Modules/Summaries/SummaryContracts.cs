using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Summaries;

/// <summary>
/// A meeting's AI summary, shaped like the client's <c>AISummary</c>.
/// </summary>
/// <remarks>
/// <c>Model</c> is not decoration. Generated text presented without provenance invites a reader to
/// treat it as human-authored, and the meeting summary is exactly the kind of document people quote
/// back at each other. The UI renders it as a provenance line.
/// </remarks>
public sealed record AiSummaryDto(
    Guid Id,
    Guid MeetingId,
    string ExecutiveSummary,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<SummaryHighlightDto> Highlights,
    string Model,
    DateTimeOffset GeneratedAt);

/// <summary>
/// A decision, risk or open question drawn out of the meeting.
/// </summary>
/// <remarks>
/// <c>SourceSegmentId</c> and <c>AtSeconds</c> are the point: they let the reader click through to
/// the line that produced the claim and check it. A highlight with neither is allowed only where the
/// model could not attribute it, never as a shortcut.
/// </remarks>
public sealed record SummaryHighlightDto(
    Guid Id,
    SummaryHighlightKind Kind,
    string Text,
    Guid? SourceSegmentId,
    int? AtSeconds);

/// <summary>
/// What a regeneration request returns.
/// </summary>
/// <remarks>
/// Summarisation takes seconds to minutes, so the endpoint answers <c>202 Accepted</c> with the job
/// id rather than holding the request open (§14.1). The client polls the meeting's
/// <c>summaryStatus</c>, or listens on the hub.
/// </remarks>
public sealed record JobAcceptedDto(string JobId);
