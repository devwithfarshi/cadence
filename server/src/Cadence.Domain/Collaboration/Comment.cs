using Cadence.Domain.Common;

namespace Cadence.Domain.Collaboration;

/// <summary>
/// A comment on a meeting. One level of threading: a comment is either top-level or a reply.
/// </summary>
/// <remarks>
/// Deliberately not arbitrarily deep. Unbounded nesting needs recursive queries and produces a UI
/// nobody can follow; one level covers the actual use — someone responds to a point.
/// </remarks>
public sealed class Comment : AggregateRoot, ISoftDeletable
{
    private readonly List<Guid> _mentions = [];

    private Comment()
    {
        Body = null!;
    }

    private Comment(Guid meetingId, Guid organizationId, Guid authorId, string body)
    {
        MeetingId = meetingId;
        OrganizationId = organizationId;
        AuthorId = authorId;
        Body = body;
    }

    public Guid MeetingId { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; }

    /// <summary>Null for a top-level comment.</summary>
    public Guid? ParentId { get; private set; }

    /// <summary>Optional anchor into the recording, so a comment can point at a moment.</summary>
    public int? AtSeconds { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    /// <summary>User ids referenced with @ in the body; each one gets a notification.</summary>
    public IReadOnlyCollection<Guid> Mentions => _mentions.AsReadOnly();

    public static Comment Create(
        Guid meetingId,
        Guid organizationId,
        Guid authorId,
        string body,
        Guid? parentId = null,
        int? atSeconds = null,
        IEnumerable<Guid>? mentions = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(body), "Comment cannot be empty.");

        var comment = new Comment(meetingId, organizationId, authorId, body.Trim())
        {
            ParentId = parentId,
            AtSeconds = atSeconds,
        };

        if (mentions is not null)
        {
            comment._mentions.AddRange(mentions.Distinct());
        }

        return comment;
    }

    public void Edit(string body, IEnumerable<Guid> mentions)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(body), "Comment cannot be empty.");

        Body = body.Trim();
        _mentions.Clear();
        _mentions.AddRange(mentions.Distinct());
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
}
