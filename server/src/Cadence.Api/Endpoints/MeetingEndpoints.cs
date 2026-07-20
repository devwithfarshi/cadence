using Cadence.Api.Common;
using Cadence.Api.Configuration;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Meetings;
using Cadence.Application.Modules.Summaries;
using Cadence.Application.Modules.Transcripts;
using Cadence.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Endpoints;

/// <summary>
/// Meetings: the list, the detail page, and the lifecycle actions around them.
/// </summary>
/// <remarks>
/// No route here checks ownership. Every meeting is visible to its whole workspace by design
/// (§5.4 — everyone can view meetings, everyone but a guest can edit them), and the tenant filter is
/// what makes an id from another workspace a 404 rather than a leak.
/// </remarks>
public static class MeetingEndpoints
{
    public static IEndpointRouteBuilder MapMeetingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/meetings")
            .WithTags("Meetings")
            .RequireAuthorization(AuthenticationConfiguration.RequireMember);

        group.MapGet("/", ListAsync)
            .WithName("ListMeetings")
            .WithSummary("The meetings list, filtered and paged")
            .WithDescription("Archived meetings are excluded unless includeArchived is set.")
            .Produces<PagedResult<MeetingSummaryDto>>(StatusCodes.Status200OK);

        // Declared before "/{meetingId:guid}" would be reached, though the guid constraint already
        // keeps the two apart — a literal segment that could also parse as a route parameter is a
        // routing bug waiting for someone to relax the constraint.
        group.MapGet("/history", HistoryAsync)
            .WithName("MeetingHistory")
            .WithSummary("Everything already held, newest first")
            .WithDescription("Defaults to finished meetings and includes archived ones.")
            .Produces<PagedResult<MeetingSummaryDto>>(StatusCodes.Status200OK);

        group.MapGet("/tags", TagsAsync)
            .WithName("ListMeetingTags")
            .WithSummary("Distinct tags across the workspace's meetings")
            .Produces<IReadOnlyList<string>>(StatusCodes.Status200OK);

        group.MapGet("/{meetingId:guid}", GetAsync)
            .WithName("GetMeeting")
            .WithSummary("One meeting, with its bookmarks")
            .Produces<MeetingDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .WithName("CreateMeeting")
            .WithSummary("Schedule a meeting")
            .WithDescription(
                "Participant ids that are not members of the workspace are ignored. The organizer "
                + "is always added as a participant.")
            .Produces<MeetingDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPatch("/{meetingId:guid}", UpdateAsync)
            .WithName("UpdateMeeting")
            .WithSummary("Edit a meeting's details")
            .WithDescription(
                "Omit participantIds to leave the attendee list alone; send an empty array to clear "
                + "it. Status and summary status are not editable — they reflect what happened.")
            .Produces<MeetingDetailDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("/{meetingId:guid}", DeleteAsync)
            .WithName("DeleteMeeting")
            .WithSummary("Delete a meeting")
            .WithDescription("Action items raised in it are unassigned from it, never deleted.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/bulk-delete", BulkDeleteAsync)
            .WithName("BulkDeleteMeetings")
            .WithSummary("Delete several meetings")
            .Produces<BulkResultDto>(StatusCodes.Status200OK);

        group.MapPost("/{meetingId:guid}/favorite", FavoriteAsync)
            .WithName("ToggleMeetingFavorite")
            .WithSummary("Toggle the favourite flag")
            .Produces<MeetingSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/archive", ArchiveAsync)
            .WithName("ArchiveMeetings")
            .WithSummary("Archive or unarchive meetings")
            .WithDescription("The count reports rows actually changed, not ids matched.")
            .Produces<BulkResultDto>(StatusCodes.Status200OK);

