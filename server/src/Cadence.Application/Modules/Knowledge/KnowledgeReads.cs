using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Knowledge;

/// <summary>
/// The single read path for knowledge entries.
/// </summary>
/// <remarks>
/// Every endpoint that returns an entry — the list, the recently-opened rail, the response to each
/// write — projects through here, so no two of them can disagree about the same row.
/// </remarks>
internal static class KnowledgeReads
{
    /// <summary>The property name of the tag collection's backing field.</summary>
    /// <remarks>
    /// <c>KnowledgeItem.Tags</c> is <c>Ignore</c>d in the EF configuration — the mapped member is the
    /// private field — so a query has to name the field. Naming the public property compiles and then
    /// fails at translation time.
    /// </remarks>
    public const string TagsField = "_tags";

    public static Error NotFound =>
        Error.NotFound("knowledge_item.not_found", "That entry could not be found.");

    public static async Task<Result<KnowledgeItemDto>> LoadAsync(
        ICadenceDbContext context,
        Guid knowledgeItemId,
        CancellationToken cancellationToken)
    {
        // Filtered before projecting: a predicate over the projected DTO has to be translated against
        // a constructor call, which EF cannot do.
        var item = await Project(
                context.KnowledgeItems.AsNoTracking().Where(row => row.Id == knowledgeItemId))
            .FirstOrDefaultAsync(cancellationToken);

        return item is null ? Result.Failure<KnowledgeItemDto>(NotFound) : Result.Success(item);
    }

    public static IQueryable<KnowledgeItemDto> Project(IQueryable<KnowledgeItem> source) =>
        source.Select(item => new KnowledgeItemDto(
            item.Id,
            item.Title,
            item.Kind,
            item.Category,
            item.Excerpt,
            EF.Property<List<string>>(item, TagsField),
            item.IsFavorite,
            item.OwnerId,
            item.SourceId,
            item.SourceUrl,
            item.LastOpenedAt,
            item.CreatedAt,
            item.UpdatedAt));

    /// <summary>
    /// Applies the knowledge filters.
    /// </summary>
    /// <remarks>
    /// No tenant predicate: <c>KnowledgeItem</c> is <c>ITenantScoped</c>, so the global filter has
    /// already scoped this. Writing one by hand is how the filter eventually gets relied on somewhere
    /// that forgot it (§3.3).
    /// </remarks>
    public static IQueryable<KnowledgeItem> Filtered(ICadenceDbContext context, KnowledgeQuery query)
    {
        var items = context.KnowledgeItems.AsNoTracking();

        if (query.Kind is { Count: > 0 })
        {
            items = items.Where(item => query.Kind.Contains(item.Kind));
        }

        if (query.Category is { Count: > 0 })
        {
            items = items.Where(item => query.Category.Contains(item.Category));
        }

        if (query.OwnerId is { } ownerId)
        {
            items = items.Where(item => item.OwnerId == ownerId);
        }

        if (query.FavoritesOnly)
        {
            items = items.Where(item => item.IsFavorite);
        }

        if (query.Tags is { Count: > 0 })
        {
            items = items.Where(item =>
                EF.Property<List<string>>(item, TagsField).Any(tag => query.Tags.Contains(tag)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // lower() + Contains rather than Npgsql's ILike: Application is provider-neutral and an
            // architecture test fails the build if Npgsql appears here.
            var term = query.Search.Trim().ToLowerInvariant();

            items = items.Where(item =>
                item.Title.ToLower().Contains(term)
                || item.Excerpt.ToLower().Contains(term)
                || item.Category.ToLower().Contains(term)
                || EF.Property<List<string>>(item, TagsField).Any(tag => tag.Contains(term)));
        }

        return items;
    }

    /// <summary>
    /// Orders the result, newest first by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Never-opened entries sort last, in both directions.</b> Postgres puts nulls first on a
    /// descending sort, and the client's "recently opened" rail asks for
    /// <c>sortBy=lastOpenedAt&amp;sortDir=desc&amp;pageSize=4</c> and then drops the entries whose
    /// <c>lastOpenedAt</c> is null. With nulls leading, those four rows are always the four that were
    /// never opened, the rail filters all of them away, and it renders empty forever — while the query
    /// and the client code both look correct in isolation.
    /// </para>
    /// <para>
    /// Every sort ends on <c>Id</c>. Without a tiebreaker, rows with equal sort keys have no defined
    /// order, so one can appear on two pages while another never appears at all.
    /// </para>
    /// </remarks>
    public static IQueryable<KnowledgeItem> Sorted(
        IQueryable<KnowledgeItem> items,
        KnowledgeQuery query)
    {
        var ascending = query.SortDir == SortDirection.Asc;

        return query.SortBy?.ToLowerInvariant() switch
        {
            "title" => ascending
                ? items.OrderBy(item => item.Title).ThenBy(item => item.Id)
                : items.OrderByDescending(item => item.Title).ThenBy(item => item.Id),

            "lastopenedat" => ascending
                ? items.OrderBy(item => item.LastOpenedAt == null)
                    .ThenBy(item => item.LastOpenedAt)
                    .ThenBy(item => item.Id)
                : items.OrderBy(item => item.LastOpenedAt == null)
                    .ThenByDescending(item => item.LastOpenedAt)
                    .ThenBy(item => item.Id),

            _ => ascending
                ? items.OrderBy(item => item.CreatedAt).ThenBy(item => item.Id)
                : items.OrderByDescending(item => item.CreatedAt).ThenBy(item => item.Id),
        };
    }

    public static async Task<PagedResult<KnowledgeItemDto>> PageAsync(
        ICadenceDbContext context,
        KnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        var filtered = Filtered(context, query);

        var total = await filtered.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<KnowledgeItemDto>.Empty(query.Page, query.PageSize);
        }

        var page = await Project(Sorted(filtered, query))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<KnowledgeItemDto>(page, total, query.Page, query.PageSize);
    }
}
