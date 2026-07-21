using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Modules.Documents;

/// <summary>
/// The document list, filtered and paged.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>DocumentQuery</c> 1:1 (§6), plus <c>MeetingId</c> — the meeting detail
/// page needs the files attached to one meeting, and the client's mock reaches that by loading every
/// document and filtering in the browser.
/// </remarks>
public sealed record DocumentQuery : ListQuery
{
    public IReadOnlyList<DocumentType>? Type { get; init; }

    public IReadOnlyList<ProcessingStatus>? ProcessingStatus { get; init; }

    public Guid? OwnerId { get; init; }

    public Guid? MeetingId { get; init; }

    public IReadOnlyList<string>? Tags { get; init; }

    public bool FavoritesOnly { get; init; }
}

public sealed record ListDocumentsQuery(DocumentQuery Query)
    : IQuery<Result<PagedResult<DocumentDto>>>;

/// <summary>Distinct tags across the workspace's documents, for the filter menus.</summary>
public sealed record ListDocumentTagsQuery : IQuery<Result<IReadOnlyList<string>>>;

public sealed class ListDocumentsHandler(ICadenceDbContext context)
    : IQueryHandler<ListDocumentsQuery, Result<PagedResult<DocumentDto>>>
{
    public async ValueTask<Result<PagedResult<DocumentDto>>> Handle(
        ListDocumentsQuery query,
        CancellationToken cancellationToken) =>
        Result.Success(await DocumentReads.PageAsync(context, query.Query, cancellationToken));
}

public sealed class ListDocumentTagsHandler(ICadenceDbContext context)
    : IQueryHandler<ListDocumentTagsQuery, Result<IReadOnlyList<string>>>
{
    public async ValueTask<Result<IReadOnlyList<string>>> Handle(
        ListDocumentTagsQuery query,
        CancellationToken cancellationToken)
    {
        // Flattened in the database rather than by loading every document's tag array and unioning in
        // memory — this feeds a filter menu, and the menu should not cost a full table read.
        var tags = await context.Documents
            .AsNoTracking()
            .SelectMany(document => EF.Property<List<string>>(document, "_tags"))
            .Distinct()
            .OrderBy(tag => tag)
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<string>>(tags);
    }
}
