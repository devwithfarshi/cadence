using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Infrastructure.Ai;

/// <summary>
/// The <see cref="ILlmProvider"/> adapter for Claude, via the official Anthropic SDK.
/// </summary>
/// <remarks>
/// <para>
/// <b>This class never invents content.</b> Every failure path — no key, a refusal, a truncated
/// response, unparseable output, a transport error — throws <see cref="LlmProviderException"/>, and
/// the caller records <c>summary_status = failed</c>. A summary that reads plausibly but was not
/// derived from the transcript is worse than no summary, because nothing marks it as wrong (§23.3).
/// </para>
/// <para>
/// The blueprint's §23.3 says "via raw HTTP". The official SDK is used instead: it is the supported
/// surface, and it already carries the retry/backoff, timeout and typed-error handling that hand-
/// rolled HTTP would have to reimplement. The port is unchanged, so a raw-HTTP or non-Claude adapter
/// can still replace this one.
/// </para>
/// </remarks>
public sealed class AnthropicLlmProvider : ILlmProvider
{
    private readonly AnthropicClient _client;
    private readonly AiOptions _options;
    private readonly ILogger<AnthropicLlmProvider> _logger;

    public AnthropicLlmProvider(
        IOptions<AiOptions> options,
        ILogger<AnthropicLlmProvider> logger)
    {
        _options = options.Value;
        _logger = logger;

        _client = new AnthropicClient
        {
            ApiKey = _options.ApiKey,
            Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds),
        };
    }

    public async Task<MeetingSummaryResult> SummariseAsync(
        SummaryRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var (transcript, truncated) = Truncate(request.Transcript);

        var response = await SendAsync(
            system: SummaryPrompts.System(request.OutputLanguage, request.Detail),
            user: SummaryPrompts.User(request, transcript, truncated),
            schema: SummarySchema.Definition,
            cancellationToken);

        var payload = ReadJson<SummaryPayload>(response);

        return new MeetingSummaryResult(
            payload.Summary,
            payload.KeyPoints ?? [],
            [.. (payload.Highlights ?? []).Select(highlight => new SummaryHighlightCandidate(
                highlight.Kind,
                highlight.Text,
                // The model returns the segment id it was shown, or null. A value that is not a real
                // id is dropped rather than stored: a citation that does not resolve is worse than
                // an unattributed line, because it looks verifiable and is not.
                ParseSegmentId(highlight.SourceSegmentId)))],
            [.. (payload.ActionItems ?? []).Select(item => new ActionItemCandidate(
                item.Title,
                item.AssigneeName,
                item.Priority ?? "medium",
                ParseSegmentId(item.SourceSegmentId)))],
            // Reported from the response, not from configuration: if a fallback or an override ever
            // serves the request, the summary should name the model that actually wrote it.
            response.Model);
    }

    public async Task<GroundedAnswer> AnswerAsync(
        string question,
        IReadOnlyList<ContextPassage> context,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (context.Count == 0)
        {
            // No retrieval hits means there is nothing in the workspace to ground an answer in.
            // Asking the model anyway would get a confident answer from its own training data,
            // attributed to a workspace that never said it.
            return new GroundedAnswer(
                "I could not find anything in this workspace that answers that.",
                []);
        }

        var response = await SendAsync(
            system: ChatPrompts.System,
            user: ChatPrompts.User(question, context),
            schema: AnswerSchema.Definition,
            cancellationToken);

        var payload = ReadJson<AnswerPayload>(response);

        var allowed = context.Select(passage => passage.SourceId).ToHashSet();

        // Citations are intersected with what was actually supplied. The port's contract says an
        // answer citing anything else is discarded — a fabricated citation is worse than no answer,
        // so the id is dropped rather than shown as a dead link.
        var cited = (payload.CitedSourceIds ?? [])
            .Select(ParseSegmentId)
            .Where(id => id is { } value && allowed.Contains(value))
            .Select(id => id!.Value)
            .Distinct()
            .ToList();

        return new GroundedAnswer(payload.Answer, cited);
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new LlmProviderException(
                "No AI provider key is configured. Set Ai__ApiKey to enable summaries.");
        }
    }

    /// <summary>
    /// One request, with the shared model settings and failure handling.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Structured outputs, not "reply with JSON".</b> <c>OutputConfig.Format</c> constrains the
    /// response to the schema, so parsing cannot fail on a stray preamble or a trailing explanation
    /// — the failure mode that makes prompt-and-hope JSON unreliable in production.
    /// </para>
    /// <para>
    /// Adaptive thinking is on: summarising a meeting means weighing what mattered, which is exactly
    /// the work thinking is for. <c>display</c> is left at its default — the reasoning is never
    /// surfaced or stored, so there is nothing to gain from returning it.
    /// </para>
    /// <para>
    /// Not streamed. <c>MaxTokens</c> is bounded well below the point where a non-streaming request
    /// risks an HTTP timeout, and this runs inside a background job where a slow call costs nothing
    /// but its own latency.
    /// </para>
    /// </remarks>
    private async Task<Message> SendAsync(
        string system,
        string user,
        IReadOnlyDictionary<string, JsonElement> schema,
        CancellationToken cancellationToken)
    {
        Message response;

        try
        {
            response = await _client.Messages.Create(
                new MessageCreateParams
                {
                    Model = _options.Model,
                    MaxTokens = _options.MaxTokens,
                    System = new List<TextBlockParam> { new() { Text = system } },
                    Thinking = new ThinkingConfigAdaptive(),
                    OutputConfig = new OutputConfig
                    {
                        Effort = _options.Effort,
                        Format = new JsonOutputFormat { Schema = schema },
                    },
                    Messages = [new() { Role = Role.User, Content = user }],
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Typed SDK exceptions (rate limit, 5xx, transport) all land here. The job's retry
            // policy decides whether to try again; this layer's job is to fail honestly.
            throw new LlmProviderException("The AI provider could not be reached.", exception);
        }

        if (response.StopReason == "refusal")
        {
            // A safety decline. Surfaced as a failure so the organizer is told the summary could not
            // be produced, rather than being shown an empty one they might mistake for a quiet
            // meeting.
            _logger.LogWarning(
                "The model declined to summarise: {Category}",
                response.StopDetails?.Category);

            throw new LlmProviderException("The model declined to process this content.");
        }

        if (response.StopReason == "max_tokens")
        {
            // The response was cut off mid-object. Structured output guarantees the shape of a
            // *complete* response, not a truncated one — parsing this would either throw or, worse,
            // succeed against a half-written summary.
            throw new LlmProviderException(
                "The summary exceeded the configured token budget and was truncated.");
        }

        return response;
    }

    private T ReadJson<T>(Message response)
    {
        var text = response.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new LlmProviderException("The AI provider returned an empty response.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOptions)
                ?? throw new LlmProviderException("The AI provider returned an empty document.");
        }
        catch (JsonException exception)
        {
            // Should be unreachable while structured outputs hold, which is why it throws rather
            // than salvaging: silently accepting off-schema output is how a malformed summary
            // reaches a user.
            _logger.LogError(exception, "The model returned output that did not match the schema");

            throw new LlmProviderException("The AI provider returned an unreadable response.", exception);
        }
    }

    private (string Transcript, bool Truncated) Truncate(string transcript)
    {
        if (transcript.Length <= _options.MaxTranscriptCharacters)
        {
            return (transcript, false);
        }

        _logger.LogInformation(
            "Truncated a {Length}-character transcript to {Limit} for summarisation",
            transcript.Length,
            _options.MaxTranscriptCharacters);

        // The head is kept rather than the tail: an agenda and the decisions it produces are usually
        // front-loaded, and the prompt tells the model the transcript was cut so it does not report
        // on an ending it never saw.
        return (transcript[.._options.MaxTranscriptCharacters], true);
    }

    private static Guid? ParseSegmentId(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SummaryPayload(
        string Summary,
        IReadOnlyList<string>? KeyPoints,
        IReadOnlyList<HighlightPayload>? Highlights,
        IReadOnlyList<ActionItemPayload>? ActionItems);

    private sealed record HighlightPayload(string Kind, string Text, string? SourceSegmentId);

    private sealed record ActionItemPayload(
        string Title,
        string? AssigneeName,
        string? Priority,
        string? SourceSegmentId);

    private sealed record AnswerPayload(string Answer, IReadOnlyList<string>? CitedSourceIds);
}

/// <summary>
/// The AI provider could not produce a usable result.
/// </summary>
/// <remarks>
/// Deliberately one exception for every failure mode. Callers do not branch on <i>why</i> the model
/// failed — the response is the same in all cases: mark the summary failed, notify the organizer,
/// offer a retry. Distinguishing them would only invite a code path that tries to carry on.
/// </remarks>
public sealed class LlmProviderException : Exception
{
    public LlmProviderException(string message)
        : base(message)
    {
    }

    public LlmProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
