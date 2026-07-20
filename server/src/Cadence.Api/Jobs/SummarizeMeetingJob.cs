using Cadence.Api.Common;
using Cadence.Application.Modules.Summaries;
using Hangfire;
using Mediator;

namespace Cadence.Api.Jobs;

/// <summary>
/// The Hangfire entry point for summarisation (§14.3).
/// </summary>
/// <remarks>
/// <para>
/// It lives in the Api layer for one reason: this is where <c>ICurrentUser</c> is implemented, and
/// therefore where the mechanism for giving non-request work an identity lives. A job that resolved
/// <c>ISender</c> without staging a principal would run every query against
/// <see cref="Guid.Empty"/> and quietly find nothing — the same trap the SignalR hub and the
/// transcript flush both hit.
/// </para>
/// <para>
/// Retries and backoff are Hangfire's (§14.3): three attempts at 10s → 1min → 5min. After the last
/// one the handler has already recorded <c>summary_status = failed</c> on every attempt, so the
/// meeting ends in a terminal state the UI can offer a retry from — it never fails silently.
/// </para>
/// </remarks>
public sealed class SummarizeMeetingJob(
    ISender sender,
    ScopedPrincipal principal,
    ILogger<SummarizeMeetingJob> logger)
    : ISummarizeMeetingJob
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 60, 300])]
    public async Task RunAsync(Guid meetingId, Guid organizationId)
    {
        // The workspace the meeting belongs to, established when the job was queued by someone who
        // could see it. Staged here so the tenant filter still applies — the alternative,
        // IgnoreQueryFilters inside the handler, would give a background job reach over every tenant.
        principal.Principal = ScopedPrincipal.ForOrganization(organizationId);

        var result = await sender.Send(new GenerateSummaryCommand(meetingId));

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Summarisation of meeting {MeetingId} failed: {Error}",
                meetingId,
                result.Error.Description);

            // Thrown so Hangfire records the attempt as failed and applies the retry policy. The
            // handler has already written the terminal state, so a give-up leaves the meeting
            // marked failed rather than stuck mid-pipeline.
            throw new InvalidOperationException(result.Error.Description);
        }
    }
}
