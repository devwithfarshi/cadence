using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Transcripts;

/// <summary>
/// One meeting's transcript, in playback order.
/// </summary>
/// <remarks>
/// <paramref name="Search"/> filters to matching lines rather than paging: the transcript view is a
/// scrollable document with a find-in-page box, and a paged search result would break the ability to
/// click a line and seek to it in context.
/// </remarks>
public sealed record GetTranscriptQuery(Guid MeetingId, string? Search)
    : IQuery<Result<IReadOnlyList<TranscriptSegmentDto>>>;

public sealed class GetTranscriptHandler(ICadenceDbContext context)
    : IQueryHandler<GetTranscriptQuery, Result<IReadOnlyList<TranscriptSegmentDto>>>
{
    public async ValueTask<Result<IReadOnlyList<TranscriptSegmentDto>>> Handle(
        GetTranscriptQuery query,
        CancellationToken cancellationToken)
    {
        // The meeting is checked first, and through the tenant-filtered set. TranscriptSegment is
        // *not* ITenantScoped — it hangs off the meeting and dies with it — so querying segments by
        // meeting id alone would happily return another workspace's transcript to anyone who
        // guessed a meeting id.
        var visible = await context.Meetings
            .AsNoTracking()
            .AnyAsync(meeting => meeting.Id == query.MeetingId, cancellationToken);

        if (!visible)
        {
            return Result.Failure<IReadOnlyList<TranscriptSegmentDto>>(
                Error.NotFound("meeting.not_found", "That meeting could not be found."));
        }

        var segments = context.TranscriptSegments
            .AsNoTracking()
            .Where(segment => segment.MeetingId == query.MeetingId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLowerInvariant();
            segments = segments.Where(segment => segment.Content.ToLower().Contains(term));
        }

        var results = await segments
            .OrderBy(segment => segment.StartMs)
            .ThenBy(segment => segment.Id)
            .Select(segment => new TranscriptSegmentDto(
                segment.Id,
                segment.MeetingId,
                segment.SpeakerId,
                segment.SpeakerName,
                segment.StartMs / 1000d,
                segment.EndMs / 1000d,
                segment.Content,
                segment.Confidence,
                segment.IsActionItem))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<TranscriptSegmentDto>>(results);
    }
}
