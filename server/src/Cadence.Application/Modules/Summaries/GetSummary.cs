using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Summaries;

/// <summary>One meeting's summary, or 404 when it has none.</summary>
public sealed record GetSummaryQuery(Guid MeetingId) : IQuery<Result<AiSummaryDto>>;

/// <summary>
/// Queues a fresh summarisation run for a meeting.
/// </summary>
/// <remarks>
/// The retry the UI offers after a failure, and the way to re-run one that came out poorly. Returns
/// the job id rather than the summary — generation takes minutes and the request cannot wait.
/// </remarks>
public sealed record RegenerateSummaryCommand(Guid MeetingId) : ICommand<Result<JobAcceptedDto>>;

public sealed class GetSummaryHandler(ICadenceDbContext context)
    : IQueryHandler<GetSummaryQuery, Result<AiSummaryDto>>
{
    public async ValueTask<Result<AiSummaryDto>> Handle(
        GetSummaryQuery query,
        CancellationToken cancellationToken)
    {
        // AiSummary is ITenantScoped, so the global filter already scopes this. The meeting is still
        // checked first so a meeting that exists but has no summary is a clean 404 about the summary
        // rather than an empty result the caller has to interpret.
        var visible = await context.Meetings
            .AsNoTracking()
            .AnyAsync(meeting => meeting.Id == query.MeetingId, cancellationToken);

        if (!visible)
        {
            return Result.Failure<AiSummaryDto>(
                Error.NotFound("meeting.not_found", "That meeting could not be found."));
        }

        var summary = await context.AiSummaries
            .AsNoTracking()
            .Where(candidate => candidate.MeetingId == query.MeetingId)
            .Select(candidate => new AiSummaryDto(
                candidate.Id,
                candidate.MeetingId,
                candidate.ExecutiveSummary,
                candidate.KeyPoints.ToList(),
                candidate.Highlights
                    .OrderBy(highlight => highlight.AtSeconds ?? int.MaxValue)
                    .Select(highlight => new SummaryHighlightDto(
                        highlight.Id,
                        highlight.Kind,
                        highlight.Text,
                        highlight.SourceSegmentId,
                        highlight.AtSeconds))
                    .ToList(),
                candidate.Model,
                candidate.GeneratedAt))
            .FirstOrDefaultAsync(cancellationToken);

        return summary is null
            ? Result.Failure<AiSummaryDto>(Error.NotFound(
                "summary.not_found",
                "That meeting has no summary yet."))
            : Result.Success(summary);
    }
}

public sealed class RegenerateSummaryHandler(
    ICadenceDbContext context,
    IJobScheduler jobs)
    : ICommandHandler<RegenerateSummaryCommand, Result<JobAcceptedDto>>
{
    public async ValueTask<Result<JobAcceptedDto>> Handle(
        RegenerateSummaryCommand command,
        CancellationToken cancellationToken)
    {
        var meeting = await context.Meetings
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MeetingId, cancellationToken);

        if (meeting is null)
        {
            return Result.Failure<JobAcceptedDto>(
                Error.NotFound("meeting.not_found", "That meeting could not be found."));
        }

        var hasTranscript = await context.TranscriptSegments
            .AsNoTracking()
            .AnyAsync(segment => segment.MeetingId == meeting.Id, cancellationToken);

        if (!hasTranscript)
        {
            // Refused up front rather than queued to fail. Nothing about waiting changes the answer,
            // and a job that is certain to fail is noise in the dashboard.
            return Result.Failure<JobAcceptedDto>(Error.Conflict(
                "summary.no_transcript",
                "That meeting has no transcript to summarise."));
        }

        // Queued *before* the job is enqueued, and committed first: a job that starts before this
        // write lands would read the old status and could be second-guessed by it.
        meeting.MarkSummaryQueued();
        await context.SaveChangesAsync(cancellationToken);

        var jobId = jobs.Enqueue<ISummarizeMeetingJob>(
            job => job.RunAsync(meeting.Id, meeting.OrganizationId));

        return Result.Success(new JobAcceptedDto(jobId));
    }
}

/// <summary>
/// The summarisation job, as the scheduler sees it.
/// </summary>
/// <remarks>
/// An interface so Application can express "enqueue this" without depending on the job's
/// implementation — Hangfire resolves the concrete type from the container when it runs.
/// </remarks>
public interface ISummarizeMeetingJob
{
    /// <remarks>
    /// <paramref name="organizationId"/> is passed rather than looked up. A background worker has no
    /// caller and therefore no tenant, so the job has to be told which workspace it is acting for —
    /// the alternative is querying with the tenant filter off, which would let a job reach every
    /// workspace instead of one.
    /// </remarks>
    Task RunAsync(Guid meetingId, Guid organizationId);
}
