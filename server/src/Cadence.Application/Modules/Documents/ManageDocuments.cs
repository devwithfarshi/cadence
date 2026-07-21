using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Common;
using Cadence.Domain.Library;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Documents;

/// <summary>One document.</summary>
public sealed record GetDocumentQuery(Guid DocumentId) : IQuery<Result<DocumentDto>>;

/// <summary>Signs an upload the browser will perform itself (§12.1).</summary>
public sealed record CreateUploadSignatureCommand(UploadSignatureRequest Upload)
    : ICommand<Result<UploadSignatureDto>>;

/// <summary>Records a file that has finished uploading.</summary>
public sealed record RegisterDocumentCommand(RegisterDocumentRequest Document)
    : ICommand<Result<DocumentDto>>;

public sealed record RenameDocumentCommand(Guid DocumentId, string Name)
    : ICommand<Result<DocumentDto>>;

public sealed record ToggleDocumentFavoriteCommand(Guid DocumentId) : ICommand<Result<DocumentDto>>;

public sealed record DeleteDocumentCommand(Guid DocumentId) : ICommand<Result>;

/// <summary>A short-lived link to the file itself.</summary>
public sealed record GetDocumentDownloadQuery(Guid DocumentId)
    : IQuery<Result<DocumentDownloadDto>>;

internal sealed class CreateUploadSignatureValidator : AbstractValidator<CreateUploadSignatureCommand>
{
    public CreateUploadSignatureValidator()
    {
        RuleFor(command => command.Upload.FileName)
            .NotEmpty().WithMessage("A file name is required.")
            .MaximumLength(500);

        RuleFor(command => command.Upload.SizeBytes)
            .GreaterThan(0).WithMessage("An empty file has nothing to upload.");
    }
}

internal sealed class RegisterDocumentValidator : AbstractValidator<RegisterDocumentCommand>
{
    public RegisterDocumentValidator()
    {
        RuleFor(command => command.Document.StorageKey)
            .NotEmpty().WithMessage("The upload could not be identified.")
            .MaximumLength(500);

        RuleFor(command => command.Document.FileName)
            .NotEmpty().WithMessage("A file name is required.")
            .MaximumLength(500);
    }
}

internal sealed class RenameDocumentValidator : AbstractValidator<RenameDocumentCommand>
{
    public RenameDocumentValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("A file name is required.")
            .MaximumLength(500);
    }
}

public sealed class GetDocumentHandler(ICadenceDbContext context)
    : IQueryHandler<GetDocumentQuery, Result<DocumentDto>>
{
    public async ValueTask<Result<DocumentDto>> Handle(
        GetDocumentQuery query,
        CancellationToken cancellationToken) =>
        await DocumentReads.LoadAsync(context, query.DocumentId, cancellationToken);
}

/// <summary>
/// Issues the parameters the browser needs to upload straight to the provider.
/// </summary>
/// <remarks>
/// Nothing is written here — a signature is permission to upload, not a document. A client that
/// signs and then abandons the transfer leaves no row, which is why registration is a separate call
/// and why an asset with no owning row is garbage the nightly reconciliation collects (§12.3).
/// </remarks>
public sealed class CreateUploadSignatureHandler(
    IFileStorage storage,
    ICurrentUser currentUser,
    IDateTime clock,
    ILogger<CreateUploadSignatureHandler> logger)
    : ICommandHandler<CreateUploadSignatureCommand, Result<UploadSignatureDto>>
{
    public async ValueTask<Result<UploadSignatureDto>> Handle(
        CreateUploadSignatureCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Upload;

        if (!DocumentPolicy.IsAllowedFileName(request.FileName))
        {
            return Result.Failure<UploadSignatureDto>(Error.Validation(
                "document.unsupported_format",
                $"That file type cannot be uploaded. Supported: "
                + $"{string.Join(", ", DocumentPolicy.AllowedExtensions)}."));
        }

        if (!DocumentPolicy.IsAllowedContentType(request.ContentType))
        {
            return Result.Failure<UploadSignatureDto>(Error.Validation(
                "document.unsupported_content_type",
                $"'{request.ContentType}' is not a content type this library accepts."));
        }

        if (request.SizeBytes > DocumentPolicy.MaxUploadBytes)
        {
            return Result.Failure<UploadSignatureDto>(Error.Validation(
                "document.too_large",
                $"Files are limited to {DocumentPolicy.MaxUploadBytes / (1024 * 1024)} MB."));
        }

        var organizationId = currentUser.RequireOrganizationId();

        try
        {
            var signed = await storage.CreateSignedUploadAsync(
                DocumentPolicy.FolderFor(organizationId, clock.UtcNow),
                request.FileName,
                cancellationToken);

            return Result.Success(new UploadSignatureDto(
                signed.UploadUrl,
                signed.StorageKey,
                signed.Signature,
                signed.Timestamp,
                signed.ApiKey,
                signed.Parameters));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not sign an upload for workspace {OrganizationId}", organizationId);

            return Result.Failure<UploadSignatureDto>(DocumentErrors.StorageUnavailable);
        }
    }
}

