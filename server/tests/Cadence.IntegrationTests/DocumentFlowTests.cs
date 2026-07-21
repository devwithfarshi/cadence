using System.Net;
using System.Net.Http.Headers;
using Cadence.Application.Common.Models;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Documents;
using Cadence.Application.Modules.Meetings;
using Cadence.Domain.Enums;
using Cadence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// Documents: the signed upload handshake, registration, the library list and deletion.
/// </summary>
/// <remarks>
/// The load-bearing tests here are the ones about <b>disagreement</b> — between what the client says
/// it uploaded and what the store actually holds, and between the workspace a key was signed for and
/// the workspace registering it. A suite that only exercised the happy path would pass just as
/// happily with every one of those checks deleted, because the happy path never disagrees with
/// itself.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentFlowTests
{
    private readonly AuthFixture _fixture;

    public DocumentFlowTests(AuthFixture fixture) => _fixture = fixture;

    /* ---------------------------------------------------------------------- */
    /* Signing                                                                */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Signature_NamesAKeyInsideTheCallersWorkspace()
    {
        var (client, session) = await SignInAsync();

        var signature = await SignAsync(client, "quarterly-review.pdf");

        // The key is chosen by the server and carries the tenant. That is what makes the prefix check
        // at registration meaningful rather than decorative.
        signature.StorageKey.ShouldContain($"/{session.User.OrganizationId}/documents/");
        signature.StorageKey.ShouldEndWith(".pdf");
        signature.UploadUrl.ShouldNotBeNullOrWhiteSpace();
        signature.Parameters.ShouldContainKey("public_id");
    }

    [Fact]
    public async Task Signature_RefusesAFormatTheLibraryDoesNotAccept()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PostJsonAsync(
            Url("/api/v1/documents/upload-signature"),
            new UploadSignatureRequest("payload.exe", "application/octet-stream", 2048));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Signature_RefusesAFileOverTheSizeLimit()
    {
        var (client, _) = await SignInAsync();

        var response = await client.PostJsonAsync(
            Url("/api/v1/documents/upload-signature"),
            new UploadSignatureRequest("huge.pdf", "application/pdf", 512L * 1024 * 1024));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    /* ---------------------------------------------------------------------- */
    /* Registering — nothing the client says is taken on trust                */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Register_RecordsTheSizeTheStoreReports()
    {
        var (client, session) = await SignInAsync();

        var signature = await SignAsync(client, "runbook.pdf");

        // What actually landed is a different size from anything the client mentioned.
        _fixture.Files.Land(signature.StorageKey, sizeBytes: 4_242);

        var document = await RegisterAsync(client, signature.StorageKey, "runbook.pdf");

        document.SizeBytes.ShouldBe(4_242);
        document.OwnerId.ShouldBe(session.User.Id);
        document.Type.ShouldBe(DocumentType.Pdf);
        document.ProcessingStatus.ShouldBe(ProcessingStatus.Processing);
        document.IsFavorite.ShouldBeFalse();
    }

    [Fact]
    public async Task Register_RefusesAKeySignedForAnotherWorkspace()
    {
        // The tenant boundary of this module. Without it, a member who learned another workspace's
        // storage key could register it as their own document and then read the file through the
        // download route.
        var (owner, _) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var signature = await SignAsync(owner, "confidential.pdf");
        _fixture.Files.Land(signature.StorageKey);

        var response = await outsider.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(signature.StorageKey, "confidential.pdf", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // And nothing was created for either party.
        var mine = await ListAsync(outsider);
        mine.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Register_RefusesAnUploadThatNeverArrived()
    {
        var (client, _) = await SignInAsync();

        var signature = await SignAsync(client, "abandoned.pdf");

        // Deliberately not landed: the client signed and then the transfer failed.
        var response = await client.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(signature.StorageKey, "abandoned.pdf", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The assertion that matters: a failed upload leaves no phantom row behind.
        var documents = await ListAsync(client);
        documents.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Register_RefusesAFileThatIsNotWhatWasSignedFor_AndDestroysIt()
    {
        // The check §12.1 exists for. The signature covers the key, so the extension in it is the one
        // thing about this upload the client could not edit.
        var (client, _) = await SignInAsync();

        var signature = await SignAsync(client, "slides.pptx");
        _fixture.Files.Land(signature.StorageKey);

        var response = await client.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(signature.StorageKey, "slides.pdf", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // Rejected uploads are not left behind. They are storage nobody is ever going to point a row
        // at, and they are already paid for.
        _fixture.Files.Destroyed.ShouldContain(signature.StorageKey);
        _fixture.Files.Holds(signature.StorageKey).ShouldBeFalse();
    }

    [Fact]
    public async Task Register_RefusesAFileThatIsOverTheLimitOnceItHasLanded()
    {
        // The declared size was fine; the real one is not. Nothing in the signature could have
        // enforced this — Cloudinary has no signed maximum-bytes parameter.
        var (client, _) = await SignInAsync();

        var signature = await SignAsync(client, "recording.pdf", sizeBytes: 1024);
        _fixture.Files.Land(signature.StorageKey, sizeBytes: 512L * 1024 * 1024);

        var response = await client.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(signature.StorageKey, "recording.pdf", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        _fixture.Files.Destroyed.ShouldContain(signature.StorageKey);
    }

    [Fact]
    public async Task Register_RefusesTheSameUploadTwice()
    {
        var (client, _) = await SignInAsync();

        var signature = await SignAsync(client, "notes.txt");
        _fixture.Files.Land(signature.StorageKey);

        await RegisterAsync(client, signature.StorageKey, "notes.txt");

        var second = await client.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(signature.StorageKey, "notes.txt", null, null));

        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_RefusesTheKeyOfADeletedDocument()
    {
        // Soft delete hides the row from the tenant filter, so the duplicate check has to look past
        // it. Two rows over one asset means the second dies when the first is purged.
        var (client, _) = await SignInAsync();

        var signature = await SignAsync(client, "superseded.pdf");
        _fixture.Files.Land(signature.StorageKey);

        var document = await RegisterAsync(client, signature.StorageKey, "superseded.pdf");

        var deleted = await client.DeleteAsync(Url($"/api/v1/documents/{document.Id}"));
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var response = await client.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(signature.StorageKey, "superseded.pdf", null, null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Register_RefusesAMeetingFromAnotherWorkspace()
    {
        var (client, _) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var meeting = await CreateMeetingAsync(outsider, "Board review");

        var signature = await SignAsync(client, "deck.pptx");
        _fixture.Files.Land(signature.StorageKey);

        var response = await client.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(signature.StorageKey, "deck.pptx", meeting.Meeting.Id, null));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /* ---------------------------------------------------------------------- */
    /* Indexing                                                               */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Indexing_ResolvesTheDocumentOutOfProcessing()
    {
        var (client, session) = await SignInAsync();

        var document = await UploadAsync(client, "handbook.pdf", sizeBytes: 3_145_728);

        await RunIndexingAsync(document.Id, session.User.OrganizationId);

        var indexed = (await client.GetJsonAsync<DocumentDto>(
            Url($"/api/v1/documents/{document.Id}")))!;

        indexed.ProcessingStatus.ShouldBe(ProcessingStatus.Indexed);

        // The excerpt describes the file, not its contents — there is no extraction behind this, and
        // saying otherwise would make every empty search result look like a bug in search.
        indexed.Excerpt.ShouldContain("PDF");
        indexed.Excerpt.ShouldContain("3 MB");
    }

    [Fact]
    public async Task Indexing_MarksTheDocumentFailedWhenItsFileHasGone()
    {
        // The state §12.2 asks for: the row survives an asset that vanished, and the UI can say so.
        var (client, session) = await SignInAsync();

        var document = await UploadAsync(client, "lost.pdf");

        await using (var scope = _fixture.CreateDbScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();
            var stored = await context.Documents
                .IgnoreQueryFilters()
                .FirstAsync(row => row.Id == document.Id);

            _fixture.Files.Lose(stored.StorageKey);
        }

        await RunIndexingAsync(document.Id, session.User.OrganizationId);

        var failed = (await client.GetJsonAsync<DocumentDto>(
            Url($"/api/v1/documents/{document.Id}")))!;

        failed.ProcessingStatus.ShouldBe(ProcessingStatus.Failed);
    }

    /* ---------------------------------------------------------------------- */
    /* Listing                                                                */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task List_FiltersByTypeAndFavourite()
    {
        var (client, _) = await SignInAsync();

        var report = await UploadAsync(client, "annual-report.pdf");
        await UploadAsync(client, "budget.csv");

        var favorited = await client.PostAsync(
            Url($"/api/v1/documents/{report.Id}/favorite"),
            content: null);

        favorited.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await favorited.Content.ReadJsonAsync<DocumentDto>())!.IsFavorite.ShouldBeTrue();

        var pdfs = await ListAsync(client, "?type=pdf");
        pdfs.Items.Count.ShouldBe(1);
        pdfs.Items[0].Id.ShouldBe(report.Id);

        var favorites = await ListAsync(client, "?favoritesOnly=true");
        favorites.Items.Count.ShouldBe(1);
        favorites.Items[0].Id.ShouldBe(report.Id);
    }

    [Fact]
    public async Task List_SearchesNamesAndTags()
    {
        var (client, _) = await SignInAsync();

        await UploadAsync(client, "migration-plan.pdf", tags: ["infrastructure"]);
        await UploadAsync(client, "holiday-rota.csv", tags: ["people"]);

        var byName = await ListAsync(client, "?search=migration");
        byName.Items.Count.ShouldBe(1);

        var byTag = await ListAsync(client, "?search=people");
        byTag.Items.Count.ShouldBe(1);
        byTag.Items[0].Name.ShouldBe("holiday-rota.csv");
    }

    [Fact]
    public async Task List_SortsAndPagesStably()
    {
        // Identical sort keys on purpose. Without the id tiebreaker a row can appear on two pages
        // while another never appears at all — invisible in data where the keys happen to differ.
        var (client, _) = await SignInAsync();

        for (var index = 0; index < 6; index++)
        {
            await UploadAsync(client, "identical.pdf", sizeBytes: 1024);
        }

        var first = await ListAsync(client, "?sortBy=sizeBytes&sortDir=asc&page=1&pageSize=3");
        var second = await ListAsync(client, "?sortBy=sizeBytes&sortDir=asc&page=2&pageSize=3");

        first.Total.ShouldBe(6);
        first.Items.Select(document => document.Id)
            .Intersect(second.Items.Select(document => document.Id))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task List_RejectsAFilterValueItDoesNotUnderstand()
    {
        var (client, _) = await SignInAsync();

        var response = await client.GetAsync(Url("/api/v1/documents?processingStatus=nearly"));

        // Named rather than silently dropped: a filter the server ignores shows the caller a list
        // that does not match what they asked for.
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Tags_AreDistinctAndSorted()
    {
        var (client, _) = await SignInAsync();

        await UploadAsync(client, "one.pdf", tags: ["Finance", "q3"]);
        await UploadAsync(client, "two.pdf", tags: ["finance", "roadmap"]);

        var tags = (await client.GetJsonAsync<List<string>>(Url("/api/v1/documents/tags")))!;

        // The aggregate lowercases tags on the way in, so the two spellings of "finance" are one tag.
        tags.ShouldBe(["finance", "q3", "roadmap"]);
    }

    /* ---------------------------------------------------------------------- */
    /* Renaming, downloading and deleting                                     */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Rename_ReclassifiesByTheNewExtension()
    {
        var (client, _) = await SignInAsync();

        var document = await UploadAsync(client, "notes.txt");
        document.Type.ShouldBe(DocumentType.Txt);

        var response = await client.PatchJsonAsync(
            Url($"/api/v1/documents/{document.Id}"),
            new RenameDocumentRequest("notes.csv"));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var renamed = (await response.Content.ReadJsonAsync<DocumentDto>())!;
        renamed.Name.ShouldBe("notes.csv");
        renamed.Type.ShouldBe(DocumentType.Csv);
    }

    [Fact]
    public async Task Download_MintsALinkThatExpires()
    {
        var (client, _) = await SignInAsync();

        var document = await UploadAsync(client, "contract.pdf");

        var download = (await client.GetJsonAsync<DocumentDownloadDto>(
            Url($"/api/v1/documents/{document.Id}/download")))!;

        download.Url.ShouldNotBeNullOrWhiteSpace();
        download.ExpiresAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        download.ExpiresAt.ShouldBeLessThan(DateTimeOffset.UtcNow.AddHours(1));
    }

    [Fact]
    public async Task Download_IsRefusedForAnotherWorkspacesDocument()
    {
        var (owner, _) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var document = await UploadAsync(owner, "private.pdf");

        var response = await outsider.GetAsync(Url($"/api/v1/documents/{document.Id}/download"));

        // 404 rather than 403: "exists but not yours" is a membership oracle.
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_HidesTheRowAndDestroysTheStoredFile()
    {
        var (client, _) = await SignInAsync();

        var document = await UploadAsync(client, "obsolete.pdf");

        string storageKey;

        await using (var scope = _fixture.CreateDbScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();
            storageKey = (await context.Documents
                .IgnoreQueryFilters()
                .FirstAsync(row => row.Id == document.Id)).StorageKey;
        }

        var response = await client.DeleteAsync(Url($"/api/v1/documents/{document.Id}"));
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await ListAsync(client)).Items.ShouldBeEmpty();

        // The destroy runs as a job, so it is driven here rather than waited for — the worker is off
        // in the test host precisely so it cannot race these assertions.
        await RunPurgeAsync(storageKey);

        _fixture.Files.Holds(storageKey).ShouldBeFalse();
    }

    [Fact]
    public async Task BulkDelete_ReportsWhatItRemoved()
    {
        var (client, _) = await SignInAsync();

        var first = await UploadAsync(client, "a.pdf");
        var second = await UploadAsync(client, "b.pdf");

        var response = await client.PostJsonAsync(
            Url("/api/v1/documents/bulk-delete"),
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

    /// <summary>The whole handshake: sign, land the file, register.</summary>
    private async Task<DocumentDto> UploadAsync(
        HttpClient client,
        string fileName,
        long sizeBytes = 2048,
        IReadOnlyList<string>? tags = null)
    {
        var signature = await SignAsync(client, fileName, sizeBytes);
        _fixture.Files.Land(signature.StorageKey, sizeBytes);

        return await RegisterAsync(client, signature.StorageKey, fileName, tags: tags);
    }

    private async Task<UploadSignatureDto> SignAsync(
        HttpClient client,
        string fileName,
        long sizeBytes = 2048)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/documents/upload-signature"),
            new UploadSignatureRequest(fileName, ContentTypeOf(fileName), sizeBytes));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        return (await response.Content.ReadJsonAsync<UploadSignatureDto>())!;
    }

    private static async Task<DocumentDto> RegisterAsync(
        HttpClient client,
        string storageKey,
        string fileName,
        Guid? meetingId = null,
        IReadOnlyList<string>? tags = null)
    {
        var response = await client.PostJsonAsync(
            Url("/api/v1/documents"),
            new RegisterDocumentRequest(storageKey, fileName, meetingId, tags));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        return (await response.Content.ReadJsonAsync<DocumentDto>())!;
    }

    private static Task<PagedResult<DocumentDto>> ListAsync(HttpClient client, string query = "") =>
        client.GetJsonAsync<PagedResult<DocumentDto>>(Url($"/api/v1/documents{query}"))!;

    private async Task RunIndexingAsync(Guid documentId, Guid organizationId)
    {
        await using var scope = _fixture.CreateDbScope();
        var job = scope.ServiceProvider.GetRequiredService<IIndexDocumentJob>();

        await job.RunAsync(documentId, organizationId);
    }

    private async Task RunPurgeAsync(string storageKey)
    {
        await using var scope = _fixture.CreateDbScope();
        var job = scope.ServiceProvider.GetRequiredService<IPurgeDocumentAssetJob>();

        await job.RunAsync(storageKey);
    }

    private static string ContentTypeOf(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream",
        };

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
