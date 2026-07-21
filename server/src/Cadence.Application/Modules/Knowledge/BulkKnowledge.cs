using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Knowledge;

/// <summary>
/// The Knowledge page's selection toolbar, as one command.
/// </summary>
/// <remarks>
/// The tenant filter is what makes it safe to take ids from a client: an id from another workspace
/// simply does not come back, and the count reflects that rather than naming which ids were rejected.
/// </remarks>
public sealed record BulkDeleteKnowledgeItemsCommand(IReadOnlyList<Guid> Ids)
    : ICommand<Result<BulkResultDto>>;

public sealed class BulkDeleteKnowledgeItemsHandler(ICadenceDbContext context)
    : ICommandHandler<BulkDeleteKnowledgeItemsCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        BulkDeleteKnowledgeItemsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        var items = await context.KnowledgeItems
            .Where(item => command.Ids.Contains(item.Id))
            .ToListAsync(cancellationToken);

        context.KnowledgeItems.RemoveRange(items);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResultDto(items.Count));
    }
}
