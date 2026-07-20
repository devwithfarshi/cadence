using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.ActionItems;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// Action items: the Tasks list, the board, and the bulk operations behind the selection toolbar.
/// </summary>
/// <remarks>
/// Tasks are visible to the whole workspace, like meetings (§5.4). The tenant filter is what makes
/// an id from another workspace a 404 rather than a leak, so no route here checks ownership.
/// </remarks>
public static class ActionItemEndpoints
{
    public static IEndpointRouteBuilder MapActionItemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/action-items")
            .WithTags("Action items")
            .RequireAuthorization(AuthenticationConfiguration.RequireMember);

        group.MapGet("/", ListAsync)
            .WithName("ListActionItems")
            .WithSummary("The task list, filtered and paged")
            .WithDescription(
                "Sorting by priority ranks by severity (low → urgent) rather than alphabetically, "
                + "and by status follows the workflow (todo → in progress → blocked → done). "
                + "Undated tasks sort last in both directions.")
            .Produces<PagedResult<ActionItemDto>>(StatusCodes.Status200OK);

        // Declared before "/{actionItemId:guid}" would be reached. The guid constraint already keeps
        // them apart, but a literal segment that could also bind as a route parameter is a routing
        // bug waiting for someone to relax the constraint.
        group.MapGet("/counts", CountsAsync)
            .WithName("ActionItemCounts")
            .WithSummary("The numbers behind the view tabs and board headers")
            .WithDescription(
                "Counted in one pass, so a badge cannot disagree with the list beneath it. "
                + "\"Assigned\" and \"created\" are relative to the calling user.")
            .Produces<ActionItemCountsDto>(StatusCodes.Status200OK);

