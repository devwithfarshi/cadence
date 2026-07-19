using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Library;

/// <summary>
/// A curated entry in the knowledge base: a note, a summary, a document pointer or a link.
/// </summary>
public sealed class KnowledgeItem : AggregateRoot, ISoftDeletable, ITenantScoped
{
    private readonly List<string> _tags = [];

    private KnowledgeItem()
    {
        Title = null!;
        Category = null!;
        Excerpt = null!;
    }

    private KnowledgeItem(
        Guid organizationId,
        Guid ownerId,
        string title,
        KnowledgeItemKind kind,
        string category,
        string excerpt)
    {
        OrganizationId = organizationId;
        OwnerId = ownerId;
        Title = title;
        Kind = kind;
        Category = category;
        Excerpt = excerpt;
    }

    public Guid OrganizationId { get; private set; }

    public Guid OwnerId { get; private set; }

    public string Title { get; private set; }

    public KnowledgeItemKind Kind { get; private set; }

    public string Category { get; private set; }

    public string Excerpt { get; private set; }

    /// <summary>The meeting or document this surfaces, when it points at one.</summary>
    public Guid? SourceId { get; private set; }

    /// <summary>Set only for <see cref="KnowledgeItemKind.Link"/> entries.</summary>
    public string? SourceUrl { get; private set; }

    public bool IsFavorite { get; private set; }

    /// <summary>Drives the "recently opened" rail, so it reflects real usage rather than creation order.</summary>
    public DateTimeOffset? LastOpenedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public IReadOnlyCollection<string> Tags => _tags.AsReadOnly();

    public static KnowledgeItem Create(
        Guid organizationId,
        Guid ownerId,
        string title,
        KnowledgeItemKind kind,
        string category,
        string excerpt,
        Guid? sourceId = null,
        string? sourceUrl = null,
        IEnumerable<string>? tags = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(title), "Title is required.");
        DomainException.ThrowIf(
            kind == KnowledgeItemKind.Link && string.IsNullOrWhiteSpace(sourceUrl),
            "A link entry needs a URL.");

        var item = new KnowledgeItem(
            organizationId,
            ownerId,
            title.Trim(),
            kind,
            string.IsNullOrWhiteSpace(category) ? "Uncategorised" : category.Trim(),
            excerpt.Trim())
        {
            SourceId = sourceId,
            SourceUrl = sourceUrl?.Trim(),
        };

        item.ReplaceTags(tags ?? []);
        return item;
    }

    public void ToggleFavorite() => IsFavorite = !IsFavorite;

    public void MarkOpened() => LastOpenedAt = DateTimeOffset.UtcNow;

    public void ReplaceTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        _tags.AddRange(
            tags.Select(tag => tag.Trim().ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct());
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
