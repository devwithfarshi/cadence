using Cadence.Application.Common.Abstractions;

namespace Cadence.IntegrationTests;

/// <summary>
/// A stand-in for the model, staged per test.
/// </summary>
/// <remarks>
/// Faked at the port and nowhere deeper, per §18's mocking policy. What these tests are actually
/// about is the <i>pipeline</i> — status transitions, replace-not-duplicate, and above all that a
/// failure is recorded as a failure — and none of that needs a real model. It does need a
/// deterministic one.
/// </remarks>
public sealed class FakeLlmProvider : ILlmProvider
{
    private Func<SummaryRequest, MeetingSummaryResult>? _summarise;

    /// <summary>The last transcript the provider was handed, so a test can assert on the prompt.</summary>
    public string? LastTranscript { get; private set; }

    public int SummariseCallCount { get; private set; }

    /// <summary>Makes the next summarisation return <paramref name="result"/>.</summary>
    public void Returns(MeetingSummaryResult result) => _summarise = _ => result;

    /// <summary>
    /// Makes the next summarisation fail.
    /// </summary>
    /// <remarks>
    /// The failure mode that matters most. Every provider failure — outage, refusal, rate limit,
    /// truncation, unparseable output — reaches the handler as an exception, so one staged throw
    /// covers the class.
    /// </remarks>
    public void Throws(string message = "The AI provider could not be reached.") =>
        _summarise = _ => throw new InvalidOperationException(message);

    public Task<MeetingSummaryResult> SummariseAsync(
        SummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        SummariseCallCount++;
        LastTranscript = request.Transcript;

        if (_summarise is null)
        {
            throw new InvalidOperationException(
                "No summary staged. Call Returns(...) or Throws() before exercising the pipeline.");
        }

        return Task.FromResult(_summarise(request));
    }

    public Task<GroundedAnswer> AnswerAsync(
        string question,
        IReadOnlyList<ContextPassage> context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new GroundedAnswer("Not implemented in tests.", []));

    /// <summary>Convenience for the common "a normal summary came back" case.</summary>
    public static MeetingSummaryResult Summary(
        string text = "The team agreed to ship on Friday.",
        IReadOnlyList<string>? keyPoints = null,
        IReadOnlyList<SummaryHighlightCandidate>? highlights = null,
        IReadOnlyList<ActionItemCandidate>? actionItems = null,
        string model = "claude-opus-4-8") =>
        new(text, keyPoints ?? ["Ship on Friday."], highlights ?? [], actionItems ?? [], model);
}