/// <summary>
/// Turns a finished upload into a row.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing the client says about the file is taken on trust.</b> The size comes from the provider,
/// the type from the extension the server itself signed, and the owner from the token. The one thing
/// the client contributes is the name, which is a label rather than a fact about the bytes.
/// </para>
/// <para>
/// The row starts <c>processing</c> and an indexing job resolves it. Doing that inline would tie the
/// response to work that has nothing to do with recording the upload.
/// </para>
/// </remarks>
public sealed class RegisterDocumentHandler(
    ICadenceDbContext context,
    IFileStorage storage,
    ICurrentUser currentUser,
    IJobScheduler jobs,
    ILogger<RegisterDocumentHandler> logger)
    : ICommandHandler<RegisterDocumentCommand, Result<DocumentDto>>
{
    public async ValueTask<Result<DocumentDto>> Handle(
        RegisterDocumentCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Document;
        var organizationId = currentUser.RequireOrganizationId();

        // The tenant boundary. Checked before anything is looked up, so registering somebody else's
        // storage key is refused without revealing whether it exists.
        if (!DocumentPolicy.BelongsTo(request.StorageKey, organizationId))
        {
            return Result.Failure<DocumentDto>(Error.Validation(
                "document.unknown_upload",
                "That upload was not issued for this workspace."));
        }

        // Ignoring the filters deliberately, to see soft-deleted rows: a deleted document still owns
        // its storage key, and re-registering it would give two rows one asset — the second of which
        // would be destroyed the moment the first is purged. The tenant predicate is restated by hand
        // because dropping the filters drops the tenant scope with them.
        var alreadyRegistered = await context.Documents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                document => document.StorageKey == request.StorageKey
                    && document.OrganizationId == organizationId,
                cancellationToken);

        if (alreadyRegistered)
        {
            return Result.Failure<DocumentDto>(Error.Conflict(
                "document.already_registered",
                "That upload has already been registered."));
        }

        var meeting = await ResolveMeetingAsync(request.MeetingId, cancellationToken);

        if (meeting.IsFailure)
        {
            return Result.Failure<DocumentDto>(meeting.Error);
        }

        StoredFile? stored;

        try
        {
            stored = await storage.GetAsync(request.StorageKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not read {StorageKey} back from storage", request.StorageKey);

            return Result.Failure<DocumentDto>(DocumentErrors.StorageUnavailable);
        }

        if (stored is null)
        {
            return Result.Failure<DocumentDto>(Error.Validation(
                "document.upload_not_found",
                "That file has not finished uploading, or the upload failed."));
        }

        var verified = await VerifyAsync(request, stored, cancellationToken);

        if (verified.IsFailure)
        {
            return Result.Failure<DocumentDto>(verified.Error);
        }

        Document document;

        try
        {
            document = Document.Register(
                organizationId,
                currentUser.RequireId(),
                request.FileName,
                // From the extension the server signed, not the one the request carried — they have
                // just been checked to agree, and the signed one is the one nobody could edit.
                DocumentPolicy.TypeOf(request.StorageKey),
                stored.SizeBytes,
                stored.StorageKey,
                stored.Url,
                request.MeetingId,
                request.Tags);
        }
        catch (DomainException exception)
        {
            return Result.Failure<DocumentDto>(
                Error.Validation("document.invalid", exception.Message));
        }

        await context.Documents.AddAsync(document, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        // Queued after the commit: a job that started first could find no row to index.
        var jobId = jobs.Enqueue<IIndexDocumentJob>(job => job.RunAsync(document.Id, organizationId));

        logger.LogInformation(
            "Queued indexing job {JobId} for document {DocumentId}",
            jobId,
            document.Id);

        return await DocumentReads.LoadAsync(context, document.Id, cancellationToken);
    }

    /// <summary>
    /// Checks the stored asset against what was signed, and destroys it if it disagrees.
    /// </summary>
    /// <remarks>
    /// This is the check §12.1 relies on. The signature cannot bound the size, and the content type
    /// is whatever the client claimed, so both are re-read here from the provider. A file that fails
    /// is removed rather than left behind — it is already paid-for storage that no row will ever
    /// point at.
    /// </remarks>
    private async Task<Result> VerifyAsync(
        RegisterDocumentRequest request,
        StoredFile stored,
        CancellationToken cancellationToken)
    {
        var signedExtension = DocumentPolicy.ExtensionOf(request.StorageKey);
        var claimedExtension = DocumentPolicy.ExtensionOf(request.FileName);

        if (!string.Equals(signedExtension, claimedExtension, StringComparison.OrdinalIgnoreCase))
        {
            await DiscardAsync(stored.StorageKey, cancellationToken);

            return Result.Failure(Error.Validation(
                "document.format_mismatch",
                $"This upload was signed for a .{signedExtension} file."));
        }

        // Cloudinary reports a format for most raw assets and nothing for some. An empty value is not
        // treated as a mismatch — asserting on a field the provider did not fill would reject valid
        // uploads to catch nothing.
        if (stored.Format.Length > 0
            && !string.Equals(stored.Format, signedExtension, StringComparison.OrdinalIgnoreCase))
        {
            await DiscardAsync(stored.StorageKey, cancellationToken);

            return Result.Failure(Error.Validation(
                "document.format_mismatch",
                $"The uploaded file is a .{stored.Format}, not a .{signedExtension}."));
        }

        if (stored.SizeBytes > DocumentPolicy.MaxUploadBytes)
        {
            await DiscardAsync(stored.StorageKey, cancellationToken);

            return Result.Failure(Error.Validation(
                "document.too_large",
                $"Files are limited to {DocumentPolicy.MaxUploadBytes / (1024 * 1024)} MB."));
        }

        return Result.Success();
    }

    /// <summary>Removes an asset that will never get a row. Failure to do so must not mask the
    /// rejection that caused it.</summary>
    private async Task DiscardAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await storage.DeleteAsync(storageKey, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Could not discard the rejected upload {StorageKey}",
                storageKey);
        }
    }

    /// <summary>
    /// Checks an attached meeting is visible in this workspace.
    /// </summary>
    /// <remarks>
    /// The same rule an action item's provenance follows: a foreign meeting id must not become a link
    /// on a row this workspace can read.
    /// </remarks>
    private async Task<Result> ResolveMeetingAsync(Guid? meetingId, CancellationToken cancellationToken)
    {
        if (meetingId is not { } id)
        {
            return Result.Success();
        }

        var exists = await context.Meetings
            .AsNoTracking()
            .AnyAsync(meeting => meeting.Id == id, cancellationToken);

        return exists
            ? Result.Success()
            : Result.Failure(Error.NotFound("meeting.not_found", "That meeting could not be found."));
    }
}

