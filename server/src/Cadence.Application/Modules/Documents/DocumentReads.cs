using System.Linq.Expressions;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Library;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Documents;

/// <summary>
/// The single read path for documents.
/// </summary>
/// <remarks>
/// Every endpoint that returns a document — the list, a rename, a favourite toggle — projects through
/// here, so no two of them can disagree about the same row.
/// </remarks>
internal static class DocumentReads
{
    /// <summary>The property name of the tag collection's backing field.</summary>
    /// <remarks>
    /// <c>Document.Tags</c> is <c>Ignore</c>d in the EF configuration — the mapped member is the
    /// private field — so a query has to name the field. Naming the public property compiles and then
    /// fails at translation time.
    /// </remarks>
    private const string TagsField = "_tags";

    public static Error NotFound =>
        Error.NotFound("document.not_found", "That document could not be found.");

    public static async Task<Result<DocumentDto>> LoadAsync(
        ICadenceDbContext context,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        // Filtered before projecting: a predicate over the projected DTO has to be translated against
        // a constructor call, which EF cannot do.
        var document = await Project(
                context.Documents.AsNoTracking().Where(row => row.Id == documentId))
            .FirstOrDefaultAsync(cancellationToken);

        return document is null ? Result.Failure<DocumentDto>(NotFound) : Result.Success(document);
    }

    public static IQueryable<DocumentDto> Project(IQueryable<Document> source) =>
        source.Select(document => new DocumentDto(
            document.Id,
            document.Name,
            document.Type,
            document.SizeBytes,
            document.OwnerId,
            document.ProcessingStatus,
            document.Excerpt,
            EF.Property<List<string>>(document, TagsField),
            document.IsFavorite,
            document.MeetingId,
            document.CreatedAt,
            document.UpdatedAt));

    /// <summary>
    /// Applies the library filters.
    /// </summary>
    /// <remarks>
    /// No tenant predicate: <c>Document</c> is <c>ITenantScoped</c>, so the global filter has already
    /// scoped this. Writing one by hand is how the filter eventually gets relied on somewhere that
    /// forgot it (§3.3).
    /// </remarks>
    public static IQueryable<Document> Filtered(ICadenceDbContext context, DocumentQuery query)
    {
        var documents = context.Documents.AsNoTracking();

        if (query.Type is { Count: > 0 })
        {
            documents = documents.Where(document => query.Type.Contains(document.Type));
        }

        if (query.ProcessingStatus is { Count: > 0 })
        {
            documents = documents.Where(document =>
                query.ProcessingStatus.Contains(document.ProcessingStatus));
        }

        if (query.OwnerId is { } ownerId)
        {
            documents = documents.Where(document => document.OwnerId == ownerId);
        }

        if (query.MeetingId is { } meetingId)
        {
            documents = documents.Where(document => document.MeetingId == meetingId);
        }

        if (query.FavoritesOnly)
        {
            documents = documents.Where(document => document.IsFavorite);
        }

        if (query.Tags is { Count: > 0 })
        {
            documents = documents.Where(document =>
                EF.Property<List<string>>(document, TagsField).Any(tag => query.Tags.Contains(tag)));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            // lower() + Contains rather than Npgsql's ILike: Application is provider-neutral and an
            // architecture test fails the build if Npgsql appears here.
            var term = query.Search.Trim().ToLowerInvariant();

            documents = documents.Where(document =>
                document.Name.ToLower().Contains(term)
                || document.Excerpt.ToLower().Contains(term)
                || EF.Property<List<string>>(document, TagsField).Any(tag => tag.Contains(term)));
        }

        return documents;
    }

    /// <summary>
    /// Orders the result, newest first by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>type</c> is ranked rather than sorted on its column. It is stored as text (§3.4), so
    /// <c>ORDER BY type</c> is alphabetical — <c>csv, docx, image, pdf, pptx, txt</c> — which is not
    /// wrong so much as arbitrary. Ranking groups the document formats people actually browse for
    /// ahead of the loose text and images.
    /// </para>
    /// <para>
    /// Every sort ends on <c>Id</c>. Without a tiebreaker, rows with equal sort keys have no defined
    /// order, so one can appear on two pages while another never appears at all.
    /// </para>
    /// </remarks>
    public static IQueryable<Document> Sorted(IQueryable<Document> documents, DocumentQuery query)
    {
        var ascending = query.SortDir == SortDirection.Asc;

        return query.SortBy?.ToLowerInvariant() switch
        {
            "name" => ascending
                ? documents.OrderBy(document => document.Name).ThenBy(document => document.Id)
                : documents.OrderByDescending(document => document.Name).ThenBy(document => document.Id),

            "sizebytes" => ascending
                ? documents.OrderBy(document => document.SizeBytes).ThenBy(document => document.Id)
                : documents.OrderByDescending(document => document.SizeBytes).ThenBy(document => document.Id),

            "type" => ascending
                ? documents.OrderBy(TypeRank).ThenBy(document => document.Name).ThenBy(document => document.Id)
                : documents.OrderByDescending(TypeRank).ThenBy(document => document.Name).ThenBy(document => document.Id),

            _ => ascending
                ? documents.OrderBy(document => document.CreatedAt).ThenBy(document => document.Id)
                : documents.OrderByDescending(document => document.CreatedAt).ThenBy(document => document.Id),
        };
    }

    /// <summary>
    /// Groups the formats a document library is mostly made of ahead of the rest.
    /// </summary>
    /// <remarks>
    /// An <see cref="Expression"/> field rather than a method: a call to a user-defined method inside
    /// a query cannot be translated, and EF's only recourse is to fail at runtime. Held once so the
    /// ascending and descending branches provably rank the same way.
    /// </remarks>
    private static readonly Expression<Func<Document, int>> TypeRank =
        document => document.Type == DocumentType.Pdf ? 0
            : document.Type == DocumentType.Docx ? 1
            : document.Type == DocumentType.Pptx ? 2
            : document.Type == DocumentType.Csv ? 3
            : document.Type == DocumentType.Txt ? 4
            : 5;

    public static async Task<PagedResult<DocumentDto>> PageAsync(
        ICadenceDbContext context,
        DocumentQuery query,
        CancellationToken cancellationToken)
    {
        var filtered = Filtered(context, query);

        var total = await filtered.CountAsync(cancellationToken);

        if (total == 0)
        {
            return PagedResult<DocumentDto>.Empty(query.Page, query.PageSize);
        }

        var page = await Project(Sorted(filtered, query))
            .Skip(query.Skip)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<DocumentDto>(page, total, query.Page, query.PageSize);
    }
}
