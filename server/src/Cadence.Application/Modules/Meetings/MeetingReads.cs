using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Meetings;

/// <summary>
/// The single read path for meetings.
/// </summary>
/// <remarks>
/// Every endpoint that returns a meeting — list, detail, create, update, duplicate, favourite —
/// comes through here. A command that mapped its own response would eventually disagree with the
/// list about the same row, and the disagreement would show up as a value that changes when the
/// page is refreshed.
/// </remarks>
internal static class MeetingReads
{
    /// <summary>The property name of the tag collection's backing field.</summary>
    /// <remarks>
    /// <c>Meeting.Tags</c> is <c>Ignore</c>d in the EF configuration — the mapped member is the
    /// private field — so a query has to name the field. Referencing <c>meeting.Tags</c> here
    /// compiles and then fails at translation time.
    /// </remarks>
    private const string TagsField = "_tags";

    public static async Task<Result<MeetingDetailDto>> LoadDetailAsync(
        ICadenceDbContext context,
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        // Filtered before projecting, never after. A predicate over the projected DTO has to be
        // translated against a constructor call, which EF cannot do — it fails at runtime with an
        // untranslatable-expression error rather than at compile time.
        var meeting = await Project(
                context.Meetings.AsNoTracking().Where(row => row.Id == meetingId))
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
        {
            return Result.Failure<MeetingDetailDto>(NotFound);
        }

        var bookmarks = await context.Bookmarks
            .AsNoTracking()
            .Where(bookmark => bookmark.MeetingId == meetingId)
            .OrderBy(bookmark => bookmark.AtSeconds)
            .Select(bookmark => new BookmarkDto(
                bookmark.Id,
                bookmark.AtSeconds,
                bookmark.Label,
                bookmark.CreatedAt))
            .ToListAsync(cancellationToken);

        var resolved = await ResolveAvatarsAsync(context, [meeting], cancellationToken);

        return Result.Success(new MeetingDetailDto(resolved[0], bookmarks));
    }

    public static async Task<Result<MeetingSummaryDto>> LoadSummaryAsync(
        ICadenceDbContext context,
        Guid meetingId,
        CancellationToken cancellationToken)
    {
        var meeting = await Project(
                context.Meetings.AsNoTracking().Where(row => row.Id == meetingId))
            .FirstOrDefaultAsync(cancellationToken);

        if (meeting is null)
        {
            return Result.Failure<MeetingSummaryDto>(NotFound);
        }

        var resolved = await ResolveAvatarsAsync(context, [meeting], cancellationToken);

        return Result.Success(resolved[0]);
    }

    public static Error NotFound =>
        Error.NotFound("meeting.not_found", "That meeting could not be found.");

    /// <summary>
    /// Projects straight to the response shape, in one round trip.
    /// </summary>
    /// <remarks>
    /// A projection rather than <c>Include</c>: the list needs sixteen columns and its participants,
    /// not the whole aggregate and its bookmarks and its transcript. Loading entities and mapping
    /// afterwards is how a list endpoint quietly becomes the slowest thing in the application.
    /// <para>
    /// <c>AvatarUrl</c> is left null here and filled in by
    /// <see cref="ResolveAvatarsAsync"/> — see the note there.
    /// </para>
    /// </remarks>
    public static IQueryable<MeetingSummaryDto> Project(IQueryable<Meeting> source) =>
        source.Select(meeting => new MeetingSummaryDto(
            meeting.Id,
            meeting.Title,
            meeting.Description,
            meeting.StartsAt,
            meeting.EndsAt,
            meeting.DurationSeconds,
            meeting.Status,
            meeting.RecordingStatus,
            meeting.SummaryStatus,
            meeting.Platform,
            meeting.MeetingUrl,
            meeting.OrganizerId,
            meeting.Participants
                .OrderBy(participant => participant.Name)
                .Select(participant => new ParticipantDto(
                    participant.UserId,
                    participant.Name,
                    participant.Email,
                    null,
                    participant.Role,
                    participant.TalkTimeRatio,
                    participant.Attended))
                .ToList(),
            EF.Property<List<string>>(meeting, TagsField),
            meeting.IsFavorite,
            meeting.IsArchived,
            meeting.CreatedAt,
            meeting.UpdatedAt));

    /// <summary>
    /// Fills in participant avatars with one query for the whole page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The avatar is the one participant field that is <i>not</i> a historical copy. Name and email
    /// are frozen at the time of the meeting on purpose (§3.9); an avatar URL is a link to a file
    /// that gets replaced, so a frozen one eventually points at nothing. It is therefore read live.
    /// </para>
    /// <para>
    /// Deliberately a second query keyed by the distinct user ids on the page, not a correlated
    /// subquery inside the projection — that translates to a lateral join per participant per row,
    /// which is the N+1 this method exists to avoid. Twenty meetings with eight attendees each is
    /// two queries, not a hundred and sixty.
    /// </para>
    /// </remarks>
    public static async Task<List<MeetingSummaryDto>> ResolveAvatarsAsync(
        ICadenceDbContext context,
        IReadOnlyList<MeetingSummaryDto> meetings,
        CancellationToken cancellationToken)
    {
        var userIds = meetings
            .SelectMany(meeting => meeting.Participants)
            .Select(participant => participant.UserId)
            .Distinct()
            .ToList();

        if (userIds.Count == 0)
        {
            return [.. meetings];
        }

        // IgnoreQueryFilters because User is global — it carries no tenant filter of its own, and
        // this only ever asks about ids already present on rows the caller can see.
        var avatars = await context.Users
            .AsNoTracking()
            .IgnoreQueryFilters()
            .Where(user => userIds.Contains(user.Id))
            .Select(user => new { user.Id, user.AvatarUrl })
            .ToDictionaryAsync(user => user.Id, user => user.AvatarUrl, cancellationToken);

        return
        [
            .. meetings.Select(meeting => meeting with
            {
                Participants =
                [
                    .. meeting.Participants.Select(participant => participant with
                    {
                        AvatarUrl = avatars.GetValueOrDefault(participant.UserId),
                    }),
                ],
            }),
        ];
    }

