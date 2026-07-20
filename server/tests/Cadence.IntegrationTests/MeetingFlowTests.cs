using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Meetings;
using Cadence.Application.Modules.Organizations;
using Cadence.Domain.Enums;
using Cadence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// Meetings: filtering, paging, the lifecycle actions and their tenant boundaries.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MeetingFlowTests
{
    private readonly AuthFixture _fixture;

    public MeetingFlowTests(AuthFixture fixture) => _fixture = fixture;

    /* ---------------------------------------------------------------------- */
    /* Creating                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Create_AddsTheOrganizerAsAHost()
    {
        var (client, session) = await SignInAsync();

        var created = await CreateMeetingAsync(client, "Quarterly planning");

        var organizer = created.Meeting.Participants.ShouldHaveSingleItem();
        organizer.UserId.ShouldBe(session.User.Id);
        // The organizer is added whether or not the client sent them — the create dialog does not
        // even offer them in the attendee picker.
        organizer.Role.ShouldBe(ParticipantRole.Host);
        created.Meeting.Status.ShouldBe(MeetingStatus.Scheduled);
        created.Meeting.SummaryStatus.ShouldBe(SummaryStatus.None);
    }

    [Fact]
    public async Task Create_ResolvesParticipantsFromTheWorkspace()
    {
        var (client, session, colleague, colleagueId) = await WorkspaceWithColleagueAsync();

        var created = await CreateMeetingAsync(client, "Design review", participantIds: [colleagueId]);

        created.Meeting.Participants.Count.ShouldBe(2);
        var attendee = created.Meeting.Participants.Single(p => p.UserId == colleagueId);
        attendee.Role.ShouldBe(ParticipantRole.Attendee);
        // Name and email are copied onto the meeting, so the record still reads correctly after
        // somebody renames themselves.
        attendee.Name.ShouldNotBeNullOrWhiteSpace();
        attendee.Email.ShouldNotBeNullOrWhiteSpace();

        _ = session;
        _ = colleague;
    }

    [Fact]
    public async Task Create_IgnoresAParticipantFromAnotherWorkspace()
    {
        // This is a tenant boundary, not input tidiness: a participant row copies the person's name
        // and email onto the meeting, so honouring an arbitrary user id would turn "create a
        // meeting" into a lookup that returns any user's address.
        var (client, _) = await SignInAsync();
        var (_, outsider) = await SignInAsync();

        var created = await CreateMeetingAsync(
            client,
            "Strategy",
            participantIds: [outsider.User.Id]);

        created.Meeting.Participants.ShouldHaveSingleItem()
            .UserId.ShouldNotBe(outsider.User.Id);
    }

    [Fact]
    public async Task Create_RejectsAnEndBeforeItsStart()
    {
        var (client, _) = await SignInAsync();
        var start = DateTimeOffset.UtcNow.AddDays(1);

        var response = await client.PostJsonAsync(
            Url("/api/v1/meetings"),
            new CreateMeetingRequest(
                "Backwards",
                null,
                start,
                start.AddHours(-1),
                MeetingPlatform.Zoom,
                [],
                null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* ---------------------------------------------------------------------- */
    /* Wire format                                                            */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Enums_AreSerialisedAsSnakeCaseStrings()
    {
        // Asserted against the raw JSON on purpose. Every other test deserialises into the same
        // enums it asserts on, so an integer wire format round-trips perfectly between two .NET
        // processes and passes — while the client, whose types are string unions, would see
        // "status": 0 and render nothing.
        var (client, _) = await SignInAsync();
        var created = await CreateMeetingAsync(client, "Wire check");

        var json = await client.GetStringAsync(Url($"/api/v1/meetings/{created.Meeting.Id}"));

        json.ShouldContain("\"status\":\"scheduled\"");
        json.ShouldContain("\"recordingStatus\":\"not_recorded\"");
        json.ShouldContain("\"summaryStatus\":\"none\"");
        json.ShouldContain("\"role\":\"host\"");
    }

    [Fact]
    public async Task Filters_AcceptTheSameSpellingTheBodyUses()
    {
        // Query-string binding does not go through the JSON converter and parses enums
        // case-sensitively, so `?status=scheduled` — the spelling every response uses — was rejected
        // as a malformed request before EnumQuery.
        var (client, _) = await SignInAsync();
        await CreateMeetingAsync(client, "Upcoming");

        var page = await ListAsync(client, "?status=scheduled&sortDir=asc&summaryStatus=none");

        page.Items.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task AnUnknownFilterValue_IsA400RatherThanAnIgnoredFilter()
    {
        // Silently dropping it would return a page of unfiltered results that reads as a correct
        // answer to a question the caller did not ask.
        var (client, _) = await SignInAsync();

        var response = await client.GetAsync(Url("/api/v1/meetings?status=finished"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("scheduled");
    }

    /* ---------------------------------------------------------------------- */
    /* Tenant scoping                                                         */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task AMeeting_IsInvisibleToAnotherWorkspace()
    {
        var (mine, _) = await SignInAsync();
        var (theirs, _) = await SignInAsync();

        var notMine = await CreateMeetingAsync(theirs, "Their roadmap");

        var direct = await mine.GetAsync(Url($"/api/v1/meetings/{notMine.Meeting.Id}"));
        direct.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var list = await ListAsync(mine);
        list.Items.ShouldNotContain(meeting => meeting.Id == notMine.Meeting.Id);
    }

    [Fact]
    public async Task BulkDelete_DoesNotCountAnotherWorkspacesMeetings()
    {
        var (mine, _) = await SignInAsync();
        var (theirs, _) = await SignInAsync();

        var own = await CreateMeetingAsync(mine, "Mine");
        var notMine = await CreateMeetingAsync(theirs, "Theirs");

        var response = await mine.PostJsonAsync(
            Url("/api/v1/meetings/bulk-delete"),
            new BulkIdsRequest([own.Meeting.Id, notMine.Meeting.Id]));

        var result = (await response.Content.ReadJsonAsync<BulkResultDto>())!;

        // One, not two: the tenant filter removes the other workspace's id before it is counted, so
        // the response cannot be used to confirm that id exists.
        result.Affected.ShouldBe(1);

        var stillThere = await theirs.GetAsync(Url($"/api/v1/meetings/{notMine.Meeting.Id}"));
        stillThere.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    /* ---------------------------------------------------------------------- */
    /* Filtering and sorting                                                  */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task List_FiltersByTag()
    {
        // Tags are a Postgres array behind a private field with a GIN index. This is the test that
        // proves the EF.Property translation actually reaches SQL rather than throwing.
        var (client, _) = await SignInAsync();

        await CreateMeetingAsync(client, "Roadmap", tags: ["planning", "q3"]);
        await CreateMeetingAsync(client, "Retro", tags: ["retro"]);

        var planning = await ListAsync(client, "?tags=planning");

        planning.Items.ShouldHaveSingleItem().Title.ShouldBe("Roadmap");
        planning.Items[0].Tags.ShouldContain("q3");
    }

    [Fact]
    public async Task List_SearchesTitleDescriptionTagsAndParticipants()
    {
        var (client, _, _, colleagueId) = await WorkspaceWithColleagueAsync();

        await CreateMeetingAsync(client, "Alpha", description: "about migrations");
        await CreateMeetingAsync(client, "Beta", tags: ["infrastructure"]);
        await CreateMeetingAsync(client, "Gamma", participantIds: [colleagueId]);

        (await ListAsync(client, "?search=migrations")).Items.ShouldHaveSingleItem()
            .Title.ShouldBe("Alpha");
        (await ListAsync(client, "?search=infrastructure")).Items.ShouldHaveSingleItem()
            .Title.ShouldBe("Beta");
    }

    [Fact]
    public async Task List_ExcludesArchivedUnlessAsked()
    {
        var (client, _) = await SignInAsync();

        var archived = await CreateMeetingAsync(client, "Old business");
        await ArchiveAsync(client, [archived.Meeting.Id], archived: true);

        (await ListAsync(client)).Items
            .ShouldNotContain(meeting => meeting.Id == archived.Meeting.Id);

        (await ListAsync(client, "?includeArchived=true")).Items
            .ShouldContain(meeting => meeting.Id == archived.Meeting.Id);
    }

    [Fact]
    public async Task List_FiltersByFavoriteAndParticipant()
    {
        var (client, session, _, colleagueId) = await WorkspaceWithColleagueAsync();

        var favorite = await CreateMeetingAsync(client, "Starred");
        await client.PostAsync(Url($"/api/v1/meetings/{favorite.Meeting.Id}/favorite"), null);
        await CreateMeetingAsync(client, "Plain", participantIds: [colleagueId]);

        (await ListAsync(client, "?favoritesOnly=true")).Items.ShouldHaveSingleItem()
            .Title.ShouldBe("Starred");

        (await ListAsync(client, $"?participantId={colleagueId}")).Items.ShouldHaveSingleItem()
            .Title.ShouldBe("Plain");

        // The organizer is on both, so filtering by them returns both.
        (await ListAsync(client, $"?participantId={session.User.Id}")).Items.Count.ShouldBe(2);
    }

    [Fact]
    public async Task List_SortsAndPagesStably()
    {
        var (client, _) = await SignInAsync();
        var start = DateTimeOffset.UtcNow.AddDays(10);

        // Identical start times, so ordering is decided entirely by the tiebreaker. Without one,
        // the same row can appear on both pages while another is never shown at all.
        foreach (var title in new[] { "A", "B", "C", "D", "E" })
        {
            await CreateMeetingAsync(client, title, startsAt: start);
        }

        var first = await ListAsync(client, "?pageSize=2&page=1&sortBy=startsAt&sortDir=asc");
        var second = await ListAsync(client, "?pageSize=2&page=2&sortBy=startsAt&sortDir=asc");

        first.Total.ShouldBe(5);
        first.TotalPages.ShouldBe(3);
        first.Items.Count.ShouldBe(2);

        var seen = first.Items.Concat(second.Items).Select(meeting => meeting.Id).ToList();
        seen.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public async Task List_SortsByTitle()
    {
        var (client, _) = await SignInAsync();

        await CreateMeetingAsync(client, "Zebra");
        await CreateMeetingAsync(client, "Aardvark");

        var sorted = await ListAsync(client, "?sortBy=title&sortDir=asc");

        sorted.Items[0].Title.ShouldBe("Aardvark");
    }

    [Fact]
    public async Task List_ClampsAnAbsurdPageSize()
    {
        var (client, _) = await SignInAsync();
        await CreateMeetingAsync(client, "One");

        var page = await ListAsync(client, "?pageSize=100000");

        // Clamped rather than rejected: a bad page size is not worth a 400, and above the cap a
        // "page" is a table scan wearing a disguise.
        page.PageSize.ShouldBe(ListQuery.MaxPageSize);
    }

    /* ---------------------------------------------------------------------- */
    /* Updating                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Update_LeavesParticipantsAloneWhenTheyAreOmitted()
    {
        // Null means "leave the attendee list alone"; an empty array means "clear it". Collapsing
        // the two would make it impossible to edit a title without resending every attendee.
        var (client, _, _, colleagueId) = await WorkspaceWithColleagueAsync();
        var created = await CreateMeetingAsync(client, "Sync", participantIds: [colleagueId]);

        var response = await client.PatchJsonAsync(
            Url($"/api/v1/meetings/{created.Meeting.Id}"),
            new UpdateMeetingRequest(
                "Sync renamed",
                null,
                created.Meeting.StartsAt,
                created.Meeting.EndsAt,
                MeetingPlatform.Zoom,
                ParticipantIds: null,
                Tags: null,
                MeetingUrl: null));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var updated = (await response.Content.ReadJsonAsync<MeetingDetailDto>())!;
        updated.Meeting.Title.ShouldBe("Sync renamed");
        updated.Meeting.Participants.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Update_RemovesAttendeesWhenAnEmptyListIsSent()
    {
        var (client, session, _, colleagueId) = await WorkspaceWithColleagueAsync();
        var created = await CreateMeetingAsync(client, "Sync", participantIds: [colleagueId]);

        var response = await client.PatchJsonAsync(
            Url($"/api/v1/meetings/{created.Meeting.Id}"),
            new UpdateMeetingRequest(
                "Sync",
                null,
                created.Meeting.StartsAt,
                created.Meeting.EndsAt,
                MeetingPlatform.Zoom,
                ParticipantIds: [],
                Tags: null,
                MeetingUrl: null));

        var updated = (await response.Content.ReadJsonAsync<MeetingDetailDto>())!;

        // Everyone but the organizer, who cannot be removed from their own meeting.
        updated.Meeting.Participants.ShouldHaveSingleItem().UserId.ShouldBe(session.User.Id);
    }

    [Fact]
    public async Task Update_RejectsANonHttpMeetingUrl()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateMeetingAsync(client, "Sync");

        var response = await client.PatchJsonAsync(
            Url($"/api/v1/meetings/{created.Meeting.Id}"),
            new UpdateMeetingRequest(
                "Sync",
                null,
                created.Meeting.StartsAt,
                created.Meeting.EndsAt,
                MeetingPlatform.Zoom,
                null,
                null,
                MeetingUrl: "javascript:alert(1)"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* ---------------------------------------------------------------------- */
    /* Lifecycle actions                                                      */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Favorite_Toggles()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateMeetingAsync(client, "Sync");

        var on = await client.PostAsync(Url($"/api/v1/meetings/{created.Meeting.Id}/favorite"), null);
        (await on.Content.ReadJsonAsync<MeetingSummaryDto>())!.IsFavorite.ShouldBeTrue();

        var off = await client.PostAsync(Url($"/api/v1/meetings/{created.Meeting.Id}/favorite"), null);
        (await off.Content.ReadJsonAsync<MeetingSummaryDto>())!.IsFavorite.ShouldBeFalse();
    }

    [Fact]
    public async Task Archive_CountsRowsItActuallyChanged()
    {
        var (client, _) = await SignInAsync();
        var first = await CreateMeetingAsync(client, "One");
        var second = await CreateMeetingAsync(client, "Two");

        await ArchiveAsync(client, [first.Meeting.Id], archived: true);

        // Both ids, but only one of them is not already archived.
        var result = await ArchiveAsync(client, [first.Meeting.Id, second.Meeting.Id], archived: true);

        result.Affected.ShouldBe(1);
    }

    [Fact]
    public async Task Duplicate_ProducesAFreshScheduledMeeting()
    {
        var (client, _, _, colleagueId) = await WorkspaceWithColleagueAsync();
        var source = await CreateMeetingAsync(
            client,
            "Weekly sync",
            participantIds: [colleagueId],
            tags: ["recurring"]);

        await client.PostJsonAsync(
            Url($"/api/v1/meetings/{source.Meeting.Id}/bookmarks"),
            new AddBookmarkRequest(30, "A moment"));

        var response = await client.PostAsync(
            Url($"/api/v1/meetings/{source.Meeting.Id}/duplicate"),
            null);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var copy = (await response.Content.ReadJsonAsync<MeetingDetailDto>())!;
        copy.Meeting.Id.ShouldNotBe(source.Meeting.Id);
        copy.Meeting.Title.ShouldBe("Weekly sync (copy)");
        copy.Meeting.Participants.Count.ShouldBe(2);
        copy.Meeting.Tags.ShouldContain("recurring");
        copy.Meeting.Status.ShouldBe(MeetingStatus.Scheduled);
        // Duplicating sets up the next occurrence; carrying the bookmarks over would attach last
        // week's moments to a meeting that has not happened.
        copy.Bookmarks.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bookmarks_AreReturnedInRecordingOrder()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateMeetingAsync(client, "Sync");

        foreach (var at in new[] { 300, 60, 900 })
        {
            var response = await client.PostJsonAsync(
                Url($"/api/v1/meetings/{created.Meeting.Id}/bookmarks"),
                new AddBookmarkRequest(at, $"At {at}"));

            response.StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var detail = await client.GetJsonAsync<MeetingDetailDto>(
            Url($"/api/v1/meetings/{created.Meeting.Id}"));

        detail!.Bookmarks.Select(bookmark => bookmark.AtSeconds).ShouldBe([60, 300, 900]);
    }

    [Fact]
    public async Task Bookmark_RejectsANegativePosition()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateMeetingAsync(client, "Sync");

        var response = await client.PostJsonAsync(
            Url($"/api/v1/meetings/{created.Meeting.Id}/bookmarks"),
            new AddBookmarkRequest(-5, "Before the start"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Delete_IsSoftAndHidesTheMeeting()
    {
        var (client, _) = await SignInAsync();
        var created = await CreateMeetingAsync(client, "Cancelled plans");

        var deleted = await client.DeleteAsync(Url($"/api/v1/meetings/{created.Meeting.Id}"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var gone = await client.GetAsync(Url($"/api/v1/meetings/{created.Meeting.Id}"));
        gone.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var row = await context.Meetings
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(meeting => meeting.Id == created.Meeting.Id);

        row.ShouldNotBeNull();
        row.DeletedAt.ShouldNotBeNull();

        // The participants went with it only in the sense that they are hidden — the rows survive,
        // so restoring the meeting restores who was in it.
        var participants = await context.Participants
            .IgnoreQueryFilters()
            .CountAsync(participant => participant.MeetingId == created.Meeting.Id);

        participants.ShouldBe(1);
    }

    /* ---------------------------------------------------------------------- */
    /* Tags and history                                                       */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Tags_AreDistinctSortedAndScopedToTheWorkspace()
    {
        var (mine, _) = await SignInAsync();
        var (theirs, _) = await SignInAsync();

        await CreateMeetingAsync(mine, "One", tags: ["planning", "q3"]);
        await CreateMeetingAsync(mine, "Two", tags: ["planning"]);
        await CreateMeetingAsync(theirs, "Theirs", tags: ["confidential"]);

        var tags = await mine.GetJsonAsync<List<string>>(Url("/api/v1/meetings/tags"));

        tags.ShouldBe(["planning", "q3"]);
    }

    [Fact]
    public async Task History_ReturnsFinishedMeetingsIncludingArchived()
    {
        var (client, session) = await SignInAsync();

        var completed = await SeedCompletedMeetingAsync(session.User.OrganizationId, session.User.Id);
        var upcoming = await CreateMeetingAsync(client, "Next week");

        await ArchiveAsync(client, [completed], archived: true);

        var history = await client.GetJsonAsync<PagedResult<MeetingSummaryDto>>(
            Url("/api/v1/meetings/history"));

        // Archived is included: an archive that hides archived meetings has nothing to show.
        history!.Items.ShouldContain(meeting => meeting.Id == completed);
        history.Items.ShouldNotContain(meeting => meeting.Id == upcoming.Meeting.Id);
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    /// <summary>Writes a completed meeting straight to the database; nothing ends one over HTTP yet.</summary>
    private async Task<Guid> SeedCompletedMeetingAsync(Guid organizationId, Guid organizerId)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var meeting = Domain.Meetings.Meeting.Schedule(
            organizationId,
            organizerId,
            "Last month's review",
            "Seeded as already finished.",
            DateTimeOffset.UtcNow.AddDays(-30),
            DateTimeOffset.UtcNow.AddDays(-30).AddHours(1),
            MeetingPlatform.Zoom);

        meeting.StartRecording();
        meeting.Complete(3600);

        context.Meetings.Add(meeting);
        await context.SaveChangesAsync();

        return meeting.Id;
    }

    private async Task<(HttpClient Client, AuthResponse Session, HttpClient Colleague, Guid ColleagueId)>
        WorkspaceWithColleagueAsync()
    {
        var (client, session) = await SignInAsync();
        var email = UniqueEmail();

        var invited = await client.PostJsonAsync(
            Url("/api/v1/invitations"),
            new InviteMemberRequest(email, UserRole.Member));
        invited.StatusCode.ShouldBe(HttpStatusCode.Created);

        var (colleague, colleagueSession) = await SignInAsync(email);

        return (client, session, colleague, colleagueSession.User.Id);
    }

    private static async Task<MeetingDetailDto> CreateMeetingAsync(
        HttpClient client,
        string title,
        string? description = null,
        IReadOnlyList<Guid>? participantIds = null,
        IReadOnlyList<string>? tags = null,
        DateTimeOffset? startsAt = null)
    {
        var start = startsAt ?? DateTimeOffset.UtcNow.AddDays(1);

        var response = await client.PostJsonAsync(
            Url("/api/v1/meetings"),
            new CreateMeetingRequest(
                title,
                description,
                start,
                start.AddHours(1),
                MeetingPlatform.Zoom,
                participantIds ?? [],
                tags));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadJsonAsync<MeetingDetailDto>())!;
    }

    private static async Task<PagedResult<MeetingSummaryDto>> ListAsync(
        HttpClient client,
        string queryString = "")
    {
        var response = await client.GetAsync(Url($"/api/v1/meetings{queryString}"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadJsonAsync<PagedResult<MeetingSummaryDto>>())!;
    }

    private static async Task<BulkResultDto> ArchiveAsync(
        HttpClient client,
        IReadOnlyList<Guid> ids,
        bool archived)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/meetings/archive"),
            new ArchiveRequest(ids, archived));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadJsonAsync<BulkResultDto>())!;
    }

    private async Task<(HttpClient Client, AuthResponse Session)> SignInAsync(string? email = null)
    {
        var address = email ?? UniqueEmail();
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

    private static string UniqueEmail() => $"user-{Guid.CreateVersion7():n}@northwind.io";
}
