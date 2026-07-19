using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Library;

/// <summary>
/// A file stored in Cloudinary, with its metadata held here.
/// </summary>
/// <remarks>
/// Cadence stores metadata only — <b>Cloudinary is not the system of record</b> (blueprint §12.2).
/// The row is created after a successful signed direct upload, so a failed upload leaves no
/// phantom document. If an asset later disappears, the row survives and the UI shows a failed state
/// rather than a broken link.
/// </remarks>
public sealed class Document : AggregateRoot, ISoftDeletable, ITenantScoped
{
    private readonly List<string> _tags = [];

    private Document()
    {
        Name = null!;
        StorageKey = null!;
        Url = null!;
        Excerpt = null!;
    }

    private Document(
        Guid organizationId,
        Guid ownerId,
        string name,
        DocumentType type,
        long sizeBytes,
        string storageKey,
        string url)
    {
        OrganizationId = organizationId;
        OwnerId = ownerId;
        Name = name;
        Type = type;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        Url = url;
        ProcessingStatus = ProcessingStatus.Processing;
        Excerpt = "Indexing in progress — content will be searchable shortly.";
    }

    public Guid OrganizationId { get; private set; }

    public Guid OwnerId { get; private set; }

    /// <summary>Set when the document was attached to a meeting.</summary>
    public Guid? MeetingId { get; private set; }

    public string Name { get; private set; }

    public DocumentType Type { get; private set; }

    public long SizeBytes { get; private set; }

    /// <summary>Cloudinary <c>publicId</c> — the handle used to delete or re-sign the asset.</summary>
    public string StorageKey { get; private set; }

    public string Url { get; private set; }

    public ProcessingStatus ProcessingStatus { get; private set; }

    /// <summary>Preview text used by search and the knowledge base cards.</summary>
    public string Excerpt { get; private set; }

    public bool IsFavorite { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public Guid? DeletedBy { get; private set; }

    public IReadOnlyCollection<string> Tags => _tags.AsReadOnly();

    public static Document Register(
        Guid organizationId,
        Guid ownerId,
        string name,
        DocumentType type,
        long sizeBytes,
        string storageKey,
        string url,
        Guid? meetingId = null,
        IEnumerable<string>? tags = null)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "File name is required.");
        DomainException.ThrowIf(sizeBytes < 0, "File size cannot be negative.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(storageKey), "Storage key is required.");

        var document = new Document(organizationId, ownerId, name.Trim(), type, sizeBytes, storageKey, url)
        {
            MeetingId = meetingId,
        };

        document.ReplaceTags(tags ?? []);
        return document;
    }

    /// <summary>Renaming can change the extension, and therefore how the file is categorised.</summary>
    public void Rename(string name, DocumentType type)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "File name cannot be empty.");

        Name = name.Trim();
        Type = type;
    }

    public void MarkIndexed(string excerpt)
    {
        ProcessingStatus = ProcessingStatus.Indexed;
        Excerpt = excerpt.Trim();
    }

    public void MarkFailed() => ProcessingStatus = ProcessingStatus.Failed;

    public void ToggleFavorite() => IsFavorite = !IsFavorite;

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
