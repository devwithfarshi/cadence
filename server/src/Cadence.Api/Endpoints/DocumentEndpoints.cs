using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Documents;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// Documents: the library list, the signed upload handshake, and the selection toolbar.
/// </summary>
/// <remarks>
/// Documents are visible to the whole workspace, like meetings and tasks (§5.4). The tenant filter is
/// what makes an id from another workspace a 404 rather than a leak, so no route here checks
/// ownership.
/// </remarks>
public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/documents")
            .WithTags("Documents")
            .RequireAuthorization(AuthenticationConfiguration.RequireMember);

        group.MapGet("/", ListAsync)
            .WithName("ListDocuments")
            .WithSummary("The document list, filtered and paged")
            .Produces<PagedResult<DocumentDto>>(StatusCodes.Status200OK);

        // Declared before "/{documentId:guid}" would be reached. The guid constraint already keeps
        // them apart, but a literal segment that could also bind as a route parameter is a routing
        // bug waiting for someone to relax the constraint.
        group.MapGet("/tags", TagsAsync)
            .WithName("ListDocumentTags")
            .WithSummary("Distinct tags across the workspace's documents")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);

        group.MapPost("/upload-signature", UploadSignatureAsync)
            .WithName("CreateUploadSignature")
            .WithSummary("Permission to upload one file directly to the storage provider")
            .WithDescription(
                "The browser posts the file to `uploadUrl` itself — it never passes through this "
                + "API. Send `file` plus `apiKey`, `signature` and every entry of `parameters` as "
                + "multipart form data, unaltered: the signature covers them, so the provider "
                + "rejects anything that has been changed. Then call POST /documents with the "
                + "returned `storageKey`.")
            .Produces<UploadSignatureDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/", RegisterAsync)
            .WithName("RegisterDocument")
            .WithSummary("Record a file that has finished uploading")
            .WithDescription(
                "The size is read back from the provider and the owner comes from the token — "
                + "neither is accepted from the body. An upload signed for a different file type, "
                + "or one that is over the size limit, is refused and the stored file destroyed. "
                + "The document starts as `processing`.")
            .Produces<DocumentDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/bulk-delete", BulkDeleteAsync)
            .WithName("BulkDeleteDocuments")
            .WithSummary("Delete several documents")
            .WithDescription("The stored files are destroyed by a background job.")
            .Produces<BulkResultDto>(StatusCodes.Status200OK);

        group.MapGet("/{documentId:guid}", GetAsync)
            .WithName("GetDocument")
            .WithSummary("One document")
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{documentId:guid}/download", DownloadAsync)
            .WithName("GetDocumentDownload")
            .WithSummary("A short-lived link to the file itself")
            .WithDescription(
                "Documents are private, so the URL is signed and expires. It is minted per request "
                + "rather than stored on the row.")
            .Produces<DocumentDownloadDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{documentId:guid}", RenameAsync)
            .WithName("RenameDocument")
            .WithSummary("Rename a document")
            .WithDescription(
                "The type is re-derived from the new extension. The stored file is untouched, so a "
                + "download still returns the bytes that were uploaded.")
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{documentId:guid}/favorite", FavoriteAsync)
            .WithName("ToggleDocumentFavorite")
            .WithSummary("Toggle a document's favourite flag")
            .Produces<DocumentDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{documentId:guid}", DeleteAsync)
            .WithName("DeleteDocument")
            .WithSummary("Delete a document")
            .WithDescription("The stored file is destroyed by a background job.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [AsParameters] DocumentQueryParameters parameters)
    {
        if (!parameters.TryToQuery(out var query, out var error))
        {
            return Result.Failure(Error.Validation("document.invalid_filter", error!))
                .ToProblem(context);
        }

        var result = await sender.Send(new ListDocumentsQuery(query), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> TagsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListDocumentTagsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> UploadSignatureAsync(
        [FromBody] UploadSignatureRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateUploadSignatureCommand(request), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> RegisterAsync(
        [FromBody] RegisterDocumentRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RegisterDocumentCommand(request), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/documents/{result.Value.Id}", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> GetAsync(
        Guid documentId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetDocumentQuery(documentId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> DownloadAsync(
        Guid documentId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetDocumentDownloadQuery(documentId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> RenameAsync(
        Guid documentId,
        [FromBody] RenameDocumentRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new RenameDocumentCommand(documentId, request.Name),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> FavoriteAsync(
        Guid documentId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ToggleDocumentFavoriteCommand(documentId),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> DeleteAsync(
        Guid documentId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteDocumentCommand(documentId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    private static async Task<IResult> BulkDeleteAsync(
        [FromBody] BulkIdsRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new BulkDeleteDocumentsCommand(request.Ids), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }
}

/// <summary>
/// The document list filters, bound from the query string.
/// </summary>
/// <remarks>
/// Enum filters bind as strings, not as enums: query-string binding bypasses the JSON converter and
/// parses case-sensitively, so <c>?processingStatus=in_progress</c> — the spelling the rest of the
/// API uses — would otherwise be rejected as malformed. See <c>EnumQuery</c>.
/// </remarks>
public sealed record DocumentQueryParameters
{
    [FromQuery] public string? Search { get; init; }

    [FromQuery] public string[]? Type { get; init; }

    [FromQuery] public string[]? ProcessingStatus { get; init; }

    [FromQuery] public Guid? OwnerId { get; init; }

    [FromQuery] public Guid? MeetingId { get; init; }

    [FromQuery] public string[]? Tags { get; init; }

    [FromQuery] public bool? FavoritesOnly { get; init; }

    [FromQuery] public string? SortBy { get; init; }

    [FromQuery] public string? SortDir { get; init; }

    [FromQuery] public int? Page { get; init; }

    [FromQuery] public int? PageSize { get; init; }

    /// <summary>
    /// Converts to the query the handler takes, or explains which value was not understood.
    /// </summary>
    public bool TryToQuery(out DocumentQuery query, out string? error)
    {
        query = new DocumentQuery();

        if (!EnumQuery.TryParseAll<DocumentType>(Type, out var type, out var badType))
        {
            error = $"'{badType}' is not a document type. Expected one of: "
                + $"{EnumQuery.Allowed<DocumentType>()}.";
            return false;
        }

        if (!EnumQuery.TryParseAll<ProcessingStatus>(
                ProcessingStatus,
                out var processingStatus,
                out var badStatus))
        {
            error = $"'{badStatus}' is not a processing status. Expected one of: "
                + $"{EnumQuery.Allowed<ProcessingStatus>()}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SortDir)
            && !EnumQuery.TryParse<SortDirection>(SortDir, out _))
        {
            error = $"'{SortDir}' is not a sort direction. Expected one of: asc, desc.";
            return false;
        }

        EnumQuery.TryParse<SortDirection>(SortDir, out var sortDir);

        // Newest first unless the caller says otherwise — a library opens on what was just added.
        // Sorting by name or type is the one case where ascending is the natural default, and the
        // client sends the direction explicitly for those.
        var defaultDir = SortBy?.ToLowerInvariant() is "name" or "type"
            ? SortDirection.Asc
            : SortDirection.Desc;

        query = new DocumentQuery
        {
            Search = Search,
            Type = type,
            ProcessingStatus = processingStatus,
            OwnerId = OwnerId,
            MeetingId = MeetingId,
            Tags = Tags,
            FavoritesOnly = FavoritesOnly ?? false,
            SortBy = SortBy,
            SortDir = string.IsNullOrWhiteSpace(SortDir) ? defaultDir : sortDir,
            Page = Page ?? 1,
            PageSize = PageSize ?? ListQuery.DefaultPageSize,
        };

        error = null;
        return true;
    }
}
