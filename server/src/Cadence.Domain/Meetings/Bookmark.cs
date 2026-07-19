using Cadence.Domain.Common;

namespace Cadence.Domain.Meetings;

/// <summary>A moment a user flagged while listening, owned by <see cref="Meeting"/>.</summary>
public sealed class Bookmark : Entity
{
    private Bookmark()
    {
        Label = null!;
    }

    private Bookmark(Guid meetingId, int atSeconds, string label)
    {
        MeetingId = meetingId;
        AtSeconds = atSeconds;
        Label = label;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid MeetingId { get; private set; }

    /// <summary>Offset from the start of the recording, in seconds.</summary>
    public int AtSeconds { get; private set; }

    public string Label { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    internal static Bookmark Create(Guid meetingId, int atSeconds, string label)
    {
        var trimmed = label.Trim();
        return new Bookmark(meetingId, atSeconds, trimmed.Length == 0 ? "Bookmark" : trimmed);
    }
}
