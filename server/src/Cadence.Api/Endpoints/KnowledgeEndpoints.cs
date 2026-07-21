using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Knowledge;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// The knowledge base: curated notes, summaries, document pointers and links.
/// </summary>
/// <remarks>
/// Entries are visible to the whole workspace, like meetings, tasks and documents (§5.4). The tenant
/// filter is what makes an id from another workspace a 404 rather than a leak, so no route here
/// checks ownership.
/// </remarks>
public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/knowledge")
            .WithTags("Knowledge")
            .RequireAuthorization(AuthenticationConfiguration.RequireMember);

        group.MapGet("/", ListAsync)
            .WithName("ListKnowledge")
            .WithSummary("The knowledge list, filtered and paged")
            .WithDescription(
                "Sorting by `lastOpenedAt` puts never-opened entries last in both directions, so "
                + "the recently-opened rail is not filled with entries nobody has read.")
            .Produces<PagedResult<KnowledgeItemDto>>(StatusCodes.Status200OK);

        // Declared before "/{knowledgeItemId:guid}" would be reached. The guid constraint already
        // keeps them apart, but a literal segment that could also bind as a route parameter is a
        // routing bug waiting for someone to relax the constraint.
        group.MapGet("/facets", FacetsAsync)
            .WithName("GetKnowledgeFacets")
            .WithSummary("The categories and tags in use, for the filter menus")
            .Produces<KnowledgeFacetsDto>(StatusCodes.Status200OK);

        group.MapPost("/", CreateAsync)
            .WithName("CreateKnowledgeItem")
            .WithSummary("File a knowledge entry")
            .WithDescription(
                "The owner is taken from the token, never the body. A `sourceId` must name a "
                + "meeting or document visible in this workspace, and a `link` entry carries a "
                + "`sourceUrl` instead of a `sourceId`.")
            .Produces<KnowledgeItemDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/bulk-delete", BulkDeleteAsync)
            .WithName("BulkDeleteKnowledgeItems")
            .WithSummary("Delete several entries")
            .Produces<BulkResultDto>(StatusCodes.Status200OK);

        group.MapGet("/{knowledgeItemId:guid}", GetAsync)
            .WithName("GetKnowledgeItem")
            .WithSummary("One entry")
            .Produces<KnowledgeItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{knowledgeItemId:guid}/favorite", FavoriteAsync)
            .WithName("ToggleKnowledgeFavorite")
            .WithSummary("Toggle an entry's favourite flag")
            .Produces<KnowledgeItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{knowledgeItemId:guid}/open", OpenAsync)
            .WithName("MarkKnowledgeItemOpened")
            .WithSummary("Record that the entry was opened")
            .WithDescription("Drives the recently-opened rail. Returns no body.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{knowledgeItemId:guid}", DeleteAsync)
            .WithName("DeleteKnowledgeItem")
            .WithSummary("Delete an entry")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [AsParameters] KnowledgeQueryParameters parameters)
    {
        if (!parameters.TryToQuery(out var query, out var error))
        {
            return Result.Failure(Error.Validation("knowledge_item.invalid_filter", error!))
                .ToProblem(context);
        }

        var result = await sender.Send(new ListKnowledgeQuery(query), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> FacetsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetKnowledgeFacetsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateKnowledgeItemRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateKnowledgeItemCommand(request), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/knowledge/{result.Value.Id}", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> GetAsync(
        Guid knowledgeItemId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetKnowledgeItemQuery(knowledgeItemId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> FavoriteAsync(
        Guid knowledgeItemId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new ToggleKnowledgeFavoriteCommand(knowledgeItemId),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> OpenAsync(
        Guid knowledgeItemId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new MarkKnowledgeItemOpenedCommand(knowledgeItemId),
            cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    private static async Task<IResult> DeleteAsync(
        Guid knowledgeItemId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new DeleteKnowledgeItemCommand(knowledgeItemId),
            cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    private static async Task<IResult> BulkDeleteAsync(
        [FromBody] BulkIdsRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new BulkDeleteKnowledgeItemsCommand(request.Ids),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }
}

/// <summary>
/// The knowledge list filters, bound from the query string.
/// </summary>
/// <remarks>
/// Enum filters bind as strings, not as enums: query-string binding bypasses the JSON converter and
/// parses case-sensitively, so <c>?kind=meeting_note</c> — the spelling the rest of the API uses —
/// would otherwise be rejected as malformed. See <c>EnumQuery</c>.
/// </remarks>
public sealed record KnowledgeQueryParameters
{
    [FromQuery] public string? Search { get; init; }

    [FromQuery] public string[]? Kind { get; init; }

    [FromQuery] public string[]? Category { get; init; }

    [FromQuery] public Guid? OwnerId { get; init; }

    [FromQuery] public string[]? Tags { get; init; }

    [FromQuery] public bool? FavoritesOnly { get; init; }

    [FromQuery] public string? SortBy { get; init; }

    [FromQuery] public string? SortDir { get; init; }

    [FromQuery] public int? Page { get; init; }

    [FromQuery] public int? PageSize { get; init; }

    /// <summary>
    /// Converts to the query the handler takes, or explains which value was not understood.
    /// </summary>
    public bool TryToQuery(out KnowledgeQuery query, out string? error)
    {
        query = new KnowledgeQuery();

        if (!EnumQuery.TryParseAll<KnowledgeItemKind>(Kind, out var kind, out var badKind))
        {
            error = $"'{badKind}' is not a knowledge entry kind. Expected one of: "
                + $"{EnumQuery.Allowed<KnowledgeItemKind>()}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SortDir)
            && !EnumQuery.TryParse<SortDirection>(SortDir, out _))
        {
            error = $"'{SortDir}' is not a sort direction. Expected one of: asc, desc.";
            return false;
        }

        EnumQuery.TryParse<SortDirection>(SortDir, out var sortDir);

        query = new KnowledgeQuery
        {
            Search = Search,
            Kind = kind,
            Category = Category,
            OwnerId = OwnerId,
            Tags = Tags,
            FavoritesOnly = FavoritesOnly ?? false,
            SortBy = SortBy,
            // Descending by default, matching the client: both the default "newest first" and the
            // recently-opened rail want the most recent value at the top.
            SortDir = string.IsNullOrWhiteSpace(SortDir) ? SortDirection.Desc : sortDir,
            Page = Page ?? 1,
            PageSize = PageSize ?? ListQuery.DefaultPageSize,
        };

        error = null;
        return true;
    }
}