        group.MapGet("/tags", TagsAsync)
            .WithName("ListActionItemTags")
            .WithSummary("Distinct tags across the workspace's tasks")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);

        group.MapGet("/{actionItemId:guid}", GetAsync)
            .WithName("GetActionItem")
            .WithSummary("One task")
            .Produces<ActionItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateActionItem")
            .WithSummary("File a task")
            .WithDescription(
                "The creator is taken from the token, never the body. An assignee must be a member "
                + "of the workspace; a sourceSegmentId that does not belong to the given meeting is "
                + "dropped rather than stored.")
            .Produces<ActionItemDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{actionItemId:guid}", UpdateAsync)
            .WithName("UpdateActionItem")
            .WithSummary("Change one or more fields of a task")
            .WithDescription(
                "A true partial update: omitted fields are left alone, and an explicit null clears "
                + "the field. `{\"assigneeId\": null}` unassigns; a body without `assigneeId` does "
                + "not. completedAt is derived from status and cannot be set.")
            .Produces<ActionItemDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{actionItemId:guid}", DeleteAsync)
            .WithName("DeleteActionItem")
            .WithSummary("Delete a task")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/bulk-status", BulkStatusAsync)
            .WithName("BulkSetActionItemStatus")
            .WithSummary("Move several tasks to one status")
            .WithDescription("The count reports rows actually changed, not ids matched.")
            .Produces<BulkResultDto>(StatusCodes.Status200OK);

        group.MapPost("/bulk-assign", BulkAssignAsync)
            .WithName("BulkAssignActionItems")
            .WithSummary("Reassign several tasks")
            .WithDescription("A null assigneeId unassigns them.")
            .Produces<BulkResultDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/bulk-priority", BulkPriorityAsync)
            .WithName("BulkSetActionItemPriority")
            .WithSummary("Set the priority of several tasks")
            .Produces<BulkResultDto>(StatusCodes.Status200OK);

        group.MapPost("/bulk-delete", BulkDeleteAsync)
            .WithName("BulkDeleteActionItems")
            .WithSummary("Delete several tasks")
            .Produces<BulkResultDto>(StatusCodes.Status200OK);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [AsParameters] ActionItemQueryParameters parameters)
    {
        if (!parameters.TryToQuery(out var query, out var error))
        {
            return Result.Failure(Error.Validation("action_item.invalid_filter", error!))
                .ToProblem(context);
        }

        var result = await sender.Send(new ListActionItemsQuery(query), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> CountsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ActionItemCountsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> TagsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListActionItemTagsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> GetAsync(
        Guid actionItemId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetActionItemQuery(actionItemId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateActionItemRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateActionItemCommand(request), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/action-items/{result.Value.Id}", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> UpdateAsync(
        Guid actionItemId,
        [FromBody] UpdateActionItemRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new UpdateActionItemCommand(actionItemId, request),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> DeleteAsync(
        Guid actionItemId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteActionItemCommand(actionItemId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    private static async Task<IResult> BulkStatusAsync(
        [FromBody] BulkStatusRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new BulkSetStatusCommand(request.Ids, request.Status),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> BulkAssignAsync(
        [FromBody] BulkAssignRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new BulkAssignCommand(request.Ids, request.AssigneeId),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> BulkPriorityAsync(
        [FromBody] BulkPriorityRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new BulkSetPriorityCommand(request.Ids, request.Priority),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> BulkDeleteAsync(
        [FromBody] BulkIdsRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new BulkDeleteActionItemsCommand(request.Ids),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }
}

/// <summary>
/// The task list filters, bound from the query string.
/// </summary>
/// <remarks>
/// Enum filters bind as strings, not as enums: query-string binding bypasses the JSON converter and
/// parses case-sensitively, so <c>?status=in_progress</c> — the spelling the rest of the API uses —
/// would otherwise be rejected as malformed. See <c>EnumQuery</c>.
/// </remarks>
public sealed record ActionItemQueryParameters
{
    [FromQuery] public string? Search { get; init; }

    [FromQuery] public string[]? Status { get; init; }

    [FromQuery] public string[]? Priority { get; init; }

    [FromQuery] public Guid? AssigneeId { get; init; }

    [FromQuery] public Guid? CreatorId { get; init; }

    [FromQuery] public Guid? MeetingId { get; init; }

    [FromQuery] public string[]? Tags { get; init; }

    [FromQuery] public bool? UnassignedOnly { get; init; }

    [FromQuery] public bool? OverdueOnly { get; init; }

    [FromQuery] public DateTimeOffset? DueFrom { get; init; }

    [FromQuery] public DateTimeOffset? DueTo { get; init; }

    [FromQuery] public string? SortBy { get; init; }

    [FromQuery] public string? SortDir { get; init; }

    [FromQuery] public int? Page { get; init; }

    [FromQuery] public int? PageSize { get; init; }

    /// <summary>
    /// Converts to the query the handler takes, or explains which value was not understood.
    /// </summary>
    public bool TryToQuery(out ActionItemQuery query, out string? error)
    {
        query = new ActionItemQuery();

        if (!EnumQuery.TryParseAll<ActionItemStatus>(Status, out var status, out var badStatus))
        {
            error = $"'{badStatus}' is not a task status. Expected one of: "
                + $"{EnumQuery.Allowed<ActionItemStatus>()}.";
            return false;
        }

        if (!EnumQuery.TryParseAll<ActionItemPriority>(Priority, out var priority, out var badPriority))
        {
            error = $"'{badPriority}' is not a task priority. Expected one of: "
                + $"{EnumQuery.Allowed<ActionItemPriority>()}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SortDir)
            && !EnumQuery.TryParse<SortDirection>(SortDir, out _))
        {
            error = $"'{SortDir}' is not a sort direction. Expected one of: asc, desc.";
            return false;
        }

        EnumQuery.TryParse<SortDirection>(SortDir, out var sortDir);

        query = new ActionItemQuery
        {
            Search = Search,
            Status = status,
            Priority = priority,
            AssigneeId = AssigneeId,
            CreatorId = CreatorId,
            MeetingId = MeetingId,
            Tags = Tags,
            UnassignedOnly = UnassignedOnly ?? false,
            OverdueOnly = OverdueOnly ?? false,
            DueFrom = DueFrom,
            DueTo = DueTo,
            SortBy = SortBy,
            // Ascending by default, unlike meetings: the task list is sorted by due date, and the
            // nearest deadline is what someone opening the page needs to see first.
            SortDir = string.IsNullOrWhiteSpace(SortDir) ? SortDirection.Asc : sortDir,
            Page = Page ?? 1,
            PageSize = PageSize ?? ListQuery.DefaultPageSize,
        };

        error = null;
        return true;
    }
}