        group.MapPost("/{meetingId:guid}/duplicate", DuplicateAsync)
            .WithName("DuplicateMeeting")
            .WithSummary("Copy a meeting into a new scheduled one")
            .WithDescription(
                "The copy carries the title, attendees and tags. Recording, summary and bookmarks "
                + "are deliberately left behind.")
            .Produces<MeetingDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{meetingId:guid}/transcript", TranscriptAsync)
            .WithName("GetMeetingTranscript")
            .WithSummary("A meeting's transcript, in playback order")
            .WithDescription(
                "Offsets are seconds for playback; they are stored as milliseconds. `search` "
                + "filters to matching lines rather than paging.")
            .Produces<IReadOnlyList<TranscriptSegmentDto>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{meetingId:guid}/summary", SummaryAsync)
            .WithName("GetMeetingSummary")
            .WithSummary("A meeting's AI summary")
            .WithDescription(
                "404 when the meeting has no summary. Check the meeting's summaryStatus to tell "
                + "\"not generated yet\" from \"generation failed\".")
            .Produces<AiSummaryDto>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{meetingId:guid}/summary", RegenerateSummaryAsync)
            .WithName("RegenerateMeetingSummary")
            .WithSummary("Queue a fresh summarisation run")
            .WithDescription(
                "Returns 202 with a job id; summarisation takes seconds to minutes. Poll the "
                + "meeting's summaryStatus for the outcome. A failed run is reported as failed and "
                + "never replaced with invented content.")
            .Produces<JobAcceptedDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{meetingId:guid}/bookmarks", AddBookmarkAsync)
            .WithName("AddMeetingBookmark")
            .WithSummary("Flag a moment in the recording")
            .Produces<BookmarkDto>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [AsParameters] MeetingQueryParameters parameters)
    {
        if (!parameters.TryToQuery(out var query, out var error))
        {
            return BadFilter(context, error!);
        }

        var result = await sender.Send(new ListMeetingsQuery(query), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> HistoryAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [AsParameters] MeetingQueryParameters parameters)
    {
        if (!parameters.TryToQuery(out var query, out var error))
        {
            return BadFilter(context, error!);
        }

        var result = await sender.Send(new MeetingHistoryQuery(query), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    /// <summary>
    /// Reports an unusable filter value as a 400 naming it.
    /// </summary>
    /// <remarks>
    /// Dropping the filter and answering 200 would be worse than an error: the caller gets a page of
    /// unfiltered results that reads as a correct answer to a question they did not ask.
    /// </remarks>
    private static IResult BadFilter(HttpContext context, string detail) =>
        Result.Failure(Error.Validation("meeting.invalid_filter", detail)).ToProblem(context);

    private static async Task<IResult> TagsAsync(
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ListMeetingTagsQuery(), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> GetAsync(
        Guid meetingId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetMeetingQuery(meetingId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateMeetingRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new CreateMeetingCommand(request), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/meetings/{result.Value.Meeting.Id}", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> UpdateAsync(
        Guid meetingId,
        [FromBody] UpdateMeetingRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new UpdateMeetingCommand(meetingId, request), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> DeleteAsync(
        Guid meetingId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteMeetingCommand(meetingId), cancellationToken);

        return result.IsSuccess ? TypedResults.NoContent() : result.ToProblem(context);
    }

    private static async Task<IResult> BulkDeleteAsync(
        [FromBody] BulkIdsRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DeleteMeetingsCommand(request.Ids), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> FavoriteAsync(
        Guid meetingId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new ToggleFavoriteCommand(meetingId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> ArchiveAsync(
        [FromBody] ArchiveRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(
            new SetArchivedCommand(request.Ids, request.Archived),
            cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> DuplicateAsync(
        Guid meetingId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new DuplicateMeetingCommand(meetingId), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/meetings/{result.Value.Meeting.Id}", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> TranscriptAsync(
        Guid meetingId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken,
        [FromQuery] string? search = null)
    {
        var result = await sender.Send(new GetTranscriptQuery(meetingId, search), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> SummaryAsync(
        Guid meetingId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetSummaryQuery(meetingId), cancellationToken);

        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.ToProblem(context);
    }

    private static async Task<IResult> RegenerateSummaryAsync(
        Guid meetingId,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new RegenerateSummaryCommand(meetingId), cancellationToken);

        // 202, not 200: the work has been accepted, not done. Answering 200 with a summary would
        // mean holding the request open for the length of a model call.
        return result.IsSuccess
            ? TypedResults.Accepted($"/api/v1/meetings/{meetingId}/summary", result.Value)
            : result.ToProblem(context);
    }

    private static async Task<IResult> AddBookmarkAsync(
        Guid meetingId,
        [FromBody] AddBookmarkRequest request,
        HttpContext context,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(new AddBookmarkCommand(meetingId, request), cancellationToken);

        return result.IsSuccess
            ? TypedResults.Created(
                $"/api/v1/meetings/{meetingId}/bookmarks/{result.Value.Id}",
                result.Value)
            : result.ToProblem(context);
    }
}

/// <summary>
/// The meetings list filters, bound from the query string.
/// </summary>
/// <remarks>
/// A binding type rather than a dozen loose parameters, so the list and history endpoints cannot
/// drift apart — and so Swagger documents the filters once. Repeated values bind as arrays
/// (<c>?status=completed&amp;status=cancelled</c>), matching the client's array-valued filters.
/// </remarks>
public sealed record MeetingQueryParameters
{
    [FromQuery] public string? Search { get; init; }

    // Enum filters bind as strings, not as enums. Query-string binding bypasses the JSON converter
    // and parses case-sensitively, so `?status=completed` — the spelling every other surface of the
    // API uses — would be rejected as a malformed request. See EnumQuery.
    [FromQuery] public string[]? Status { get; init; }

    [FromQuery] public string[]? Platform { get; init; }

    [FromQuery] public string[]? SummaryStatus { get; init; }

    [FromQuery] public string[]? Tags { get; init; }

    [FromQuery] public Guid? ParticipantId { get; init; }

    [FromQuery] public bool? FavoritesOnly { get; init; }

    [FromQuery] public bool? IncludeArchived { get; init; }

    [FromQuery] public DateTimeOffset? From { get; init; }

    [FromQuery] public DateTimeOffset? To { get; init; }

    [FromQuery] public string? SortBy { get; init; }

    [FromQuery] public string? SortDir { get; init; }

    [FromQuery] public int? Page { get; init; }

    [FromQuery] public int? PageSize { get; init; }

    /// <summary>
    /// Converts to the query the handler takes, or explains which value was not understood.
    /// </summary>
    public bool TryToQuery(out MeetingQuery query, out string? error)
    {
        query = new MeetingQuery();

        if (!EnumQuery.TryParseAll<MeetingStatus>(Status, out var status, out var badStatus))
        {
            error = $"'{badStatus}' is not a meeting status. Expected one of: "
                + $"{EnumQuery.Allowed<MeetingStatus>()}.";
            return false;
        }

        if (!EnumQuery.TryParseAll<MeetingPlatform>(Platform, out var platform, out var badPlatform))
        {
            error = $"'{badPlatform}' is not a meeting platform. Expected one of: "
                + $"{EnumQuery.Allowed<MeetingPlatform>()}.";
            return false;
        }

        if (!EnumQuery.TryParseAll<SummaryStatus>(SummaryStatus, out var summary, out var badSummary))
        {
            error = $"'{badSummary}' is not a summary status. Expected one of: "
                + $"{EnumQuery.Allowed<SummaryStatus>()}.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SortDir)
            && !EnumQuery.TryParse<SortDirection>(SortDir, out _))
        {
            error = $"'{SortDir}' is not a sort direction. Expected one of: asc, desc.";
            return false;
        }

        EnumQuery.TryParse<SortDirection>(SortDir, out var sortDir);

        query = new MeetingQuery
        {
            Search = Search,
            Status = status,
            Platform = platform,
            SummaryStatus = summary,
            Tags = Tags,
            ParticipantId = ParticipantId,
            FavoritesOnly = FavoritesOnly ?? false,
            IncludeArchived = IncludeArchived ?? false,
            From = From,
            To = To,
            SortBy = SortBy,
            SortDir = string.IsNullOrWhiteSpace(SortDir) ? SortDirection.Desc : sortDir,
            // Page and PageSize clamp themselves in ListQuery; passing the raw value through is what
            // lets them.
            Page = Page ?? 1,
            PageSize = PageSize ?? ListQuery.DefaultPageSize,
        };

        error = null;
        return true;
    }
}
