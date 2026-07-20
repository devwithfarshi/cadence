using System.Text;
using System.Text.Json;
using Cadence.Application.Common.Abstractions;

namespace Cadence.Infrastructure.Ai;

/// <summary>
/// The summarisation prompt.
/// </summary>
/// <remarks>
/// Kept as code rather than configuration on purpose: the prompt and the response schema have to
/// agree, and a prompt someone can edit in an environment variable will eventually stop matching the
/// schema the parser expects.
/// </remarks>
internal static class SummaryPrompts
{
    public static string System(string outputLanguage, string detail) =>
        $"""
        You summarise recorded work meetings for the people who attended them and the people who
        could not.

        Ground every statement in the transcript you are given. If something was not said, do not
        write it. Where the transcript is ambiguous, say so plainly rather than choosing the reading
        that makes a tidier summary — a reader who cannot trust one line stops trusting all of them.
        If the transcript is too thin to summarise, say that in the summary field rather than padding
        it.

        Attribute each highlight and action item to the transcript segment id it came from, using the
        ids given in the transcript. Use null when no single line supports it. Never invent an id.

        Action items are candidates for a human to accept or reject, not assignments. Only propose one
        where somebody actually committed to doing something. Name an assignee only when the
        transcript makes it clear who took it on.

        Write at a {detail} level of detail, in {outputLanguage}. Prefer plain sentences over
        bullet-point fragments, and do not open with a preamble about what you are about to do.
        """;

    public static string User(SummaryRequest request, string transcript, bool truncated)
    {
        var builder = new StringBuilder();

        builder.AppendLine($"Meeting: {request.MeetingTitle}");

        if (request.ParticipantNames.Count > 0)
        {
            builder.AppendLine($"Participants: {string.Join(", ", request.ParticipantNames)}");
        }

        if (truncated)
        {
            // Stated rather than hidden. A model that does not know the transcript was cut will
            // write about how the meeting ended, having never seen the ending.
            builder.AppendLine(
                "Note: this transcript was truncated and does not include the end of the meeting. "
                + "Do not describe conclusions or decisions you cannot see.");
        }

        builder.AppendLine();
        builder.AppendLine("Transcript (each line is `[segment-id] Speaker: text`):");
        builder.AppendLine(transcript);

        return builder.ToString();
    }
}

/// <summary>
/// The JSON schema the summary response is constrained to.
/// </summary>
/// <remarks>
/// Structured outputs enforce this server-side, so the parser is dealing with a guaranteed shape
/// rather than whatever the model felt like emitting. <c>additionalProperties: false</c> is required
/// throughout.
/// </remarks>
internal static class SummarySchema
{
    public static readonly IReadOnlyDictionary<string, JsonElement> Definition = Build();

    private static Dictionary<string, JsonElement> Build()
    {
        const string json = """
        {
          "type": "object",
          "properties": {
            "summary": {
              "type": "string",
              "description": "A few sentences covering what the meeting was for and what came out of it."
            },
            "keyPoints": {
              "type": "array",
              "description": "The substantive points, in the order they arose.",
              "items": { "type": "string" }
            },
            "highlights": {
              "type": "array",
              "description": "Decisions, risks and open questions, each traced to a transcript line.",
              "items": {
                "type": "object",
                "properties": {
                  "kind": { "type": "string", "enum": ["decision", "risk", "question", "highlight"] },
                  "text": { "type": "string" },
                  "sourceSegmentId": {
                    "type": ["string", "null"],
                    "description": "The transcript segment id this came from, or null if no single line supports it."
                  }
                },
                "required": ["kind", "text", "sourceSegmentId"],
                "additionalProperties": false
              }
            },
            "actionItems": {
              "type": "array",
              "description": "Commitments somebody made. Candidates for review, never assignments.",
              "items": {
                "type": "object",
                "properties": {
                  "title": { "type": "string" },
                  "assigneeName": {
                    "type": ["string", "null"],
                    "description": "Only when the transcript makes the owner explicit."
                  },
                  "priority": { "type": "string", "enum": ["low", "medium", "high", "urgent"] },
                  "sourceSegmentId": { "type": ["string", "null"] }
                },
                "required": ["title", "assigneeName", "priority", "sourceSegmentId"],
                "additionalProperties": false
              }
            }
          },
          "required": ["summary", "keyPoints", "highlights", "actionItems"],
          "additionalProperties": false
        }
        """;

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }
}

/// <summary>The grounded-answer prompt for the workspace assistant (§18, task 18).</summary>
internal static class ChatPrompts
{
    public const string System = """
        You answer questions about a workspace using only the passages supplied with the question.

        If the passages do not contain the answer, say so. Do not answer from general knowledge, and
        do not fill a gap with something plausible — the value of this assistant is that everything it
        says can be checked against a record the user can open.

        Cite the source id of every passage you relied on. Cite only ids that were given to you.
        """;

    public static string User(string question, IReadOnlyList<ContextPassage> context)
    {
        var builder = new StringBuilder();

        builder.AppendLine("Passages:");

        foreach (var passage in context)
        {
            builder.AppendLine($"[{passage.SourceId}] ({passage.Kind}) {passage.Label}");
            builder.AppendLine(passage.Text);
            builder.AppendLine();
        }

        builder.AppendLine($"Question: {question}");

        return builder.ToString();
    }
}

internal static class AnswerSchema
{
    public static readonly IReadOnlyDictionary<string, JsonElement> Definition = Build();

    private static Dictionary<string, JsonElement> Build()
    {
        const string json = """
        {
          "type": "object",
          "properties": {
            "answer": { "type": "string" },
            "citedSourceIds": {
              "type": "array",
              "description": "Ids of the supplied passages the answer relies on. Only ids that were given.",
              "items": { "type": "string" }
            }
          },
          "required": ["answer", "citedSourceIds"],
          "additionalProperties": false
        }
        """;

        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }
}