    /// <summary>
    /// Applies the filters shared by the meetings list and the history view.
    /// </summary>
    /// <remarks>
    /// No tenant predicate: <c>Meeting</c> is <c>ITenantScoped</c>, so the global filter has already
    /// scoped this. Writing one by hand is how the filter eventually gets relied on somewhere that
    /// forgot it (§3.3).
    /// </remarks>
    public static IQueryable<Meeting> Filtered(ICadenceDbContext context, MeetingQuery query)
    {
        var meetings = context.Meetings.AsNoTracking();

        if (!query.IncludeArchived)
        {
            meetings = meetings.Where(meeting => !meeting.IsArchived);
        }

        if (query.FavoritesOnly)
        {
            meetings = meetings.Where(meeting => meeting.IsFavorite);
        }

        if (query.Status is { Count: > 0 })
        {
            meetings = meetings.Where(meeting => query.Status.Contains(meeting.Status));
        }

        if (query.Platform is { Count: > 0 })
        {
            meetings = meetings.Where(meeting => query.Platform.Contains(meeting.Platform));
        }

        if (query.SummaryStatus is { Count: > 0 })
        {
            meetings = meetings.Where(meeting => query.SummaryStatus.Contains(meeting.SummaryStatus));
        }

        if (query.Tags is { Count: > 0 })
        {
            // Any-of, matching the client's filter menu. The GIN index on `tags` is what keeps this
            // from being a scan.
            meetings = meetings.Where(meeting =>
                EF.Property<List<string>>(meeting, TagsField).Any(tag => query.Tags.Contains(tag)));
        }

        if (query.ParticipantId is { } participantId)
        {
            meetings = meetings.Where(meeting =>
                meeting.Participants.Any(participant => participant.UserId == participantId));
        }

        if (query.From is { } from)
        {
            meetings = meetings.Where(meeting => meeting.StartsAt >= from);
        }

        if (query.To is { } to)
        {
            meetings = meetings.Where(meeting => meeting.StartsAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // lower() + Contains rather than Npgsql's ILike: Application is provider-neutral and an
            // architecture test fails the build if Npgsql appears here.
            var term = query.Search.Trim().ToLowerInvariant();

            meetings = meetings.Where(meeting =>
                meeting.Title.ToLower().Contains(term)
                || meeting.Description.ToLower().Contains(term)
                || EF.Property<List<string>>(meeting, TagsField).Any(tag => tag.Contains(term))
                || meeting.Participants.Any(participant => participant.Name.ToLower().Contains(term)));
        }

        return meetings;
    }

    /// <summary>
    /// Orders the result, defaulting to most recent first.
    /// </summary>
    /// <remarks>
    /// Every sort ends on <c>Id</c>. Without a tiebreaker, rows with equal sort keys have no defined
    /// order between them, so the same row can appear on page 1 and again on page 2 while another is
    /// never shown at all — a paging bug that only appears with duplicate values in the data.
    /// </remarks>
    public static IQueryable<Meeting> Sorted(IQueryable<Meeting> meetings, MeetingQuery query)
    {
        var ascending = query.SortDir == SortDirection.Asc;

        return query.SortBy?.ToLowerInvariant() switch
        {
            "title" => ascending
                ? meetings.OrderBy(meeting => meeting.Title).ThenBy(meeting => meeting.Id)
                : meetings.OrderByDescending(meeting => meeting.Title).ThenBy(meeting => meeting.Id),

            "durationseconds" => ascending
                ? meetings.OrderBy(meeting => meeting.DurationSeconds).ThenBy(meeting => meeting.Id)
                : meetings.OrderByDescending(meeting => meeting.DurationSeconds).ThenBy(meeting => meeting.Id),

            "participants" => ascending
                ? meetings.OrderBy(meeting => meeting.Participants.Count).ThenBy(meeting => meeting.Id)
                : meetings.OrderByDescending(meeting => meeting.Participants.Count).ThenBy(meeting => meeting.Id),

            _ => ascending
                ? meetings.OrderBy(meeting => meeting.StartsAt).ThenBy(meeting => meeting.Id)
                : meetings.OrderByDescending(meeting => meeting.StartsAt).ThenBy(meeting => meeting.Id),
        };
    }

    /// <summary>Runs the count, the page and the avatar resolve for a filtered query.</summary>
    public static async Task<PagedResult<MeetingSummaryDto>> PageAsync(
        ICadenceDbContext context,
        MeetingQuery query,
        CancellationToken cancellationToken)
    {
        var filtered = Filtered(context, query);

        var total = await filtered.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<MeetingSummaryDto>.Empty(query.Page, query.PageSize);
        }

        var page = await Project(Sorted(filtered, query))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        var resolved = await ResolveAvatarsAsync(context, page, cancellationToken);

        return new PagedResult<MeetingSummaryDto>(resolved, total, query.Page, query.PageSize);
    }
}
