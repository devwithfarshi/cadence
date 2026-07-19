using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Meetings;

/// <summary>
/// Someone who attended a meeting, owned by <see cref="Meeting"/>.
/// </summary>
/// <remarks>
/// <see cref="Name"/> and <see cref="Email"/> are copied rather than joined from the user
/// (blueprint §3.9). A meeting is a historical record: if someone later changes their display
/// name, the meeting must still read as it did at the time.
/// </remarks>
public sealed class Participant : Entity
{
    private Participant()
    {
        Name = null!;
        Email = null!;
    }

    private Participant(Guid meetingId, Guid userId, string name, string email, ParticipantRole role)
    {
        MeetingId = meetingId;
        UserId = userId;
        Name = name;
        Email = email;
        Role = role;
        Attended = true;
    }

    public Guid MeetingId { get; private set; }

    public Guid UserId { get; private set; }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public ParticipantRole Role { get; private set; }

    /// <summary>Share of total speaking time, 0–1. Drives the speaker-distribution chart.</summary>
    public double TalkTimeRatio { get; private set; }

    public bool Attended { get; private set; }

    internal static Participant Create(
        Guid meetingId,
        Guid userId,
        string name,
        string email,
        ParticipantRole role) => new(meetingId, userId, name, email, role);

    internal void SetTalkTimeRatio(double ratio)
    {
        DomainException.ThrowIf(ratio is < 0 or > 1, "Talk-time ratio must be between 0 and 1.");
        TalkTimeRatio = ratio;
    }

    public void MarkAbsent() => Attended = false;
}
