using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Meetings;

/// <summary>
/// A meeting as the list, calendar and dashboard render it.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>Meeting</c> shape (§6) with one omission: <b>bookmarks are not here</b>.
/// They are only ever read on the detail page, and carrying every bookmark of every row would grow
/// a list response without a screen to show it on. <see cref="MeetingDetailDto"/> supplies them.
/// </remarks>
public sealed record MeetingSummaryDto(
    Guid Id,
    string Title,
    string Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int DurationSeconds,
    MeetingStatus Status,
    RecordingStatus RecordingStatus,
    SummaryStatus SummaryStatus,
    MeetingPlatform Platform,
    string? MeetingUrl,
    Guid OrganizerId,
    IReadOnlyList<ParticipantDto> Participants,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    bool IsArchived,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A meeting with the parts only its own page needs.</summary>
public sealed record MeetingDetailDto(
    MeetingSummaryDto Meeting,
    IReadOnlyList<BookmarkDto> Bookmarks);

/// <summary>
/// Someone who attended.
/// </summary>
/// <remarks>
/// <c>Name</c> and <c>Email</c> are the values copied onto the meeting when it was created, not a
/// live join against the user (§3.9). A meeting is a historical record: renaming yourself today must
/// not rewrite who was in a meeting last year.
/// </remarks>
public sealed record ParticipantDto(
    Guid UserId,
    string Name,
    string Email,
    string? AvatarUrl,
    ParticipantRole Role,
    double TalkTimeRatio,
    bool Attended);

public sealed record BookmarkDto(
    Guid Id,
    int AtSeconds,
    string Label,
    DateTimeOffset CreatedAt);

public sealed record CreateMeetingRequest(
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    MeetingPlatform Platform,
    IReadOnlyList<Guid> ParticipantIds,
    IReadOnlyList<string>? Tags);

/// <summary>
/// The editable parts of a meeting.
/// </summary>
/// <remarks>
/// Status, recording status and summary status are absent. Those are driven by what actually
/// happened — a recording that ran, a pipeline that finished — and letting a client assert
/// <c>summaryStatus = ready</c> on a meeting with no summary would make the field meaningless.
/// </remarks>
public sealed record UpdateMeetingRequest(
    string Title,
    string? Description,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    MeetingPlatform Platform,
    IReadOnlyList<Guid>? ParticipantIds,
    IReadOnlyList<string>? Tags,
    string? MeetingUrl);

public sealed record AddBookmarkRequest(int AtSeconds, string Label);

public sealed record ArchiveRequest(IReadOnlyList<Guid> Ids, bool Archived);
