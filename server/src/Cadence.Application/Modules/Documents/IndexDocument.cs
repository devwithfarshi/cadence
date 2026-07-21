using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Documents;

/// <summary>
/// Resolves a freshly registered document out of <c>processing</c>.
/// </summary>
/// <remarks>
/// <para>
/// The asset is re-read from the provider first. Registration already confirmed it existed, but the
/// two are separate moments and an upload can be replaced or removed in between — and a document
/// whose file has gone must say so (<c>failed</c>) rather than sit in a status the page reads as
/// "nearly ready" forever (§12.2).
/// </para>
/// <para>
/// <b>What this does not do is extract text.</b> There is no extraction port and no adapter behind
/// one, so a document is searchable by its name and its tags and nothing else. The excerpt says that
/// rather than claiming the contents were read — an excerpt that implied full-text search would make
/// every empty search result look like a bug in search.
/// </para>
/// </remarks>
public sealed record IndexDocumentCommand(Guid DocumentId) : ICommand<Result>;

public sealed class IndexDocumentHandler(
    ICadenceDbContext context,
    IFileStorage storage,
    ILogger<IndexDocumentHandler> logger)
    : ICommandHandler<IndexDocumentCommand, Result>
{
    public async ValueTask<Result> Handle(
        IndexDocumentCommand command,
        CancellationToken cancellationToken)
    {
        var document = await context.Documents
            .FirstOrDefaultAsync(candidate => candidate.Id == command.DocumentId, cancellationToken);

        if (document is null)
        {
            // Deleted between registration and indexing. Nothing to do, and nothing wrong — reported
            // as success so the job is not retried against a row that will never come back.
            logger.LogInformation(
                "Document {DocumentId} was gone before it could be indexed",
                command.DocumentId);

            return Result.Success();
        }

        var stored = await storage.GetAsync(document.StorageKey, cancellationToken);

        if (stored is null)
        {
            // The terminal state the UI can act on. Nothing is fabricated to fill the gap, for the
            // same reason a failed summary writes no summary (§23.3).
            document.MarkFailed();
            await context.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "Document {DocumentId} has no asset at {StorageKey}; marked failed",
                document.Id,
                document.StorageKey);

            return Result.Success();
        }

        document.MarkIndexed(Excerpt(document.Name, stored.SizeBytes));
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>
    /// A preview line that describes the file rather than its contents.
    /// </summary>
    /// <remarks>
    /// Every word of it is checkable against the row. The temptation is a friendlier sentence about
    /// the document being searchable across the workspace — which is what the client's mock writes,
    /// and which would not be true of anything inside the file.
    /// </remarks>
    private static string Excerpt(string name, long sizeBytes)
    {
        var extension = DocumentPolicy.ExtensionOf(name);
        var label = extension.Length > 0 ? extension.ToUpperInvariant() : "File";

        return $"{label} · {Size(sizeBytes)} · searchable by name and tags.";
    }

    private static string Size(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / (1024d * 1024d):0.#} MB",
        >= 1024 => $"{bytes / 1024d:0.#} KB",
        _ => $"{bytes} bytes",
    };
}

/// <summary>
/// The indexing job, as the scheduler sees it.
/// </summary>
/// <remarks>
/// The organization travels with the id for the same reason it does on summarisation: a job has no
/// caller, so its tenant has to be handed to it by whoever had the authority to establish one. Looking
/// it up would need <c>IgnoreQueryFilters</c>, which turns "sees nothing" into "sees everything".
/// </remarks>
public interface IIndexDocumentJob
{
    Task RunAsync(Guid documentId, Guid organizationId);
}

/// <summary>
/// Destroys the stored asset behind a deleted document (§12.3).
/// </summary>
/// <remarks>
/// It takes a storage key and touches no table, so it needs no tenant: the key was read from a row
/// the caller could already see, and the only thing this job can do with it is delete it.
/// </remarks>
public interface IPurgeDocumentAssetJob
{
    Task RunAsync(string storageKey);
}
