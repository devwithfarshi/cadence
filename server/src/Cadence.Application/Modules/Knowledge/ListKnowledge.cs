using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Knowledge;

/// <summary>
/// The knowledge list, filtered and paged.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>KnowledgeQuery</c> 1:1 (§6), plus <c>OwnerId</c> — the same filter
/// documents carry, and the one a "filed by me" view needs.
/// </remarks>
public sealed record KnowledgeQuery : ListQuery
{
    public IReadOnlyList<KnowledgeItemKind>? Kind { get; init; }

    public IReadOnlyList<string>? Category { get; init; }

    public Guid? OwnerId { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public bool FavoritesOnly { get; init; }
}

public sealed record ListKnowledgeQuery(KnowledgeQuery Query)
    : IQuery<Result<PagedResult<KnowledgeItemDto>>>;

/// <summary>The values behind the category and tag filter menus.</summary>
public sealed record GetKnowledgeFacetsQuery : IQuery<Result<KnowledgeFacetsDto>>;

public sealed class ListKnowledgeHandler(ICadenceDbContext context)
    : IQueryHandler<ListKnowledgeQuery, Result<PagedResult<KnowledgeItemDto>>>
{
    public async ValueTask<Result<PagedResult<KnowledgeItemDto>>> Handle(
        ListKnowledgeQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(await KnowledgeReads.PageAsync(context, query.Query, cancellationToken));
}

public sealed class GetKnowledgeFacetsHandler(ICadenceDbContext context)
    : IQueryHandler<GetKnowledgeFacetsQuery, Result<KnowledgeFacetsDto>>
{
    public async ValueTask<Result<KnowledgeFacetsDto>> Handle(
        GetKnowledgeFacetsQuery query,
        CancellationToken cancellationToken)
    {
        // Both distinct sets are computed in the database rather than by loading every entry and
        // reducing in memory. These feed two filter menus, and a menu should not cost a table read.
        var categories = await context.KnowledgeItems
            .AsNoTracking()
            .Select(item => item.Category)
            .Distinct()
            .OrderBy(category => category)
            .ToListAsync(cancellationToken);

        var tags = await context.KnowledgeItems
            .AsNoTracking()
            .SelectMany(item => EF.Property<List<string>>(item, KnowledgeReads.TagsField))
            .Distinct()
            .OrderBy(tag => tag)
            .ToListAsync(cancellationToken);

        return Result.Success(new KnowledgeFacetsDto(categories, tags));
    }
}
