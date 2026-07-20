namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// The AI model, as a dependency.
/// </summary>
/// <remarks>
/// Kept provider-neutral so OpenAI, Anthropic or Gemini is a configuration choice rather than a
/// rewrite (§23.1). Nothing here names a vendor or a model.
/// </remarks>
public interface ILlmProvider
{
    /// <summary>Produces a structured summary of a transcript.</summary>
    Task<MeetingSummaryResult> SummariseAsync(
        SummaryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a question over supplied context passages.
    /// </summary>
    /// <remarks>
    /// The context is passed in rather than retrieved here, so retrieval stays testable on its own
    /// and the model is never asked to answer from anything but workspace records.
    /// </remarks>
    Task<GroundedAnswer> AnswerAsync(
        string question,
        IReadOnlyList<ContextPassage> context,
        CancellationToken cancellationToken = default);
}

public sealed record SummaryRequest(
    string Transcript,
    string MeetingTitle,
    IReadOnlyList<string> ParticipantNames,
    string OutputLanguage,
    string Detail);

/// <summary>What the model produced for one meeting.</summary>
/// <remarks>
/// <c>Highlights</c> are decisions, risks and questions, each pointing back at the line that produced
/// it. <c>ActionItems</c> are <b>candidates only</b> — whether they become real tasks is the user's
/// call, gated by their <c>RequireActionItemReview</c> preference, so the model never silently
/// assigns work to a colleague.
/// <para>
/// <c>Model</c> is reported by the adapter rather than read from configuration by the caller, so the
/// provenance stored on a summary names the model that actually produced it — not whatever the
/// config happened to say when the row was later read.
/// </para>
/// </remarks>
public sealed record MeetingSummaryResult(
    string Summary,
    IReadOnlyList<string> KeyPoints,
    IReadOnlyList<SummaryHighlightCandidate> Highlights,
    IReadOnlyList<ActionItemCandidate> ActionItems,
    string Model);

public sealed record SummaryHighlightCandidate(string Kind, string Text, Guid? SourceSegmentId);

public sealed record ActionItemCandidate(string Title, string? AssigneeName, string Priority, Guid? SourceSegmentId);

/// <summary>One retrieved passage handed to the model as context.</summary>
/// <remarks><c>SourceId</c> is the record it came from, so the answer can cite it.</remarks>
public sealed record ContextPassage(Guid SourceId, string Kind, string Label, string Text);

/// <summary>An answer together with the records it is based on.</summary>
/// <remarks>
/// <c>CitedSourceIds</c> must be a subset of the supplied passages. An answer citing anything else is
/// discarded rather than shown — a fabricated citation is worse than no answer (§23.3).
/// </remarks>
public sealed record GroundedAnswer(string Answer, IReadOnlyList<Guid> CitedSourceIds);
