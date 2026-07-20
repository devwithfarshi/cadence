using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Meetings.Events;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Summaries;

/// <summary>
/// Starts the processing pipeline when a meeting ends (§14.3).
/// </summary>
/// <remarks>
/// <para>
/// The meeting aggregate knows nothing about summarisation — it raises <see cref="MeetingCompleted"/>
/// and this module decides that a summary should follow. That is what keeps the modules independent:
/// adding transcription or action-item extraction to the pipeline adds handlers here, not branches
/// inside <c>Meeting.Complete</c>.
/// </para>
/// <para>
/// Domain event handlers run <b>after</b> the commit, so the meeting is durably completed before any
/// job is queued. The corollary is that this handler cannot undo the completion — which is precisely
/// why it only queues durable work rather than doing any itself.
/// </para>
/// </remarks>
public sealed class StartPipelineOnMeetingCompleted(
    IJobScheduler jobs,
    ILogger<StartPipelineOnMeetingCompleted> logger)
    : IDomainEventHandler<MeetingCompleted>
{
    public Task HandleAsync(MeetingCompleted domainEvent, CancellationToken cancellationToken = default)
    {
        // Meeting.Complete already set summary_status to Queued for a meeting that actually
        // recorded, so there is nothing to write here — only work to schedule.
        var jobId = jobs.Enqueue<ISummarizeMeetingJob>(
            job => job.RunAsync(domainEvent.MeetingId, domainEvent.OrganizationId));

        logger.LogInformation(
            "Queued summarisation job {JobId} for meeting {MeetingId}",
            jobId,
            domainEvent.MeetingId);

        return Task.CompletedTask;
    }
}
