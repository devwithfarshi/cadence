using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Library;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Knowledge;

/// <summary>One entry.</summary>
public sealed record GetKnowledgeItemQuery(Guid KnowledgeItemId)
    : IQuery<Result<KnowledgeItemDto>>;

public sealed record CreateKnowledgeItemCommand(CreateKnowledgeItemRequest Item)
    : ICommand<Result<KnowledgeItemDto>>;

public sealed record ToggleKnowledgeFavoriteCommand(Guid KnowledgeItemId)
    : ICommand<Result<KnowledgeItemDto>>;

/// <summary>Records that someone opened the entry.</summary>
public sealed record MarkKnowledgeItemOpenedCommand(Guid KnowledgeItemId) : ICommand<Result>;

public sealed record DeleteKnowledgeItemCommand(Guid KnowledgeItemId) : ICommand<Result>;

internal sealed class CreateKnowledgeItemValidator : AbstractValidator<CreateKnowledgeItemCommand>
{
    public CreateKnowledgeItemValidator()
    {
        RuleFor(command => command.Item.Title)
            .NotEmpty().WithMessage("Give the entry a title.")
            .MaximumLength(500);

        RuleFor(command => command.Item.Kind).IsInEnum();

        RuleFor(command => command.Item.Category).MaximumLength(100);

        RuleFor(command => command.Item.SourceUrl)
            .MaximumLength(2048)
            .Must(BeAnAbsoluteHttpUrl)
            .WithMessage("A link entry needs a full http:// or https:// address.")
            .When(command => !string.IsNullOrWhiteSpace(command.Item.SourceUrl));
    }

    /// <summary>
    /// Rejects anything that is not an absolute web address.
    /// </summary>
    /// <remarks>
    /// The client opens this URL with <c>window.open</c>, so a relative path would resolve against
    /// Cadence's own origin and a <c>javascript:</c> scheme would run in the reader's session. The
    /// scheme check is the load-bearing half.
    /// </remarks>
    private static bool BeAnAbsoluteHttpUrl(string? candidate) =>
        Uri.TryCreate(candidate, UriKind.Absolute, out var url)
        && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
}

public sealed class GetKnowledgeItemHandler(ICadenceDbContext context)
    : IQueryHandler<GetKnowledgeItemQuery, Result<KnowledgeItemDto>>
{
    public async ValueTask<Result<KnowledgeItemDto>> Handle(
        GetKnowledgeItemQuery query,
        CancellationToken cancellationToken) =>
        await KnowledgeReads.LoadAsync(context, query.KnowledgeItemId, cancellationToken);
}