public sealed class RenameDocumentHandler(ICadenceDbContext context)
    : ICommandHandler<RenameDocumentCommand, Result<DocumentDto>>
{
    public async ValueTask<Result<DocumentDto>> Handle(
        RenameDocumentCommand command,
        CancellationToken cancellationToken)
    {
        var document = await context.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == command.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<DocumentDto>(DocumentReads.NotFound);
        }

        try
        {
            // The rename changes the label only. The stored asset keeps the key and extension it was
            // uploaded with, so a download still produces the original file — renaming a .pdf to
            // .docx changes how the row is grouped, not what the bytes are.
            document.Rename(command.Name, DocumentPolicy.TypeOf(command.Name));
        }
        catch (DomainException exception)
        {
            return Result.Failure<DocumentDto>(
                Error.Validation("document.invalid", exception.Message));
        }

        await context.SaveChangesAsync(cancellationToken);

        return await DocumentReads.LoadAsync(context, document.Id, cancellationToken);
    }
}

public sealed class ToggleDocumentFavoriteHandler(ICadenceDbContext context)
    : ICommandHandler<ToggleDocumentFavoriteCommand, Result<DocumentDto>>
{
    public async ValueTask<Result<DocumentDto>> Handle(
        ToggleDocumentFavoriteCommand command,
        CancellationToken cancellationToken)
    {
        var document = await context.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == command.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<DocumentDto>(DocumentReads.NotFound);
        }

        document.ToggleFavorite();
        await context.SaveChangesAsync(cancellationToken);

        return await DocumentReads.LoadAsync(context, document.Id, cancellationToken);
    }
}

