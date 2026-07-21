using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Documents;

/// <summary>
/// The Documents page's selection toolbar, as one command.
/// </summary>
/// <remarks>
/// The tenant filter is what makes it safe to take ids from a client: an id from another workspace
/// simply does not come back, and the count reflects that rather than naming which ids were rejected.
/// </remarks>
public sealed record BulkDeleteDocumentsCommand(IReadOnlyList<Guid> Ids)
    : ICommand<Result<BulkResultDto>>;

public sealed class BulkDeleteDocumentsHandler(ICadenceDbContext context, IJobScheduler jobs)
    : ICommandHandler<BulkDeleteDocumentsCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        BulkDeleteDocumentsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        var documents = await context.Documents
            .Where(document => command.Ids.Contains(document.Id))
            .ToListAsync(cancellationToken);

        // Read before the delete: the entities stay loaded afterwards, but reading the keys up front
        // keeps the enqueue loop below independent of what soft delete leaves on the instances.
        var storageKeys = documents.Select(document => document.StorageKey).ToList();

        context.Documents.RemoveRange(documents);
        await context.SaveChangesAsync(cancellationToken);

        // One job per asset rather than one job for the batch: a provider that refuses one key should
        // not strand the rest, and Hangfire retries at the granularity of a job.
        foreach (var storageKey in storageKeys)
        {
            jobs.Enqueue<IPurgeDocumentAssetJob>(job => job.RunAsync(storageKey));
        }

        return Result.Success(new BulkResultDto(documents.Count));
    }
}
