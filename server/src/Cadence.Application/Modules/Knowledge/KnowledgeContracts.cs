using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Knowledge;

/// <summary>
/// A knowledge base entry, as the Knowledge page renders it.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>KnowledgeItem</c> shape 1:1 (§6). <c>SourceId</c> points at the meeting or
/// document the entry was drawn from; <c>SourceUrl</c> is the external address a link entry opens.
/// They are alternatives rather than a pair — an entry is either about something in this workspace or
/// about something outside it.
/// </remarks>
public sealed record KnowledgeItemDto(
    Guid Id,
    string Title,
    KnowledgeItemKind Kind,
    string Category,
    string Excerpt,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    Guid OwnerId,
    Guid? SourceId,
    string? SourceUrl,
    DateTimeOffset? LastOpenedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// A new entry.
/// </summary>
/// <remarks>
/// There is deliberately no <c>ownerId</c>. The client's mock takes one because it has no session to
/// read; here it comes from the token, so an entry cannot be filed under someone else's name.
/// </remarks>
public sealed record CreateKnowledgeItemRequest(
    string Title,
    KnowledgeItemKind Kind,
    string? Category,
    string? Excerpt,
    Guid? SourceId,
    string? SourceUrl,
    IReadOnlyList<string>? Tags);

/// <summary>
/// The values behind the category and tag filter menus.
/// </summary>
/// <remarks>
/// Both come from the entries that exist rather than from a fixed vocabulary, so a category stops
/// being offered once nothing is filed under it. Computed in one round trip for the same reason the
/// task counts are: two queries see two slightly different databases.
/// </remarks>
public sealed record KnowledgeFacetsDto(
    IReadOnlyList<string> Categories,
    IReadOnlyList<string> Tags);
