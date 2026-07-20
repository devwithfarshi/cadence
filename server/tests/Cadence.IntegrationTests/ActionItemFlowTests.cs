using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.ActionItems;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Meetings;
using Cadence.Application.Modules.Organizations;
using Cadence.Domain.Enums;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// Action items: creation, partial updates, filtering, counts and the bulk operations.
/// </summary>
/// <remarks>
/// Two things here are worth more than the rest. The <b>patch semantics</b> — an omitted field and
/// an explicit null must mean different things — because collapsing them makes it impossible to edit
/// a title without also clearing the assignee. And <b>ordering by priority</b>, because the column
/// is text and the obvious implementation sorts <c>high, low, medium, urgent</c> while looking
/// entirely correct.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ActionItemFlowTests
{
    private readonly AuthFixture _fixture;

    public ActionItemFlowTests(AuthFixture fixture) => _fixture = fixture;

    /* ---------------------------------------------------------------------- */
    /* Creating                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Create_FilesTheTaskAgainstTheCaller()
    {
        var (client, session) = await SignInAsync();

        var task = await CreateTaskAsync(client, "Write the rollback plan");

        // The creator comes from the token. The client's mock takes one as input because it has no
        // session to read; accepting it here would let a member file work under someone else's name.
        task.CreatorId.ShouldBe(session.User.Id);
        task.Status.ShouldBe(ActionItemStatus.Todo);
        task.Priority.ShouldBe(ActionItemPriority.Medium);
        task.AssigneeId.ShouldBeNull();
        task.CompletedAt.ShouldBeNull();
        task.MeetingId.ShouldBeNull();
    }

    [Fact]
    public async Task Create_RefusesAnAssigneeFromAnotherWorkspace()
    {
        // Assigning is a write into somebody's task list. Without the membership check it is a way
        // to put a row carrying a stranger's user id into their "assigned to me" view.
        var (client, _) = await SignInAsync();
        var (_, outsider) = await SignInAsync();

        var response = await client.PostJsonAsync(
            Url("/api/v1/action-items"),
            NewTask("Ship it", assigneeId: outsider.User.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_DropsACitationThatBelongsToAnotherMeeting()
    {
        // Same rule the summary applies to a highlight: a citation that does not resolve is worse
        // than none, because it looks checkable and is not.
        var (client, _) = await SignInAsync();
        var meeting = await CreateMeetingAsync(client, "Migration planning");

        var response = await client.PostJsonAsync(
            Url("/api/v1/action-items"),
            NewTask(
                "Draft the runbook",
                meetingId: meeting.Meeting.Id,
                sourceSegmentId: Guid.CreateVersion7()));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var task = (await response.Content.ReadJsonAsync<ActionItemDto>())!;
        task.MeetingId.ShouldBe(meeting.Meeting.Id);
        task.SourceSegmentId.ShouldBeNull();
    }

    [Fact]
    public async Task Create_RejectsAnEmptyTitle()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PostJsonAsync(
            Url("/api/v1/action-items"),
            NewTask("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* ---------------------------------------------------------------------- */
    /* Patching — an absent field is not a null one                           */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Patch_LeavesFieldsTheRequestDidNotMention()
    {
        // The client's task drawer patches one field at a time. If an absent field read as null,
        // renaming a task would silently unassign it and drop its due date.
        var (client, session) = await SignInAsync();

        var task = await CreateTaskAsync(
            client,
            "Write the rollback plan",
            assigneeId: session.User.Id,
            dueDate: DateTimeOffset.UtcNow.AddDays(3),
            priority: ActionItemPriority.High);

        var patched = await PatchAsync(client, task.Id, """{"title": "Write the rollback plan (v2)"}""");

        patched.Title.ShouldBe("Write the rollback plan (v2)");
        patched.AssigneeId.ShouldBe(session.User.Id);
        patched.DueDate.ShouldNotBeNull();
        patched.Priority.ShouldBe(ActionItemPriority.High);
    }

    [Fact]
    public async Task Patch_WithAnExplicitNull_ClearsTheField()
    {
        // The other half of the pair above, and the reason Patch<T> exists at all: these two
        // requests differ only in whether the property is present, and they must not do the same
        // thing.
        var (client, session) = await SignInAsync();

        var task = await CreateTaskAsync(
            client,
            "Write the rollback plan",
            assigneeId: session.User.Id,
            dueDate: DateTimeOffset.UtcNow.AddDays(3));

        var patched = await PatchAsync(client, task.Id, """{"assigneeId": null, "dueDate": null}""");

        patched.AssigneeId.ShouldBeNull();
        patched.DueDate.ShouldBeNull();
        // Untouched, so the clear was scoped to the two fields that were sent.
        patched.Title.ShouldBe("Write the rollback plan");
    }

    [Fact]
    public async Task Patch_ToDone_StampsCompletedAt_AndReopeningClearsIt()
    {
        // Derived by the aggregate rather than sent by the client, so a task cannot be recorded as
        // finished at a time it was not — and cannot stay stamped after being reopened.
        var (client, _) = await SignInAsync();
        var task = await CreateTaskAsync(client, "Rotate the credentials");

        var done = await PatchAsync(client, task.Id, """{"status": "done"}""");

        done.Status.ShouldBe(ActionItemStatus.Done);
        done.CompletedAt.ShouldNotBeNull();

        var reopened = await PatchAsync(client, task.Id, """{"status": "in_progress"}""");

        reopened.Status.ShouldBe(ActionItemStatus.InProgress);
        reopened.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Patch_RejectsAnEmptyTitle_ButOnlyWhenTheTitleWasSent()
    {
        var (client, _) = await SignInAsync();
        var task = await CreateTaskAsync(client, "Rotate the credentials");

        var refused = await SendPatchAsync(client, task.Id, """{"title": ""}""");
        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // A patch that never mentions the title must not be rejected for it.
        var accepted = await SendPatchAsync(client, task.Id, """{"status": "blocked"}""");
        accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /* ---------------------------------------------------------------------- */
    /* Listing                                                                */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task List_SortsPriorityBySeverityRatherThanAlphabetically()
    {
        // The trap this exists for: priority is stored as text, so ORDER BY priority yields
        // high, low, medium, urgent — an ordering that looks plausible and is meaningless.
        var (client, _) = await SignInAsync();

        await CreateTaskAsync(client, "Low one", priority: ActionItemPriority.Low);
        await CreateTaskAsync(client, "Urgent one", priority: ActionItemPriority.Urgent);
        await CreateTaskAsync(client, "Medium one", priority: ActionItemPriority.Medium);
        await CreateTaskAsync(client, "High one", priority: ActionItemPriority.High);

        var page = await ListAsync(client, "?sortBy=priority&sortDir=desc");

        page.Items.Select(item => item.Priority).ShouldBe(
        [
            ActionItemPriority.Urgent,
            ActionItemPriority.High,
            ActionItemPriority.Medium,
            ActionItemPriority.Low,
        ]);
    }

    [Fact]
    public async Task List_PutsUndatedTasksLast_InBothDirections()
    {
        // Postgres sorts nulls first on a descending sort, which would open a list ordered by
        // urgency with every task that has no deadline at all.
        var (client, _) = await SignInAsync();

        await CreateTaskAsync(client, "Undated");
        await CreateTaskAsync(client, "Soon", dueDate: DateTimeOffset.UtcNow.AddDays(1));
        await CreateTaskAsync(client, "Later", dueDate: DateTimeOffset.UtcNow.AddDays(10));

        (await ListAsync(client, "?sortBy=dueDate&sortDir=asc")).Items.Last()
            .Title.ShouldBe("Undated");

        (await ListAsync(client, "?sortBy=dueDate&sortDir=desc")).Items.Last()
            .Title.ShouldBe("Undated");
    }

    [Fact]
    public async Task List_FiltersToOverdueAndUnassignedWork()
    {
        var (client, session) = await SignInAsync();

        await CreateTaskAsync(
            client,
            "Overdue and unassigned",
            dueDate: DateTimeOffset.UtcNow.AddDays(-2));

        await CreateTaskAsync(
            client,
            "Overdue but mine",
            assigneeId: session.User.Id,
            dueDate: DateTimeOffset.UtcNow.AddDays(-1));

        var stale = await CreateTaskAsync(client, "Done, so not overdue",
            dueDate: DateTimeOffset.UtcNow.AddDays(-5));
        await PatchAsync(client, stale.Id, """{"status": "done"}""");

        await CreateTaskAsync(client, "Undated");

        var overdue = await ListAsync(client, "?overdueOnly=true");
        // A finished task is not overdue however long ago it was due, and an undated one cannot be.
        overdue.Items.Select(item => item.Title).ShouldBe(
            ["Overdue but mine", "Overdue and unassigned"],
            ignoreOrder: true);

        var unassigned = await ListAsync(client, "?unassignedOnly=true&overdueOnly=true");
        unassigned.Items.ShouldHaveSingleItem().Title.ShouldBe("Overdue and unassigned");
    }

    [Fact]
    public async Task List_RejectsAnUnknownStatusRatherThanIgnoringIt()
    {
        // Dropping the filter and answering 200 would return a page of unfiltered results that reads
        // as a correct answer to a question nobody asked.
        var (client, _) = await SignInAsync();

        var response = await client.GetAsync(Url("/api/v1/action-items?status=finished"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task List_AcceptsTheApisOwnSpellingOfAStatus()
    {
        // Query-string binding never consults the JSON converter, so `in_progress` has to be
        // handled at the endpoint or the API rejects the spelling it publishes everywhere else.
        var (client, _) = await SignInAsync();
        var task = await CreateTaskAsync(client, "Halfway there");
        await PatchAsync(client, task.Id, """{"status": "in_progress"}""");

        var page = await ListAsync(client, "?status=in_progress");

        page.Items.ShouldHaveSingleItem().Title.ShouldBe("Halfway there");
    }

    /* ---------------------------------------------------------------------- */
    /* Counts                                                                 */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Counts_ReportOpenWorkForTheCallerAndTotalsForEveryone()
    {
        var (client, session) = await SignInAsync();

        await CreateTaskAsync(client, "Mine, open", assigneeId: session.User.Id);
        await CreateTaskAsync(client, "Mine, overdue",
            assigneeId: session.User.Id,
            dueDate: DateTimeOffset.UtcNow.AddDays(-1));

        var finished = await CreateTaskAsync(client, "Mine, finished", assigneeId: session.User.Id);
        await PatchAsync(client, finished.Id, """{"status": "done"}""");

        var counts = await client.GetJsonAsync<ActionItemCountsDto>(
            Url("/api/v1/action-items/counts"));

        counts.ShouldNotBeNull();
        counts.All.ShouldBe(3);
        // "Assigned to me" means work still to do — a tab counting finished tasks never reaches zero.
        counts.Assigned.ShouldBe(2);
        counts.Created.ShouldBe(3);
        counts.Completed.ShouldBe(1);
        counts.Overdue.ShouldBe(1);

        // Every status present even at zero: the board renders a column per status and indexes
        // straight into this map, where a missing key reads as undefined rather than as none.
        counts.ByStatus.Count.ShouldBe(Enum.GetValues<ActionItemStatus>().Length);
        counts.ByStatus[ActionItemStatus.Todo].ShouldBe(2);
        counts.ByStatus[ActionItemStatus.Done].ShouldBe(1);
        counts.ByStatus[ActionItemStatus.Blocked].ShouldBe(0);
    }

    [Fact]
    public async Task Counts_KeyTheStatusMapBySnakeCaseStrings()
    {
        // Asserted on the raw JSON, not on the deserialised object. A dictionary keyed by a .NET
        // enum round-trips perfectly between two .NET processes whatever it writes on the wire —
        // and the client indexes this map with "in_progress".
        var (client, _) = await SignInAsync();
        var task = await CreateTaskAsync(client, "Halfway there");
        await PatchAsync(client, task.Id, """{"status": "in_progress"}""");

        var json = await client.GetStringAsync(Url("/api/v1/action-items/counts"));

        using var document = JsonDocument.Parse(json);
        var byStatus = document.RootElement.GetProperty("byStatus");

        byStatus.GetProperty("in_progress").GetInt32().ShouldBe(1);
        byStatus.TryGetProperty("todo", out _).ShouldBeTrue();
    }

    /* ---------------------------------------------------------------------- */
    /* Bulk operations                                                        */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task BulkStatus_CountsRowsChangedRatherThanIdsMatched()
    {
        var (client, _) = await SignInAsync();

        var first = await CreateTaskAsync(client, "One");
        var second = await CreateTaskAsync(client, "Two");
        await PatchAsync(client, second.Id, """{"status": "done"}""");

        var response = await client.PostJsonAsync(
            Url("/api/v1/action-items/bulk-status"),
            new BulkStatusRequest([first.Id, second.Id], ActionItemStatus.Done));

        var result = (await response.Content.ReadJsonAsync<BulkResultDto>())!;

        // One, not two: the second was already done. A count of "ids matched" is a number the UI
        // cannot explain to the person who triggered it.
        result.Affected.ShouldBe(1);
    }

    [Fact]
    public async Task BulkAssign_WithANullAssignee_Unassigns()
    {
        var (client, session) = await SignInAsync();

        var first = await CreateTaskAsync(client, "One", assigneeId: session.User.Id);
        var second = await CreateTaskAsync(client, "Two", assigneeId: session.User.Id);

        var response = await client.PostJsonAsync(
            Url("/api/v1/action-items/bulk-assign"),
            new BulkAssignRequest([first.Id, second.Id], null));

        (await response.Content.ReadJsonAsync<BulkResultDto>())!.Affected.ShouldBe(2);

        var page = await ListAsync(client);
        page.Items.ShouldAllBe(item => item.AssigneeId == null);
    }

    [Fact]
    public async Task BulkOperations_IgnoreIdsFromAnotherWorkspace()
    {
        // The tenant filter is what makes it safe to take a list of ids from a client: an id from
        // another workspace simply does not come back, and the count reflects that rather than
        // reporting which ids were rejected.
        var (client, _) = await SignInAsync();
        var (outsiderClient, _) = await SignInAsync();

        var mine = await CreateTaskAsync(client, "Mine");
        var theirs = await CreateTaskAsync(outsiderClient, "Theirs");

        var response = await client.PostJsonAsync(
            Url("/api/v1/action-items/bulk-delete"),
            new BulkIdsRequest([mine.Id, theirs.Id]));

        (await response.Content.ReadJsonAsync<BulkResultDto>())!.Affected.ShouldBe(1);

        // Still there, and still theirs.
        var survivor = await outsiderClient.GetAsync(Url($"/api/v1/action-items/{theirs.Id}"));
        survivor.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /* ---------------------------------------------------------------------- */
    /* Meetings                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task DeletingAMeeting_LeavesTheWorkItProduced()
    {
        // §3.8, and the reason meeting → action_item is SET NULL rather than CASCADE: a commitment
        // outlives the meeting that produced it. Deleting a meeting must never silently delete
        // somebody's assigned work.
        var (client, session) = await SignInAsync();
        var meeting = await CreateMeetingAsync(client, "Migration planning");

        var task = await CreateTaskAsync(
            client,
            "Draft the runbook",
            assigneeId: session.User.Id,
            meetingId: meeting.Meeting.Id);

        (await client.DeleteAsync(Url($"/api/v1/meetings/{meeting.Meeting.Id}")))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var survivor = await client.GetJsonAsync<ActionItemDto>(
            Url($"/api/v1/action-items/{task.Id}"));

        survivor.ShouldNotBeNull();
        survivor.AssigneeId.ShouldBe(session.User.Id);
        // It loses its back-reference, not its existence.
        survivor.MeetingId.ShouldBeNull();
    }

    [Fact]
    public async Task AMeetingsPanel_ListsOnlyThatMeetingsTasks()
    {
        var (client, _) = await SignInAsync();
        var first = await CreateMeetingAsync(client, "Migration planning");
        var second = await CreateMeetingAsync(client, "Design review");

        await CreateTaskAsync(client, "Draft the runbook", meetingId: first.Meeting.Id);
        await CreateTaskAsync(client, "Update the mocks", meetingId: second.Meeting.Id);
        await CreateTaskAsync(client, "Unrelated chore");

        var tasks = await client.GetJsonAsync<IReadOnlyList<ActionItemDto>>(
            Url($"/api/v1/meetings/{first.Meeting.Id}/action-items"));

        tasks.ShouldNotBeNull();
        tasks.ShouldHaveSingleItem().Title.ShouldBe("Draft the runbook");
    }

    [Fact]
    public async Task ATask_IsInvisibleToAnotherWorkspace()
    {
        var (client, _) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var task = await CreateTaskAsync(client, "Confidential");

        (await outsider.GetAsync(Url($"/api/v1/action-items/{task.Id}")))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await outsider.GetJsonAsync<PagedResult<ActionItemDto>>(Url("/api/v1/action-items")))!
            .Total.ShouldBe(0);
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    private static CreateActionItemRequest NewTask(
        string title,
        Guid? assigneeId = null,
        DateTimeOffset? dueDate = null,
        ActionItemPriority? priority = null,
        Guid? meetingId = null,
        Guid? sourceSegmentId = null,
        IReadOnlyList<string>? tags = null) =>
        new(title, null, assigneeId, dueDate, priority, meetingId, sourceSegmentId, tags);

    private static async Task<ActionItemDto> CreateTaskAsync(
        HttpClient client,
        string title,
        Guid? assigneeId = null,
        DateTimeOffset? dueDate = null,
        ActionItemPriority? priority = null,
        Guid? meetingId = null,
        IReadOnlyList<string>? tags = null)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/action-items"),
            NewTask(title, assigneeId, dueDate, priority, meetingId, tags: tags));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadJsonAsync<ActionItemDto>())!;
    }

    /// <summary>
    /// Sends a patch as <b>raw JSON</b>, which is the only way to test these semantics.
    /// </summary>
    /// <remarks>
    /// Serialising an <c>UpdateActionItemRequest</c> would write every unset field as null and the
    /// request would then mean "clear everything" — the exact confusion the type exists to prevent.
    /// A test that built its body from the DTO could never tell the two cases apart.
    /// </remarks>
    private static Task<HttpResponseMessage> SendPatchAsync(
        HttpClient client,
        Guid taskId,
        string json) =>
        client.PatchAsync(
            Url($"/api/v1/action-items/{taskId}"),
            new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<ActionItemDto> PatchAsync(HttpClient client, Guid taskId, string json)
    {
        var response = await SendPatchAsync(client, taskId, json);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadJsonAsync<ActionItemDto>())!;
    }

    private static async Task<PagedResult<ActionItemDto>> ListAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Url($"/api/v1/action-items{queryString}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadJsonAsync<PagedResult<ActionItemDto>>())!;
    }

    private static async Task<MeetingDetailDto> CreateMeetingAsync(HttpClient client, string title)
    {
        var start = DateTimeOffset.UtcNow.AddDays(1);

        var response = await client.PostJsonAsync(
            Url("/api/v1/meetings"),
            new CreateMeetingRequest(title, null, start, start.AddHours(1), MeetingPlatform.Zoom, [], null));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadJsonAsync<MeetingDetailDto>())!;
    }

    private async Task<(HttpClient Client, AuthResponse Session)> SignInAsync(string? email = null)
    {
        var address = email ?? $"user-{Guid.CreateVersion7():n}@northwind.io";
        var idToken = $"token-{Guid.CreateVersion7():n}";
        _fixture.Google.Stage(idToken, address, subject: $"google-sub-{address}");

        var client = _fixture.CreateClient(new() { HandleCookies = false });

        var response = await client.PostJsonAsync(
            Url("/api/v1/auth/google"),
            new GoogleSignInRequest(idToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var session = (await response.Content.ReadJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return (client, session);
    }

    private static Uri Url(string relative) => new(relative, UriKind.Relative);
}
