using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Chat;

/// <summary>
/// One turn in a conversation. Belongs to the <see cref="Conversation"/> aggregate.
/// </summary>
public sealed class ChatMessage : Entity
{
    private readonly List<ChatSource> _sources = [];

    private ChatMessage()
    {
        Content = null!;
    }

    private ChatMessage(Guid conversationId, ChatRole role, string content)
    {
        ConversationId = conversationId;
        Role = role;
        Content = content;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid ConversationId { get; private set; }

    public ChatRole Role { get; private set; }

    public string Content { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Only ever populated for an assistant turn.</summary>
    public IReadOnlyCollection<ChatSource> Sources => _sources.AsReadOnly();

    internal static ChatMessage FromUser(Guid conversationId, string content)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(content), "A message cannot be empty.");

        return new ChatMessage(conversationId, ChatRole.User, content.Trim());
    }

    internal static ChatMessage FromAssistant(
        Guid conversationId,
        string content,
        IEnumerable<ChatSource> sources)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(content), "An answer cannot be empty.");

        var message = new ChatMessage(conversationId, ChatRole.Assistant, content.Trim());
        message._sources.AddRange(sources);
        return message;
    }
}
