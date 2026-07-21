using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Documents;

/// <summary>
/// A file in the library, as the Documents page renders it.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>DocumentFile</c> shape 1:1 (§6). There is deliberately no URL field: a
/// document is a private asset, and its delivery URL is minted per request with an expiry rather than
/// stored on the row (§12.3). The download route is what produces one.
/// </remarks>
public sealed record DocumentDto(
    Guid Id,
    string Name,
    DocumentType Type,
    long SizeBytes,
    Guid OwnerId,
    ProcessingStatus ProcessingStatus,
    string Excerpt,
    IReadOnlyList<string> Tags,
    bool IsFavorite,
    Guid? MeetingId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Asks for permission to upload one file.
/// </summary>
/// <remarks>
/// The size and content type are what the client <i>says</i> it is about to upload. They are checked
/// before signing so an obviously-too-large or unsupported file is refused before the transfer
/// starts — but neither is trusted afterwards: both are re-read from the provider at registration,
/// which is the check that actually holds (§12.1).
/// </remarks>
public sealed record UploadSignatureRequest(string FileName, string ContentType, long SizeBytes);

/// <summary>
/// What the browser needs to post the file straight to the provider.
/// </summary>
/// <remarks>
/// <para>
/// The client posts <c>file</c> plus <c>apiKey</c>, <c>signature</c> and every entry of
/// <c>parameters</c> to <c>uploadUrl</c> as multipart form data. It must not add or alter a
/// parameter: the signature covers them, so the provider rejects anything that does not match.
/// </para>
/// <para>
/// <c>storageKey</c> comes back so the client can name the finished upload when it registers. It is
/// chosen by the server — a client-chosen path would let a member of one workspace write into
/// another's folder.
/// </para>
/// </remarks>
public sealed record UploadSignatureDto(
    string UploadUrl,
    string StorageKey,
    string Signature,
    long Timestamp,
    string ApiKey,
    IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Records a file that has finished uploading.
/// </summary>
/// <remarks>
/// There is no <c>sizeBytes</c> and no <c>ownerId</c>. The size is read from the provider, and the
/// owner is the caller — accepting either from the body would let a client file someone else's
/// document, or claim a size that has nothing to do with the bytes that landed.
/// </remarks>
public sealed record RegisterDocumentRequest(
    string StorageKey,
    string FileName,
    Guid? MeetingId,
    IReadOnlyList<string>? Tags);

/// <summary>Renames a document. The type is re-derived from the new extension.</summary>
public sealed record RenameDocumentRequest(string Name);

/// <summary>
/// A short-lived link to the file itself.
/// </summary>
/// <remarks>
/// <c>ExpiresAt</c> is returned so the client can tell "this link has expired" apart from "this file
/// is gone" without having to follow the link to find out.
/// </remarks>
public sealed record DocumentDownloadDto(string Url, DateTimeOffset ExpiresAt);
