using System.ComponentModel.DataAnnotations;

namespace Cadence.Infrastructure.Configuration;

/// <summary>
/// Settings for the AI provider behind <c>ILlmProvider</c> (§23.3).
/// </summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    /// <summary>
    /// Which adapter to resolve. Only <c>anthropic</c> is implemented today.
    /// </summary>
    /// <remarks>
    /// Kept as configuration rather than hard-wired so adding a second provider is a new adapter
    /// plus a case here, not a change to any caller — the point of the port (§23.1).
    /// </remarks>
    [Required]
    public string Provider { get; init; } = "anthropic";

    /// <summary>
    /// The provider key. Empty disables AI rather than failing at startup.
    /// </summary>
    /// <remarks>
    /// A developer running the app to look at meetings should not have to hold a paid API key. With
    /// no key the pipeline records <c>summary_status = failed</c> — the same terminal state a real
    /// outage produces, so the UI path is exercised rather than bypassed. It never invents content.
    /// </remarks>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// The model id, recorded on every summary for provenance.
    /// </summary>
    /// <remarks>
    /// Stored on <c>ai_summary.model</c> so a reader can see which model wrote the text. Changing
    /// this does not rewrite existing summaries — each one keeps the model that produced it.
    /// </remarks>
    [Required]
    public string Model { get; init; } = "claude-opus-4-8";

    /// <summary>
    /// How hard the model should work, trading tokens and latency for quality.
    /// </summary>
    /// <remarks>
    /// <c>high</c> for summarisation: the input is a whole meeting and the output is read by people
    /// deciding what to do next, so this is not the place to economise.
    /// </remarks>
    public string Effort { get; init; } = "high";

    /// <summary>Upper bound on one response. Thinking counts against it.</summary>
    [Range(1024, 64000)]
    public int MaxTokens { get; init; } = 8000;

    [Range(5, 600)]
    public int TimeoutSeconds { get; init; } = 120;

    /// <summary>
    /// Longest transcript, in characters, sent to the model in one request.
    /// </summary>
    /// <remarks>
    /// A cost ceiling (§23.3). A three-hour meeting is a large prompt, and the summary of its middle
    /// hour is not proportionally more valuable. Truncation is reported to the model rather than
    /// hidden, so it does not describe the end of a meeting it was never shown.
    /// </remarks>
    [Range(1000, 2_000_000)]
    public int MaxTranscriptCharacters { get; init; } = 200_000;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
