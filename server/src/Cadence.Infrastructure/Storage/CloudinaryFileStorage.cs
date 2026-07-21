using System.Net;
using Cadence.Application.Common.Abstractions;
using Cadence.Infrastructure.Configuration;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Cadence.Infrastructure.Storage;

/// <summary>
/// The <see cref="IFileStorage"/> adapter for Cloudinary (§12).
/// </summary>
/// <remarks>
/// <para>
/// Everything here is metadata and signatures — <b>no file content passes through this class</b>. The
/// browser posts the bytes straight to Cloudinary using a signature produced here, so a
/// three-hundred-megabyte recording never occupies an API request thread (§12.1).
/// </para>
/// <para>
/// <b>Every document is stored as a <c>raw</c> resource of type <c>authenticated</c>.</b> Raw because
/// this is a document library rather than an image CDN: raw keeps the bytes exactly as uploaded and,
/// more usefully here, means one resource type covers pdf, docx, csv and png alike — the alternative
/// is probing three resource types on every lookup because the port is handed a key and nothing else.
/// Authenticated because a document is private: an <c>upload</c>-type asset is readable by anyone who
/// learns its URL, which is the whole reason §12.3 asks for short-lived signed delivery URLs.
/// </para>
/// </remarks>
public sealed class CloudinaryFileStorage : IFileStorage
{
    /// <summary>Cloudinary's delivery type for assets that are not publicly readable.</summary>
    private const string AuthenticatedType = "authenticated";

    private const string RawResourceType = "raw";

    private readonly Cloudinary? _cloudinary;
    private readonly CloudinaryOptions _options;
    private readonly ILogger<CloudinaryFileStorage> _logger;

    public CloudinaryFileStorage(
        IOptions<CloudinaryOptions> options,
        ILogger<CloudinaryFileStorage> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Constructed once, and only when configured. The SDK client is thread-safe and holds the
        // HTTP pipeline, so one per process is both correct and what keeps connections alive.
        _cloudinary = _options.IsConfigured ? new Cloudinary(_options.Url) : null;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The returned <c>StorageKey</c> is decided <i>here</i> rather than accepted from the client,
    /// and it is covered by the signature. That is what stops a member of one workspace signing an
    /// upload into another's folder: Cloudinary rejects any request whose parameters do not match the
    /// signature, so the path cannot be edited on the way out of the browser.
    /// </remarks>
    public Task<SignedUpload> CreateSignedUploadAsync(
        string folder,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var cloudinary = Require();

        var storageKey = BuildStorageKey(folder, fileName);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Only the parameters Cloudinary signs. resource_type is part of the URL, not the payload,
        // and api_key is sent alongside the signature rather than inside it.
        var parameters = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["public_id"] = storageKey,
            ["timestamp"] = timestamp,
            ["type"] = AuthenticatedType,
        };

        var signature = cloudinary.Api.SignParameters(parameters);

        return Task.FromResult(new SignedUpload(
            UploadUrl: cloudinary.Api.GetUploadUrl(RawResourceType),
            StorageKey: storageKey,
            Signature: signature,
            Timestamp: timestamp,
            ApiKey: cloudinary.Api.Account.ApiKey,
            Parameters: parameters.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.ToString() ?? string.Empty,
                StringComparer.Ordinal)));
    }

    /// <inheritdoc />
    /// <remarks>
    /// A missing asset is <see langword="null"/>, not an exception: "the upload never arrived" is an
    /// ordinary answer to this question, and the caller has a use for it — refusing to register a
    /// document for a file that is not there.
    /// </remarks>
    public async Task<StoredFile?> GetAsync(
        string storageKey,
        CancellationToken cancellationToken = default)
    {
        var cloudinary = Require();

        var result = await cloudinary.GetResourceAsync(
            new GetResourceParams(storageKey)
            {
                ResourceType = ResourceType.Raw,
                Type = AuthenticatedType,
            },
            cancellationToken);

        if (result.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (result.Error is not null)
        {
            throw new FileStorageException(
                $"Cloudinary could not describe '{storageKey}': {result.Error.Message}");
        }

        return new StoredFile(
            result.PublicId,
            result.SecureUrl,
            result.Bytes,
            // Cloudinary reports a format ("pdf"), never the MIME type the client declared. Reporting
            // what the provider actually knows is what lets the caller catch a mismatch.
            result.Format ?? string.Empty);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var cloudinary = Require();

        var result = await cloudinary.DestroyAsync(new DeletionParams(storageKey)
        {
            ResourceType = ResourceType.Raw,
            Type = AuthenticatedType,
        });

        // A key that is already gone is the desired end state, so it is logged rather than thrown —
        // this runs from a background job whose retry would achieve nothing.
        if (result.Error is not null)
        {
            _logger.LogWarning(
                "Cloudinary refused to destroy {StorageKey}: {Error}",
                storageKey,
                result.Error.Message);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Built per request and short-lived. The alternative — storing one long-lived URL on the row —
    /// makes the document readable forever by anyone the URL ever reaches, including after the
    /// document is deleted (§12.3).
    /// </remarks>
    public Task<string> GetDownloadUrlAsync(
        string storageKey,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default)
    {
        var cloudinary = Require();

        var url = cloudinary.DownloadPrivate(
            storageKey,
            attachment: true,
            format: string.Empty,
            type: AuthenticatedType,
            expiresAt: DateTimeOffset.UtcNow.Add(expiresIn).ToUnixTimeSeconds(),
            resourceType: RawResourceType);

        return Task.FromResult(url);
    }

    /// <summary>
    /// <c>{folder}/{uuid}{extension}</c> under the deployment's root.
    /// </summary>
    /// <remarks>
    /// A generated id rather than the uploaded file name: two people uploading <c>notes.pdf</c> must
    /// not collide, and a file name is attacker-controlled text that would otherwise become a path.
    /// The extension is kept because a raw asset's public id is what gives the downloaded file its
    /// name, and a browser handed an extensionless PDF does not know what to open.
    /// </remarks>
    private string BuildStorageKey(string folder, string fileName)
    {
        var extension = Path.GetExtension(fileName);

        // Path.GetExtension returns whatever followed the last dot, which for a hostile name can be
        // arbitrarily long or contain separators. Anything unexpected is simply dropped.
        if (extension.Length > 12 || !extension.All(character =>
                character == '.' || char.IsAsciiLetterOrDigit(character)))
        {
            extension = string.Empty;
        }

        return $"cadence/{_options.Environment}/{folder}/{Guid.CreateVersion7()}{extension.ToLowerInvariant()}";
    }

    private Cloudinary Require() =>
        _cloudinary ?? throw new FileStorageException(
            "Cloudinary is not configured. Set Cloudinary__Url before uploading files.");
}

/// <summary>
/// Every failure reaching a caller of <see cref="IFileStorage"/>, as one type.
/// </summary>
/// <remarks>
/// The same shape as <c>LlmProviderException</c>, and for the same reason: a handler should not have
/// to know which of a third party's failure modes it is looking at to decide that the operation did
/// not happen.
/// </remarks>
public sealed class FileStorageException : Exception
{
    public FileStorageException(string message)
        : base(message)
    {
    }

    public FileStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
