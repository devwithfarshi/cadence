using System.Text;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Intelligence;
using Cadence.Domain.Intelligence.Events;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Summaries;

/// <summary>
/// Summarises one meeting's transcript, replacing any existing summary.
/// </summary>
/// <remarks>
/// <para>
/// Run by <c>SummarizeMeetingJob</c>, not by an HTTP request — this takes seconds to minutes.
/// </para>
/// <para>
/// <b>Idempotent by construction.</b> Hangfire delivers at least once, so this <i>will</i> run twice
/// for some meeting. It replaces the summary in place rather than inserting, so a second run leaves
/// one summary rather than two (§14.3).
/// </para>
/// </remarks>
public sealed record GenerateSummaryCommand(Guid MeetingId) : ICommand<Result>;

public sealed class GenerateSummaryHandler(
    ICadenceDbContext context,
    ILlmProvider llm,
    ILogger<GenerateSummaryHandler> logger)
    : ICommandHandler<GenerateSummaryCommand, Result>
{
    public async ValueTask<Result> Handle(
        GenerateSummaryCommand command,
        CancellationToken cancellationToken)
    {
        var meeting = await context.Meetings
            .Include(candidate => candidate.Participants)
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MeetingId, cancellationToken);

        if (meeting is null)
        {
            // Deleted between the job being queued and running. Nothing to fail loudly about.
            logger.LogInformation(
                "Skipped summarising meeting {MeetingId}: it no longer exists",
                command.MeetingId);

            return Result.Success();
        }

        var segments = await context.TranscriptSegments
            .AsNoTracking()
            .Where(segment => segment.MeetingId == meeting.Id)
            .OrderBy(segment => segment.StartMs)
            .ToListAsync(cancellationToken);

        if (segments.Count == 0)
        {
            // No transcript is not a model failure — there is genuinely nothing to summarise. Marked
            // failed all the same, because the alternative leaves the meeting stuck on "generating"
            // forever with nothing to explain why.
            meeting.MarkSummaryFailed();
            await context.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Meeting {MeetingId} has no transcript to summarise",
                meeting.Id);

            return Result.Failure(Error.Failure(
                "summary.no_transcript",
                "That meeting has no transcript to summarise."));
        }

        meeting.MarkSummaryGenerating();
        await context.SaveChangesAsync(cancellationToken);

        MeetingSummaryResult generated;

        try
        {
            generated = await llm.SummariseAsync(
                new SummaryRequest(
                    Render(segments),
                    meeting.Title,
                    [.. meeting.Participants.Select(participant => participant.Name)],
                    OutputLanguage,
                    Detail),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // §23.3, the rule this whole module is built around: a failed summary is recorded as
            // failed. It is never replaced with a placeholder, a template, or text assembled from
            // the transcript to look like a summary — a reader cannot tell those apart from the
            // real thing, which is exactly why they must not exist.
            meeting.MarkSummaryFailed();
            await context.SaveChangesAsync(cancellationToken);

            logger.LogError(
                exception,
                "Summarisation failed for meeting {MeetingId}; marked failed",
                meeting.Id);

            return Result.Failure(Error.Failure(
                "summary.generation_failed",
                "The summary could not be generated. You can try again."));
        }

        if (string.IsNullOrWhiteSpace(generated.Summary))
        {
            // An empty summary would fail the aggregate's own invariant. Caught here so the meeting
            // still lands in a terminal state rather than the exception escaping mid-write.
            meeting.MarkSummaryFailed();
            await context.SaveChangesAsync(cancellationToken);

            return Result.Failure(Error.Failure(
                "summary.empty",
                "The summary could not be generated. You can try again."));
        }

        // Names as spoken in the room, mapped to the people who were actually in it. Case-insensitive
        // because the model echoes back whatever casing the transcript used; duplicates collapse to
        // the first, since a room with two identically-named attendees cannot attribute by name at all.
        var participantsByName = meeting.Participants
            .GroupBy(participant => participant.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First().UserId,
                StringComparer.OrdinalIgnoreCase);

        await PersistAsync(
            meeting.Id,
            meeting.OrganizationId,
            generated,
            segments,
            participantsByName,
            cancellationToken);

        meeting.MarkSummaryReady();
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Summarised meeting {MeetingId}", meeting.Id);

        return Result.Success();
    }

    /// <summary>
    /// Writes the summary, replacing any previous one.
    /// </summary>
    /// <remarks>
    /// Replace-in-place rather than delete-and-insert: the summary keeps its id, so a link to it
    /// from elsewhere still resolves after a regeneration.
    /// </remarks>
    private async Task PersistAsync(
        Guid meetingId,
        Guid organizationId,
        MeetingSummaryResult generated,
        IReadOnlyList<Domain.Meetings.TranscriptSegment> segments,
        IReadOnlyDictionary<string, Guid> participantsByName,
        CancellationToken cancellationToken)
    {
        var existing = await context.AiSummaries
            .Include(summary => summary.Highlights)
            .FirstOrDefaultAsync(summary => summary.MeetingId == meetingId, cancellationToken);

        AiSummary summary;

        if (existing is null)
        {
            summary = AiSummary.Create(
                meetingId,
                organizationId,
                generated.Summary,
                generated.Model,
                generated.KeyPoints);

            await context.AiSummaries.AddAsync(summary, cancellationToken);
        }
        else
        {
            // Replace() clears the old highlights; the ones still tracked have to go with them or
            // EF will try to re-insert orphans pointing at a summary that no longer lists them.
            context.SummaryHighlights.RemoveRange(existing.Highlights);
            existing.Replace(generated.Summary, generated.Model, generated.KeyPoints);
            summary = existing;
        }

        var startsById = segments.ToDictionary(segment => segment.Id, segment => segment.StartMs);

        foreach (var candidate in generated.Highlights)
        {
            // An id the model returned that is not a segment of *this* meeting is dropped to null.
            // A highlight that cites a line from somewhere else is worse than an unattributed one.
            var sourceId = candidate.SourceSegmentId is { } id && startsById.ContainsKey(id)
                ? id
                : (Guid?)null;

            var highlight = summary.AddHighlight(
                ParseKind(candidate.Kind),
                candidate.Text,
                sourceId,
                sourceId is { } resolved ? startsById[resolved] / 1000 : null);

            // Added explicitly: the summary may already be tracked, and a child carrying its own
            // generated key is otherwise detected as an existing row and issued as an UPDATE.
            context.SummaryHighlights.Add(highlight);
        }

        summary.NoteDetectedActionItems(
            Detect(generated.ActionItems, participantsByName, startsById.Keys.ToHashSet()));
    }

    /// <summary>
    /// Turns the model's action-item candidates into the event's shape.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>An assignee is resolved against the meeting's own participants, not the workspace
    /// directory.</b> The model returns a name it read off the transcript, and matching that across
    /// every member would let "Sam" in a meeting Sam never attended assign work to a different Sam
    /// entirely. A name that does not match somebody who was in the room leaves the task
    /// unassigned — the honest answer when nothing establishes who committed to it.
    /// </para>
    /// <para>
    /// The source segment is intersected with this meeting's lines, exactly as a highlight's is: a
    /// citation that does not resolve looks checkable and is not.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<DetectedActionItem> Detect(
        IReadOnlyList<ActionItemCandidate> candidates,
        IReadOnlyDictionary<string, Guid> participantsByName,
        HashSet<Guid> segmentIds) =>
    [
        .. candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Title))
            .Select(candidate => new DetectedActionItem(
                candidate.Title.Trim(),
                candidate.AssigneeName is { } name
                    && participantsByName.TryGetValue(name.Trim(), out var assigneeId)
                        ? assigneeId
                        : null,
                candidate.SourceSegmentId is { } id && segmentIds.Contains(id) ? id : null)),
    ];

    /// <summary>
    /// Renders the transcript with each line's id, so the model can cite them back.
    /// </summary>
    /// <remarks>
    /// The ids are what make a highlight verifiable. Without them the model can only paraphrase, and
    /// nothing links a claim to the moment it came from.
    /// </remarks>
    private static string Render(IReadOnlyList<Domain.Meetings.TranscriptSegment> segments)
    {
        var builder = new StringBuilder();

        foreach (var segment in segments)
        {
            builder.Append('[').Append(segment.Id).Append("] ")
                .Append(segment.SpeakerName).Append(": ")
                .AppendLine(segment.Content);
        }

        return builder.ToString();
    }

    private static SummaryHighlightKind ParseKind(string kind) =>
        Enum.TryParse<SummaryHighlightKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            // The schema constrains this to the four values, so an unknown one means the schema and
            // the enum have drifted. Defaulting keeps the summary usable; the value is not load-bearing.
            : SummaryHighlightKind.Highlight;

    private const string OutputLanguage = "English";

    private const string Detail = "standard";
}
