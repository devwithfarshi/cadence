using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings.Events;

namespace Cadence.Domain.Meetings;

/// <summary>
/// A meeting: the aggregate root that owns its participants, bookmarks and transcript.
/// </summary>
/// <remarks>
/// Ending a meeting raises <see cref="MeetingCompleted"/>, which is what starts the processing
/// pipeline — transcription, then summarisation, then action-item extraction (blueprint §14.3).
/// The meeting itself knows nothing about any of those modules.
/// </remarks>
public sealed class Meeting : AggregateRoot, ISoftDeletable
{
    private readonly List<Participant> _participants = [];
    private readonly List<Bookmark> _bookmarks = [];
    private readonly List<string> _tags = [];

    private Meeting()
    {
        Title = null!;
        Description = null!;
    }

    private Meeting(
        Guid organizationId,
        Guid organizerId,
        string title,
        string description,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        MeetingPlatform platform)
    {
        OrganizationId = organizationId;
        OrganizerId = organizerId;
        Title = title;
        Description = description;
        StartsAt = startsAt;
        EndsAt = endsAt;
        Platform = platform;
        Status = MeetingStatus.Scheduled;
        RecordingStatus = RecordingStatus.NotRecorded;
        SummaryStatus = SummaryStatus.None;
    }

    public Guid OrganizationId { get; private set; }

    public Guid OrganizerId { get; private set; }

    public string Title { get; private set; }

