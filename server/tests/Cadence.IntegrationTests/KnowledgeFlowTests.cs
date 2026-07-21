using System.Net;
using System.Net.Http.Headers;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Knowledge;
using Cadence.Application.Modules.Meetings;
using Cadence.Domain.Enums;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// The knowledge base: entries, categories, tags, favourites and the recently-opened rail.
/// </summary>
/// <remarks>
/// The test worth the most here is <see cref="List_PutsNeverOpenedEntriesLast"/>. The client's rail
/// asks for the four most recently opened entries and then drops the ones that were never opened —
/// so if nulls sort first, the rail is empty forever while both the query and the client code look
/// correct in isolation.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class KnowledgeFlowTests
{
    private readonly AuthFixture _fixture;

    public KnowledgeFlowTests(AuthFixture fixture) => _fixture = fixture;

    /* ---------------------------------------------------------------------- */
    /* Creating                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Create_FilesTheEntryAgainstTheCaller()
    {
        var (client, session) = await SignInAsync();

        var item = await CreateAsync(client, "Onboarding checklist", category: "People");

        // The owner comes from the token. The client's mock takes one as input because it has no
        // session to read; accepting it here would let a member file under someone else's name.
        item.OwnerId.ShouldBe(session.User.Id);
        item.Kind.ShouldBe(KnowledgeItemKind.MeetingNote);
        item.Category.ShouldBe("People");
        item.IsFavorite.ShouldBeFalse();
        item.LastOpenedAt.ShouldBeNull();
        item.SourceId.ShouldBeNull();
    }

    [Fact]
    public async Task Create_FallsBackToUncategorised()
    {
        var (client, _) = await SignInAsync();

        var item = await CreateAsync(client, "Loose note", category: "   ");

        // The category drives a filter menu, and an entry with a blank one would be unreachable
        // through it.
        item.Category.ShouldBe("Uncategorised");
    }

    [Fact]
    public async Task Create_RejectsAnEmptyTitle()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge"),
            NewItem("   "));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_LinksTheEntryToAMeetingInThisWorkspace()
    {
        var (client, _) = await SignInAsync();
        var meeting = await CreateMeetingAsync(client, "Architecture review");

        var item = await CreateAsync(
            client,
            "What we decided about the queue",
            kind: KnowledgeItemKind.AiSummary,
            sourceId: meeting.Meeting.Id);

        item.SourceId.ShouldBe(meeting.Meeting.Id);
    }

    [Fact]
    public async Task Create_RefusesASourceFromAnotherWorkspace()
    {
        // The same rule the summary applies to a highlight: a citation that does not resolve is
        // worse than none, because it looks checkable and is not. Here it would also assert a
        // connection to a record the reader is not allowed to inspect.
        var (client, _) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var meeting = await CreateMeetingAsync(outsider, "Their board review");

        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge"),
            NewItem(
                "Notes from a meeting I cannot see",
                kind: KnowledgeItemKind.MeetingNote,
                sourceId: meeting.Meeting.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_RefusesASourceOfTheWrongKind()
    {
        // A document entry must cite a document. Checking only "does this id exist somewhere" would
        // let a meeting id land in a field the client renders as a document link.
        var (client, _) = await SignInAsync();
        var meeting = await CreateMeetingAsync(client, "Planning");

        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge"),
            NewItem(
                "The spec",
                kind: KnowledgeItemKind.Document,
                sourceId: meeting.Meeting.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_RequiresAUrlForALinkEntry()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge"),
            NewItem("A link to nowhere", kind: KnowledgeItemKind.Link));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_RefusesAUrlThatIsNotAWebAddress()
    {
        // The client opens this with window.open, so a javascript: scheme would run in the reader's
        // own session and a relative path would resolve against Cadence's origin.
        var (client, _) = await SignInAsync();

        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge"),
            NewItem(
                "Totally normal link",
                kind: KnowledgeItemKind.Link,
                sourceUrl: "javascript:alert(document.cookie)"));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_RefusesALinkThatAlsoCitesARecord()
    {
        var (client, _) = await SignInAsync();
        var meeting = await CreateMeetingAsync(client, "Planning");

        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge"),
            NewItem(
                "Both at once",
                kind: KnowledgeItemKind.Link,
                sourceId: meeting.Meeting.Id,
                sourceUrl: "https://example.com/notes"));

        // Two destinations, and nothing in the UI says which one a click follows.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* ---------------------------------------------------------------------- */
    /* The recently-opened rail                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Open_RecordsTheVisitWithoutABody()
    {
        var (client, _) = await SignInAsync();

        var item = await CreateAsync(client, "Runbook");

        var response = await client.PostAsync(
            Url($"/api/v1/knowledge/{item.Id}/open"),
            content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var opened = (await client.GetJsonAsync<KnowledgeItemDto>(
            Url($"/api/v1/knowledge/{item.Id}")))!;

        opened.LastOpenedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task List_PutsNeverOpenedEntriesLast()
    {
        // The rail's exact request: four newest-opened entries, then the client drops the nulls.
        // Postgres sorts nulls first on a descending sort, so without the explicit null ordering
        // every one of these four rows would be an entry nobody has ever opened and the rail would
        // render empty — permanently, and with no error anywhere.
        var (client, _) = await SignInAsync();

        var read = await CreateAsync(client, "Read me");

        for (var index = 0; index < 5; index++)
        {
            await CreateAsync(client, $"Never opened {index}");
        }

        var opened = await client.PostAsync(
            Url($"/api/v1/knowledge/{read.Id}/open"),
            content: null);

        opened.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var rail = await ListAsync(client, "?sortBy=lastOpenedAt&sortDir=desc&pageSize=4");

        rail.Items[0].Id.ShouldBe(read.Id);
        rail.Items.Count(item => item.LastOpenedAt is not null).ShouldBe(1);
    }

    [Fact]
    public async Task List_PutsNeverOpenedEntriesLastAscendingToo()
    {
        // Ascending is where "nulls last" is the surprising choice, and it is deliberate: an entry
        // with no timestamp has not been opened at all, which is not the same as having been opened
        // longest ago.
        var (client, _) = await SignInAsync();

        await CreateAsync(client, "Never opened");
        var read = await CreateAsync(client, "Read me");

        await client.PostAsync(Url($"/api/v1/knowledge/{read.Id}/open"), content: null);

        var ascending = await ListAsync(client, "?sortBy=lastOpenedAt&sortDir=asc");

        ascending.Items[0].Id.ShouldBe(read.Id);
        ascending.Items[^1].LastOpenedAt.ShouldBeNull();
    }

    /* ---------------------------------------------------------------------- */
    /* Listing, facets and favourites                                         */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task List_FiltersByKindCategoryAndFavourite()
    {
        var (client, _) = await SignInAsync();

        var note = await CreateAsync(client, "Retro notes", category: "Process");
        await CreateAsync(
            client,
            "Postgres docs",
            kind: KnowledgeItemKind.Link,
            category: "Engineering",
            sourceUrl: "https://www.postgresql.org/docs/");

        var favorited = await client.PostAsync(
            Url($"/api/v1/knowledge/{note.Id}/favorite"),
            content: null);

        favorited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await favorited.Content.ReadJsonAsync<KnowledgeItemDto>())!.IsFavorite.ShouldBeTrue();

        (await ListAsync(client, "?kind=link")).Items.Count.ShouldBe(1);
        (await ListAsync(client, "?category=Process")).Items.Count.ShouldBe(1);

        var favorites = await ListAsync(client, "?favoritesOnly=true");
        favorites.Items.Count.ShouldBe(1);
        favorites.Items[0].Id.ShouldBe(note.Id);
    }

    [Fact]
    public async Task List_SearchesTitlesExcerptsCategoriesAndTags()
    {
        var (client, _) = await SignInAsync();

        await CreateAsync(
            client,
            "Deployment runbook",
            category: "Engineering",
            excerpt: "How a release reaches production.",
            tags: ["release"]);

        await CreateAsync(client, "Expenses policy", category: "Finance", tags: ["policy"]);

        (await ListAsync(client, "?search=runbook")).Items.Count.ShouldBe(1);
        (await ListAsync(client, "?search=release")).Items.Count.ShouldBe(1);
        (await ListAsync(client, "?search=finance")).Items.Count.ShouldBe(1);
        (await ListAsync(client, "?search=production")).Items.Count.ShouldBe(1);
    }

    [Fact]
    public async Task List_SortsAndPagesStably()
    {
        // Identical titles on purpose. Without the id tiebreaker a row can appear on two pages while
        // another never appears at all — invisible in data where the keys happen to differ.
        var (client, _) = await SignInAsync();

        for (var index = 0; index < 6; index++)
        {
            await CreateAsync(client, "Identical");
        }

        var first = await ListAsync(client, "?sortBy=title&sortDir=asc&page=1&pageSize=3");
        var second = await ListAsync(client, "?sortBy=title&sortDir=asc&page=2&pageSize=3");

        first.Total.ShouldBe(6);
        first.Items.Select(item => item.Id)
            .Intersect(second.Items.Select(item => item.Id))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task List_RejectsAKindItDoesNotUnderstand()
    {
        var (client, _) = await SignInAsync();

        var response = await client.GetAsync(Url("/api/v1/knowledge?kind=recipe"));

        // Named rather than silently dropped: a filter the server ignores shows the caller a list
        // that does not match what they asked for.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Facets_ReportOnlyWhatIsFiled()
    {
        var (client, _) = await SignInAsync();

        await CreateAsync(client, "One", category: "Engineering", tags: ["Runbook", "oncall"]);
        await CreateAsync(client, "Two", category: "Finance", tags: ["runbook"]);

        var facets = (await client.GetJsonAsync<KnowledgeFacetsDto>(
            Url("/api/v1/knowledge/facets")))!;

        facets.Categories.ShouldBe(["Engineering", "Finance"]);

        // The aggregate lowercases tags on the way in, so the two spellings of "runbook" are one tag.
        facets.Tags.ShouldBe(["oncall", "runbook"]);
    }

    [Fact]
    public async Task Facets_AreScopedToTheWorkspace()
    {
        var (client, _) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        await CreateAsync(outsider, "Their entry", category: "Secrets", tags: ["confidential"]);
        await CreateAsync(client, "My entry", category: "Mine", tags: ["public"]);

        var facets = (await client.GetJsonAsync<KnowledgeFacetsDto>(
            Url("/api/v1/knowledge/facets")))!;

        // A filter menu is a listing too. Category names leak what another workspace works on even
        // when the entries themselves stay hidden.
        facets.Categories.ShouldBe(["Mine"]);
        facets.Tags.ShouldBe(["public"]);
    }

    /* ---------------------------------------------------------------------- */
    /* Deleting                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Delete_RemovesTheEntryAndLeavesItsSourceAlone()
    {
        var (client, _) = await SignInAsync();
        var meeting = await CreateMeetingAsync(client, "Architecture review");

        var item = await CreateAsync(
            client,
            "What we decided",
            kind: KnowledgeItemKind.AiSummary,
            sourceId: meeting.Meeting.Id);

        var response = await client.DeleteAsync(Url($"/api/v1/knowledge/{item.Id}"));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(client)).Items.ShouldBeEmpty();

        // The entry was a pointer. Removing it must not touch what it pointed at.
        var stillThere = await client.GetAsync(Url($"/api/v1/meetings/{meeting.Meeting.Id}"));
        stillThere.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Delete_IsRefusedForAnotherWorkspacesEntry()
    {
        var (owner, _) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var item = await CreateAsync(owner, "Private note");

        var response = await outsider.DeleteAsync(Url($"/api/v1/knowledge/{item.Id}"));

        // 404 rather than 403: "exists but not yours" is a membership oracle.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BulkDelete_ReportsWhatItRemoved()
    {
        var (client, _) = await SignInAsync();

        var first = await CreateAsync(client, "One");
        var second = await CreateAsync(client, "Two");

        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge/bulk-delete"),
            new BulkIdsRequest([first.Id, second.Id, Guid.CreateVersion7()]));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Two, not three: the unknown id matched nothing, and the count reports rows removed rather
        // than ids submitted.
        (await response.Content.ReadJsonAsync<BulkResultDto>())!.Affected.ShouldBe(2);
        (await ListAsync(client)).Items.ShouldBeEmpty();
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    private static async Task<KnowledgeItemDto> CreateAsync(
        HttpClient client,
        string title,
        KnowledgeItemKind kind = KnowledgeItemKind.MeetingNote,
        string? category = "General",
        string? excerpt = "A short preview.",
        Guid? sourceId = null,
        string? sourceUrl = null,
        IReadOnlyList<string>? tags = null)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/knowledge"),
            NewItem(title, kind, category, excerpt, sourceId, sourceUrl, tags));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadJsonAsync<KnowledgeItemDto>())!;
    }

    private static CreateKnowledgeItemRequest NewItem(
        string title,
        KnowledgeItemKind kind = KnowledgeItemKind.MeetingNote,
        string? category = "General",
        string? excerpt = "A short preview.",
        Guid? sourceId = null,
        string? sourceUrl = null,
        IReadOnlyList<string>? tags = null) =>
        new(title, kind, category, excerpt, sourceId, sourceUrl, tags);

    private static Task<PagedResult<KnowledgeItemDto>> ListAsync(HttpClient client, string query = "") =>
        client.GetJsonAsync<PagedResult<KnowledgeItemDto>>(Url($"/api/v1/knowledge{query}"))!;

    private async Task<MeetingDetailDto> CreateMeetingAsync(HttpClient client, string title)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/meetings"),
            new CreateMeetingRequest(
                title,
                Description: null,
                StartsAt: DateTimeOffset.UtcNow.AddHours(1),
                EndsAt: DateTimeOffset.UtcNow.AddHours(2),
                Platform: MeetingPlatform.GoogleMeet,
                ParticipantIds: [],
                Tags: null));

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