public sealed class CreateKnowledgeItemHandler(ICadenceDbContext context, ICurrentUser currentUser)
    : ICommandHandler<CreateKnowledgeItemCommand, Result<KnowledgeItemDto>>
{
    public async ValueTask<Result<KnowledgeItemDto>> Handle(
        CreateKnowledgeItemCommand command,
        CancellationToken cancellationToken)
    {
        var request = command.Item;

        var source = await ResolveSourceAsync(request, cancellationToken);

        if (source.IsFailure)
        {
            return Result.Failure<KnowledgeItemDto>(source.Error);
        }

        KnowledgeItem item;

        try
        {
            item = KnowledgeItem.Create(
                currentUser.RequireOrganizationId(),
                currentUser.RequireId(),
                request.Title,
                request.Kind,
                request.Category ?? string.Empty,
                request.Excerpt ?? string.Empty,
                source.Value,
                request.Kind == KnowledgeItemKind.Link ? request.SourceUrl : null,
                request.Tags);
        }
        catch (DomainException exception)
        {
            return Result.Failure<KnowledgeItemDto>(
                Error.Validation("knowledge_item.invalid", exception.Message));
        }

        await context.KnowledgeItems.AddAsync(item, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        return await KnowledgeReads.LoadAsync(context, item.Id, cancellationToken);
    }

    /// <summary>
    /// Checks that a cited record exists, is visible here, and is the kind the entry claims.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same rule the summary applies to a highlight and an action item to its segment: <b>a
    /// citation that does not resolve is worse than none</b>, because it looks checkable and is not.
    /// An entry whose source is a meeting from another workspace would render a link to a 404 and,
    /// worse, assert a connection to a record the reader cannot inspect.
    /// </para>
    /// <para>
    /// A link entry cannot carry a source id at all. It points outside this workspace by definition,
    /// and accepting both would leave the client to guess which one to open.
    /// </para>
    /// </remarks>
    private async Task<Result<Guid?>> ResolveSourceAsync(
        CreateKnowledgeItemRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceId is not { } sourceId)
        {
            return Result.Success<Guid?>(null);
        }

        if (request.Kind == KnowledgeItemKind.Link)
        {
            return Result.Failure<Guid?>(Error.Validation(
                "knowledge_item.link_has_no_source",
                "A link entry points at a URL, not at a record in this workspace."));
        }

        var exists = request.Kind switch
        {
            KnowledgeItemKind.Document => await context.Documents
                .AsNoTracking()
                .AnyAsync(document => document.Id == sourceId, cancellationToken),

            // Both a meeting note and an AI summary are about a meeting.
            _ => await context.Meetings
                .AsNoTracking()
                .AnyAsync(meeting => meeting.Id == sourceId, cancellationToken),
        };

        return exists
            ? Result.Success<Guid?>(sourceId)
            : Result.Failure<Guid?>(Error.NotFound(
                "knowledge_item.source_not_found",
                "The record this entry refers to could not be found."));
    }
}

public sealed class ToggleKnowledgeFavoriteHandler(ICadenceDbContext context)
    : ICommandHandler<ToggleKnowledgeFavoriteCommand, Result<KnowledgeItemDto>>
{
    public async ValueTask<Result<KnowledgeItemDto>> Handle(
        ToggleKnowledgeFavoriteCommand command,
        CancellationToken cancellationToken)
    {
        var item = await context.KnowledgeItems
            .FirstOrDefaultAsync(candidate => candidate.Id == command.KnowledgeItemId, cancellationToken);

        if (item is null)
        {
            return Result.Failure<KnowledgeItemDto>(KnowledgeReads.NotFound);
        }

        item.ToggleFavorite();
        await context.SaveChangesAsync(cancellationToken);

        return await KnowledgeReads.LoadAsync(context, item.Id, cancellationToken);
    }
}

/// <summary>
/// Records a visit, so the recently-opened rail reflects real usage.
/// </summary>
/// <remarks>
/// Returns nothing. The client calls this as it opens the entry and immediately refetches the rail,
/// so serialising the row back would be a payload nobody reads — and §6 specifies 204.
/// </remarks>
public sealed class MarkKnowledgeItemOpenedHandler(ICadenceDbContext context, IDateTime clock)
    : ICommandHandler<MarkKnowledgeItemOpenedCommand, Result>
{
    public async ValueTask<Result> Handle(
        MarkKnowledgeItemOpenedCommand command,
        CancellationToken cancellationToken)
    {
        var item = await context.KnowledgeItems
            .FirstOrDefaultAsync(candidate => candidate.Id == command.KnowledgeItemId, cancellationToken);

        if (item is null)
        {
            return Result.Failure(KnowledgeReads.NotFound);
        }

        item.MarkOpened(clock.UtcNow);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class DeleteKnowledgeItemHandler(ICadenceDbContext context)
    : ICommandHandler<DeleteKnowledgeItemCommand, Result>
{
    public async ValueTask<Result> Handle(
        DeleteKnowledgeItemCommand command,
        CancellationToken cancellationToken)
    {
        var item = await context.KnowledgeItems
            .FirstOrDefaultAsync(candidate => candidate.Id == command.KnowledgeItemId, cancellationToken);

        if (item is null)
        {
            return Result.Failure(KnowledgeReads.NotFound);
        }

        // Soft delete, applied by the auditing interceptor. Nothing cascades: an entry citing a
        // meeting is a pointer, and removing the pointer leaves the meeting alone.
        context.KnowledgeItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
