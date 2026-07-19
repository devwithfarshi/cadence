using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Chat;

/// <summary>
/// A citation backing an assistant answer.
/// </summary>
/// <remarks>
/// Every citation must resolve to a real record — an answer that cites nothing verifiable is worse
/// than one that admits it does not know (blueprint §23.3). Stored as jsonb on the message rather
/// than as rows: citations are only ever read with their message, and a foreign key would block
/// deleting the meeting or document they point at.
/// </remarks>
public sealed class ChatSource : ValueObject
{
    private ChatSource(Guid sourceId, ChatSourceKind kind, string label, string href)
    {
        SourceId = sourceId;
        Kind = kind;
        Label = label;
        Href = href;
    }

    /// <summary>Id of the cited meeting, document or knowledge item.</summary>
    public Guid SourceId { get; }

    public ChatSourceKind Kind { get; }

    /// <summary>Display text, captured at answer time so the citation survives a later rename.</summary>
    public string Label { get; }

    public string Href { get; }

    public static ChatSource Create(Guid sourceId, ChatSourceKind kind, string label, string href)
    {
        DomainException.ThrowIf(sourceId == Guid.Empty, "A citation must point at a real record.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(label), "A citation needs a label.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(href), "A citation needs a link.");

        return new ChatSource(sourceId, kind, label.Trim(), href.Trim());
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return SourceId;
        yield return Kind;
        yield return Label;
        yield return Href;
    }
}
