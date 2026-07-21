namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Where uploaded files live. Implemented by the Cloudinary adapter.
/// </summary>
/// <remarks>
/// The port exists so swapping Cloudinary for S3 or Azure Blob touches one adapter and no use case
/// (§12.1). Nothing here mentions Cloudinary types.
/// </remarks>
public interface IFileStorage
{
    /// <summary>
    /// Produces the parameters the browser needs to upload <b>directly</b> to the provider.
    /// </summary>
    /// <remarks>
    /// The file never passes through the API. Proxying uploads would tie up a request thread for
    /// the length of the transfer and cap file size at whatever the server tolerates, for no
    /// benefit — the signature is what enforces the constraints (§12.3).
    /// </remarks>
    Task<SignedUpload> CreateSignedUploadAsync(
        string folder,
        string fileName,
        CancellationToken cancellationToken = default);

    /// <summary>Confirms an asset exists and returns its real size and type, as reported by the
    /// provider — client-supplied metadata is not trusted.</summary>
    Task<StoredFile?> GetAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>A time-limited URL for a private asset.</summary>
    Task<string> GetDownloadUrlAsync(
        string storageKey,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default);
}

/// <summary>What the browser needs to upload straight to the provider.</summary>
/// <remarks>
/// <c>StorageKey</c> is the <c>publicId</c> the asset will get, decided up front so the database row
/// can reference it before the upload finishes. <c>Signature</c> covers every parameter, so the
/// provider rejects a request the client has altered, and <c>Timestamp</c> (unix seconds) bounds how
/// long that signature stays usable.
/// </remarks>
public sealed record SignedUpload(
    string UploadUrl,
    string StorageKey,
    string Signature,
    long Timestamp,
    string ApiKey,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>An asset as the provider describes it, which is the only description worth trusting.</summary>
/// <remarks>
/// <c>Format</c> is the provider's own word for the file type — <c>pdf</c>, <c>docx</c> — rather than
/// the MIME type the client declared when it asked for a signature. The two are deliberately
/// different values from different sources: comparing them is how a client that lied about what it
/// was uploading gets caught (§12.1).
/// </remarks>
public sealed record StoredFile(string StorageKey, string Url, long SizeBytes, string Format);