    public string Description { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    /// <summary>Actual recorded length. Zero until the meeting completes.</summary>
    public int DurationSeconds { get; private set; }

    public MeetingStatus Status { get; private set; }

    public RecordingStatus RecordingStatus { get; private set; }

    public SummaryStatus SummaryStatus { get; private set; }

    public MeetingPlatform Platform { get; private set; }

    public string? MeetingUrl { get; private set; }

    public bool IsFavorite { get; private set; }

    public bool IsArchived { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public IReadOnlyCollection<Participant> Participants => _participants.AsReadOnly();

    public IReadOnlyCollection<Bookmark> Bookmarks => _bookmarks.AsReadOnly();

    public IReadOnlyCollection<string> Tags => _tags.AsReadOnly();

    public static Meeting Schedule(
        Guid organizationId,
        Guid organizerId,
        string title,
        string description,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        MeetingPlatform platform,
        IEnumerable<string>? tags = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Title is required.");

        // Mirrors the CHECK constraint on the table (§3.10). Enforced here too so the rule holds
        // even for code paths that never reach the database.
        DomainException.ThrowIf(endsAt <= startsAt, "End time must be after the start time.");

        var meeting = new Meeting(
            organizationId,
            organizerId,
            title.Trim(),
            description.Trim(),
            startsAt,
            endsAt,
            platform);

        meeting.ReplaceTags(tags ?? []);
        meeting.Raise(new MeetingCreated(meeting.Id, organizationId, organizerId));
        return meeting;
    }

    public void UpdateDetails(
        string title,
        string description,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        MeetingPlatform platform,
        IEnumerable<string>? tags = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Title is required.");
        DomainException.ThrowIf(endsAt <= startsAt, "End time must be after the start time.");

        Title = title.Trim();
        Description = description.Trim();
        StartsAt = startsAt;
        EndsAt = endsAt;
        Platform = platform;

        if (tags is not null)
        {
            ReplaceTags(tags);
        }
    }

    public void StartRecording()
    {
        DomainException.ThrowIf(
            Status is MeetingStatus.Completed or MeetingStatus.Cancelled,
            "A finished meeting cannot be recorded.");

        Status = MeetingStatus.Live;
        RecordingStatus = RecordingStatus.Recording;
    }

    public void PauseRecording()
    {
        DomainException.ThrowIf(
            RecordingStatus != RecordingStatus.Recording,
            "Only an active recording can be paused.");

        RecordingStatus = RecordingStatus.Paused;
    }

    public void ResumeRecording()
    {
        DomainException.ThrowIf(
            RecordingStatus != RecordingStatus.Paused,
            "Only a paused recording can be resumed.");

        RecordingStatus = RecordingStatus.Recording;
    }

    /// <summary>
    /// Ends the meeting and starts the processing pipeline.
    /// </summary>
    public void Complete(int durationSeconds)
    {
        DomainException.ThrowIf(durationSeconds < 0, "Duration cannot be negative.");
        DomainException.ThrowIf(Status == MeetingStatus.Completed, "Meeting is already completed.");

        DurationSeconds = durationSeconds;
        Status = MeetingStatus.Completed;

        // Only a meeting that actually recorded something has a transcript to process.
        if (RecordingStatus is RecordingStatus.Recording or RecordingStatus.Paused)
        {
            RecordingStatus = RecordingStatus.Recorded;
            SummaryStatus = SummaryStatus.Queued;
        }

        Raise(new MeetingCompleted(Id, OrganizationId, OrganizerId, DurationSeconds));
    }

    public void Cancel()
    {
        DomainException.ThrowIf(Status == MeetingStatus.Completed, "A completed meeting cannot be cancelled.");
        Status = MeetingStatus.Cancelled;
    }

    public void MarkSummaryGenerating() => SummaryStatus = SummaryStatus.Generating;

    public void MarkSummaryReady() => SummaryStatus = SummaryStatus.Ready;

    /// <summary>Terminal failure. Surfaced in the UI with a retry — never replaced by invented content.</summary>
    public void MarkSummaryFailed() => SummaryStatus = SummaryStatus.Failed;

    public void MarkRecordingFailed() => RecordingStatus = RecordingStatus.Failed;

    public void ToggleFavorite() => IsFavorite = !IsFavorite;

    public void SetArchived(bool archived) => IsArchived = archived;

    public void SetMeetingUrl(string? url) => MeetingUrl = url;

    public Participant AddParticipant(Guid userId, string name, string email, ParticipantRole role)
    {
        DomainException.ThrowIf(
            _participants.Any(participant => participant.UserId == userId),
            "That person is already a participant.");

        var participant = Participant.Create(Id, userId, name, email, role);
        _participants.Add(participant);
        return participant;
    }

    public void RemoveParticipant(Guid userId)
    {
        var participant = _participants.FirstOrDefault(candidate => candidate.UserId == userId);
        if (participant is not null)
        {
            _participants.Remove(participant);
        }
    }

    /// <summary>
    /// Records how much of the meeting each participant spoke, as a ratio of the whole.
    /// </summary>
    /// <remarks>
    /// Set once from the finished transcript rather than accumulated live, so the ratios always
    /// sum to 1 and the speaker-distribution chart cannot drift.
    /// </remarks>
    public void SetTalkTime(IReadOnlyDictionary<Guid, double> secondsByUser)
    {
        var total = secondsByUser.Values.Sum();
        if (total <= 0)
        {
            return;
        }

        foreach (var participant in _participants)
        {
            var spoken = secondsByUser.TryGetValue(participant.UserId, out var value) ? value : 0;
            participant.SetTalkTimeRatio(spoken / total);
        }
    }

    public Bookmark AddBookmark(int atSeconds, string label)
    {
        DomainException.ThrowIf(atSeconds < 0, "Bookmark position cannot be negative.");

        var bookmark = Bookmark.Create(Id, atSeconds, label);
        _bookmarks.Add(bookmark);
        return bookmark;
    }

    public void MarkDeleted(DateTimeOffset deletedAt, Guid? deletedBy)
    {
        DeletedAt = deletedAt;
        DeletedBy = deletedBy;
    }

    public void Restore()
    {
        DeletedAt = null;
        DeletedBy = null;
    }

    private void ReplaceTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        _tags.AddRange(
            tags.Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct());
    }
}
