using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.ActionItems;

/// <summary>
/// The Tasks page's selection toolbar, as commands.
/// </summary>
/// <remarks>
/// Each takes a list of ids and reports how many rows it changed. The tenant filter is what makes it
/// safe to take ids from a client: an id from another workspace simply does not come back, and the
/// count reflects that rather than naming which ids were rejected.
/// </remarks>
public sealed record BulkSetStatusCommand(IReadOnlyList<Guid> Ids, ActionItemStatus Status)
    : ICommand<Result<BulkResultDto>>;

public sealed record BulkAssignCommand(IReadOnlyList<Guid> Ids, Guid? AssigneeId)
    : ICommand<Result<BulkResultDto>>;

public sealed record BulkSetPriorityCommand(IReadOnlyList<Guid> Ids, ActionItemPriority Priority)
    : ICommand<Result<BulkResultDto>>;

public sealed record BulkDeleteActionItemsCommand(IReadOnlyList<Guid> Ids)
    : ICommand<Result<BulkResultDto>>;

public sealed class BulkSetStatusHandler(ICadenceDbContext context)
    : ICommandHandler<BulkSetStatusCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        BulkSetStatusCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        // Rows already in the target status are excluded so the count means "changed" rather than
        // "matched" — a bulk action over a mixed selection otherwise reports a number the UI cannot
        // explain to the person who triggered it.
        var items = await context.ActionItems
            .Where(item => command.Ids.Contains(item.Id) && item.Status != command.Status)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            // The aggregate keeps CompletedAt in step, so a bulk "mark done" cannot leave rows that
            // are done without a completion time.
            item.ChangeStatus(command.Status);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResultDto(items.Count));
    }
}

public sealed class BulkAssignHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<BulkAssignCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        BulkAssignCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        var assignee = await ActionItemWrites.ResolveAssigneeAsync(
            context,
            currentUser.RequireOrganizationId(),
            command.AssigneeId,
            cancellationToken);

        if (assignee.IsFailure)
        {
            return Result.Failure<BulkResultDto>(assignee.Error);
        }

        var items = await context.ActionItems
            .Where(item => command.Ids.Contains(item.Id) && item.AssigneeId != command.AssigneeId)
            .ToListAsync(cancellationToken);

        var actorId = currentUser.RequireId();

        foreach (var item in items)
        {
            item.Assign(assignee.Value, actorId);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResultDto(items.Count));
    }
}

public sealed class BulkSetPriorityHandler(ICadenceDbContext context)
    : ICommandHandler<BulkSetPriorityCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        BulkSetPriorityCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        var items = await context.ActionItems
            .Where(item => command.Ids.Contains(item.Id) && item.Priority != command.Priority)
            .ToListAsync(cancellationToken);

        foreach (var item in items)
        {
            item.ChangePriority(command.Priority);
        }

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResultDto(items.Count));
    }
}

public sealed class BulkDeleteActionItemsHandler(ICadenceDbContext context)
    : ICommandHandler<BulkDeleteActionItemsCommand, Result<BulkResultDto>>
{
    public async ValueTask<Result<BulkResultDto>> Handle(
        BulkDeleteActionItemsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Ids.Count == 0)
        {
            return Result.Success(new BulkResultDto(0));
        }

        var items = await context.ActionItems
            .Where(item => command.Ids.Contains(item.Id))
            .ToListAsync(cancellationToken);

        context.ActionItems.RemoveRange(items);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(new BulkResultDto(items.Count));
    }
}