/// <summary>
/// Deletes a document and schedules the asset behind it for destruction (§12.3).
/// </summary>
/// <remarks>
/// The row is soft-deleted and the file is destroyed for real, which means <b>a restored document
/// would come back without its bytes</b>. That is the blueprint's trade and it is the right one here:
/// keeping every deleted file alive indefinitely is a storage bill and a data-retention problem, and
/// §12.2 already says the row survives an asset that has gone — the UI shows a failed state rather
/// than a broken link.
/// </remarks>
public sealed class DeleteDocumentHandler(
    ICadenceDbContext context,
    IJobScheduler jobs)
    : ICommandHandler<DeleteDocumentCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteDocumentCommand command,
        CancellationToken cancellationToken)
    {
        var document = await context.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == command.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure(DocumentReads.NotFound);
        }

        var storageKey = document.StorageKey;

        // Soft delete, applied by the auditing interceptor.
        context.Documents.Remove(document);
        await context.SaveChangesAsync(cancellationToken);

        // Enqueued only once the delete has committed. The other order destroys the file of a
        // document that is still there if the save fails.
        jobs.Enqueue<IPurgeDocumentAssetJob>(job => job.RunAsync(storageKey));

        return Result.Success();
    }
}

/// <summary>
/// Mints a link to the file.
/// </summary>
/// <remarks>
/// Not part of §6's table, and a deliberate addition: §12.3 requires private assets to be served
/// through short-lived signed URLs, so something has to mint them. The alternative — putting a
/// permanent URL on <c>DocumentDto</c> — would make every document readable by anyone who ever saw a
/// list response, forever, including after it was deleted.
/// </remarks>
public sealed class GetDocumentDownloadHandler(
    ICadenceDbContext context,
    IFileStorage storage,
    IDateTime clock,
    ILogger<GetDocumentDownloadHandler> logger)
    : IQueryHandler<GetDocumentDownloadQuery, Result<DocumentDownloadDto>>
{
    public async ValueTask<Result<DocumentDownloadDto>> Handle(
        GetDocumentDownloadQuery query,
        CancellationToken cancellationToken)
    {
        var document = await context.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == query.DocumentId, cancellationToken);

        if (document is null)
        {
            return Result.Failure<DocumentDownloadDto>(DocumentReads.NotFound);
        }

        try
        {
            var url = await storage.GetDownloadUrlAsync(
                document.StorageKey,
                DocumentPolicy.DownloadUrlLifetime,
                cancellationToken);

            return Result.Success(new DocumentDownloadDto(
                url,
                clock.UtcNow.Add(DocumentPolicy.DownloadUrlLifetime)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Could not mint a download URL for document {DocumentId}",
                document.Id);

            return Result.Failure<DocumentDownloadDto>(DocumentErrors.StorageUnavailable);
        }
    }
}

internal static class DocumentErrors
{
    /// <summary>
    /// The file host could not be reached, or was never configured.
    /// </summary>
    /// <remarks>
    /// Reported as a failure rather than dressed up as a validation error: nothing the caller sent is
    /// wrong, and a 400 would send them off editing a request that was fine.
    /// </remarks>
    public static Error StorageUnavailable => Error.Failure(
        "document.storage_unavailable",
        "File storage is unavailable. The upload was not recorded.");
}
