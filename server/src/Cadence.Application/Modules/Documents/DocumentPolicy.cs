using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Documents;

/// <summary>
/// What the library will accept, where it puts it, and how a stored file is classified.
/// </summary>
/// <remarks>
/// These are rules about the <i>library</i>, not about Cloudinary, which is why they sit in the
/// module rather than in the adapter's configuration. A second storage adapter should enforce the
/// same ceiling and the same allow-list; one that could not would be a different product.
/// </remarks>
internal static class DocumentPolicy
{
    /// <summary>
    /// Largest file the API will sign for.
    /// </summary>
    /// <remarks>
    /// <b>The signature cannot enforce this.</b> Cloudinary has no signed "maximum bytes" parameter,
    /// so a client that understates the size still receives a usable signature. The declared size is
    /// checked here to refuse the honest mistake before a long transfer starts; the size the provider
    /// reports at registration is the check that actually binds (§12.1).
    /// </remarks>
    public const long MaxUploadBytes = 100L * 1024 * 1024;

    /// <summary>
    /// How long a download link stays usable.
    /// </summary>
    /// <remarks>
    /// Long enough for a browser to start the transfer, short enough that a URL pasted into a chat
    /// is dead by the time anyone clicks it (§12.3).
    /// </remarks>
    public static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// File extension → the type the Documents page groups by.
    /// </summary>
    /// <remarks>
    /// This doubles as the format allow-list §12.3 asks for: an extension that is not a key here is
    /// refused. Deliberately conservative — nothing executable, nothing archived. It mirrors the
    /// client's <c>inferType</c> so a file lands in the same bucket on both sides of the wire.
    /// </remarks>
    private static readonly Dictionary<string, DocumentType> TypesByExtension =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["pdf"] = DocumentType.Pdf,
            ["doc"] = DocumentType.Docx,
            ["docx"] = DocumentType.Docx,
            ["ppt"] = DocumentType.Pptx,
            ["pptx"] = DocumentType.Pptx,
            ["csv"] = DocumentType.Csv,
            ["txt"] = DocumentType.Txt,
            ["md"] = DocumentType.Txt,
            ["png"] = DocumentType.Image,
            ["jpg"] = DocumentType.Image,
            ["jpeg"] = DocumentType.Image,
            ["gif"] = DocumentType.Image,
            ["webp"] = DocumentType.Image,
            ["svg"] = DocumentType.Image,
        };

    /// <summary>
    /// The content types a client may declare.
    /// </summary>
    /// <remarks>
    /// A courtesy check, and worth being honest about: the client chooses this string, so agreeing
    /// with it proves nothing about the bytes. It catches a misconfigured uploader before the
    /// transfer rather than after. The binding check is the format the provider reports once the file
    /// has landed.
    /// </remarks>
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-powerpoint",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "text/csv",
        "text/plain",
        "text/markdown",
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp",
        "image/svg+xml",
    };

    public static IEnumerable<string> AllowedExtensions => TypesByExtension.Keys.Order();

    /// <summary>The extension without its dot, lowercased. Empty when the name has none.</summary>
    public static string ExtensionOf(string fileName)
    {
        var extension = Path.GetExtension(fileName);

        return extension.Length > 1 ? extension[1..].ToLowerInvariant() : string.Empty;
    }

    public static bool IsAllowedFileName(string fileName) =>
        TypesByExtension.ContainsKey(ExtensionOf(fileName));

    public static bool IsAllowedContentType(string contentType) =>
        AllowedContentTypes.Contains(contentType.Split(';')[0].Trim());

    /// <summary>
    /// The type a file name implies, mirroring the client's <c>inferType</c>.
    /// </summary>
    /// <remarks>
    /// Falls back to <see cref="DocumentType.Txt"/> for the same reason the client does — a rename
    /// can strip an extension, and a document with no type is not a state the page can render.
    /// </remarks>
    public static DocumentType TypeOf(string fileName) =>
        TypesByExtension.GetValueOrDefault(ExtensionOf(fileName), DocumentType.Txt);

    /// <summary>
    /// The tenant-first folder an upload belongs in (§12.2).
    /// </summary>
    /// <remarks>
    /// Organization first, so exporting or purging one workspace is a prefix operation rather than a
    /// query. Dated below that, so a folder listing stays navigable once a workspace has years of
    /// files in it. The adapter prefixes its own deployment root.
    /// </remarks>
    public static string FolderFor(Guid organizationId, DateTimeOffset now) =>
        $"{organizationId}/documents/{now:yyyy}/{now:MM}";

    /// <summary>
    /// Whether a storage key was signed for this workspace.
    /// </summary>
    /// <remarks>
    /// <b>A tenant boundary, not a formatting check.</b> Registration is the one place a client hands
    /// back a path, and without this a member of one workspace who learned another's storage key
    /// could register it as their own document — and then read the file through the download route.
    /// The signing route is what put the organization id into the key, so requiring it here means a
    /// key can only be registered by the workspace it was issued to.
    /// </remarks>
    public static bool BelongsTo(string storageKey, Guid organizationId) =>
        storageKey.Contains($"/{organizationId}/documents/", StringComparison.OrdinalIgnoreCase);
}
