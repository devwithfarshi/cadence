using Cadence.Api.Common;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Modules.Documents;
using Hangfire;
using Mediator;

namespace Cadence.Api.Jobs;

/// <summary>
/// The Hangfire entry point for document indexing.
/// </summary>
/// <remarks>
/// It lives in the Api layer for the same reason <c>SummarizeMeetingJob</c> does: this is where
/// <c>ICurrentUser</c> is implemented, and therefore where the mechanism for giving non-request work
/// an identity lives. Without the staged principal every query in the handler would run against
/// <see cref="Guid.Empty"/> and quietly find nothing.
/// </remarks>
public sealed class IndexDocumentJob(
    ISender sender,
    ScopedPrincipal principal,
    ILogger<IndexDocumentJob> logger)
    : IIndexDocumentJob
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [10, 60, 300])]
    public async Task RunAsync(Guid documentId, Guid organizationId)
    {
        principal.Principal = ScopedPrincipal.ForOrganization(organizationId);

        var result = await sender.Send(new IndexDocumentCommand(documentId));

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Indexing document {DocumentId} failed: {Error}",
                documentId,
                result.Error.Description);

            throw new InvalidOperationException(result.Error.Description);
        }
    }
}

/// <summary>
/// Destroys the stored asset behind a deleted document.
/// </summary>
/// <remarks>
/// <para>
/// It calls <see cref="IFileStorage"/> directly rather than going through a command: there is no row
/// left to read, no tenant to establish and nothing to authorize — the decision was made when the
/// document was deleted, and this is the consequence.
/// </para>
/// <para>
/// Retried, because a provider outage between the delete and the destroy would otherwise leave a file
/// alive that a user believes is gone.
/// </para>
/// </remarks>
public sealed class PurgeDocumentAssetJob(
    IFileStorage storage,
    ILogger<PurgeDocumentAssetJob> logger)
    : IPurgeDocumentAssetJob
{
    [AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800])]
    public async Task RunAsync(string storageKey)
    {
        await storage.DeleteAsync(storageKey);

        logger.LogInformation("Destroyed the stored asset {StorageKey}", storageKey);
    }
}
