using Cadence.Domain.Common;

namespace Cadence.Domain.Chat;

/// <summary>
/// An AI chat thread, scoped to one user within one organization.
/// </summary>
/// <remarks>
/// Messages are part of this aggregate: a turn has no meaning outside its thread, and appending a
/// user question plus its answer must be one transactional unit or the thread can end up with a
/// dangling question.
/// </remarks>
public sealed class Conversation : AggregateRoot, ISoftDeletable
{
    private readonly List<ChatMessage> _messages = [];

    private Conversation()
    {
        Title = null!;
    }

    private Conversation(Guid organizationId, Guid userId, string title)
    {
        OrganizationId = organizationId;
        UserId = userId;
        Title = title;
    }

    public Guid OrganizationId { get; private set; }

    public Guid UserId { get; private set; }

    public string Title { get; private set; }

    /// <summary>
    /// Set when the thread is anchored to a single meeting, which narrows retrieval to that
    /// transcript instead of the whole workspace.
    /// </summary>
    public Guid? MeetingId { get; private set; }

    /// <summary>Drives ordering in the sidebar; creation order is not what users look for.</summary>
    public DateTimeOffset LastMessageAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public IReadOnlyList<ChatMessage> Messages => _messages.AsReadOnly();

    public static Conversation Start(
        Guid organizationId,
        Guid userId,
        string title,
        Guid? meetingId = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "A conversation needs a title.");

        return new Conversation(organizationId, userId, title.Trim())
        {
            MeetingId = meetingId,
            LastMessageAt = DateTimeOffset.UtcNow,
        };
    }

    public ChatMessage Ask(string content)
    {
        var message = ChatMessage.FromUser(Id, content);
        Append(message);
        return message;
    }

    public ChatMessage Answer(string content, IEnumerable<ChatSource>? sources = null)
    {
        DomainException.ThrowIf(
            _messages.Count == 0,
            "A conversation cannot open with an assistant turn.");

        var message = ChatMessage.FromAssistant(Id, content, sources ?? []);
        Append(message);
        return message;
    }

    public void Rename(string title)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Title cannot be empty.");

        Title = title.Trim();
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

    private void Append(ChatMessage message)
    {
        _messages.Add(message);
        LastMessageAt = message.CreatedAt;
    }
}
