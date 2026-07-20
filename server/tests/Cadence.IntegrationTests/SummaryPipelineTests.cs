using System.Net;
using System.Net.Http.Headers;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Summaries;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using Cadence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// The meeting processing pipeline: summarisation, its failure behaviour, and regeneration.
/// </summary>
/// <remarks>
/// The rule these exist to protect is §23.3 — <b>a failed summary is recorded as failed and never
/// replaced with invented content</b>. It is the kind of rule that decays quietly: nothing breaks if
/// someone later adds a "helpful" fallback that assembles a summary from the transcript, and no
/// user could tell the difference from the real thing. Hence a test that asserts the absence.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class SummaryPipelineTests
{
    private readonly AuthFixture _fixture;

    public SummaryPipelineTests(AuthFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Summarising_StoresTheSummaryAndMarksTheMeetingReady()
    {
        var (client, session) = await SignInAsync();
        var (meetingId, segmentIds) = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            "The team agreed to ship the migration on Friday.",
            ["Migration ships Friday.", "Rollback plan still outstanding."],
            [new SummaryHighlightCandidate("decision", "Ship on Friday.", segmentIds[0])]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var summary = await client.GetJsonAsync<AiSummaryDto>(
            Url($"/api/v1/meetings/{meetingId}/summary"));

        summary.ShouldNotBeNull();
        summary.ExecutiveSummary.ShouldBe("The team agreed to ship the migration on Friday.");
        summary.KeyPoints.Count.ShouldBe(2);
        // Provenance: the reader can see which model wrote this.
        summary.Model.ShouldBe("claude-opus-4-8");

        var highlight = summary.Highlights.ShouldHaveSingleItem();
        highlight.Kind.ShouldBe(SummaryHighlightKind.Decision);
        // Traceable back to the line it came from, which is the whole point of a highlight.
        highlight.SourceSegmentId.ShouldBe(segmentIds[0]);
        highlight.AtSeconds.ShouldBe(0);

        (await SummaryStatusOf(meetingId)).ShouldBe(SummaryStatus.Ready);
    }

    [Fact]
    public async Task AFailedProvider_MarksTheMeetingFailedAndWritesNoSummary()
    {
        // §23.3, stated as a test. The absence of a summary row is the assertion that matters: a
        // fabricated one would satisfy every other check in this file.
        var (client, session) = await SignInAsync();
        var (meetingId, _) = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Throws();

        await RunSummarisationAsync(meetingId, session.User.OrganizationId, expectFailure: true);

        (await SummaryStatusOf(meetingId)).ShouldBe(SummaryStatus.Failed);

        var response = await client.GetAsync(Url($"/api/v1/meetings/{meetingId}/summary"));
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        (await context.AiSummaries
            .IgnoreQueryFilters()
            .CountAsync(summary => summary.MeetingId == meetingId))
            .ShouldBe(0);
    }

    [Fact]
    public async Task AnEmptySummary_IsTreatedAsAFailureRatherThanStored()
    {
        // A model that returns nothing has not produced a summary. Storing the empty string would
        // show the user a blank summary they might read as "nothing was said".
        var (_, session) = await SignInAsync();
        var (meetingId, _) = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(text: "   "));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId, expectFailure: true);

        (await SummaryStatusOf(meetingId)).ShouldBe(SummaryStatus.Failed);
    }

    [Fact]
    public async Task Regenerating_ReplacesTheSummaryRatherThanAddingASecond()
    {
        // Hangfire delivers at least once, so this path runs twice for some meeting whether or not
        // a user asks. One meeting must end up with one summary.
        var (client, session) = await SignInAsync();
        var (meetingId, _) = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary("First pass."));
        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var first = await client.GetJsonAsync<AiSummaryDto>(
            Url($"/api/v1/meetings/{meetingId}/summary"));

        _fixture.Llm.Returns(FakeLlmProvider.Summary("Second pass, better."));
        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var second = await client.GetJsonAsync<AiSummaryDto>(
            Url($"/api/v1/meetings/{meetingId}/summary"));

        second!.ExecutiveSummary.ShouldBe("Second pass, better.");
        // Replaced in place, so anything linking to the summary still resolves.
        second.Id.ShouldBe(first!.Id);

        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        (await context.AiSummaries
            .IgnoreQueryFilters()
            .CountAsync(summary => summary.MeetingId == meetingId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task RegeneratingDropsTheOldHighlights()
    {
        var (client, session) = await SignInAsync();
        var (meetingId, segmentIds) = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            highlights:
            [
                new SummaryHighlightCandidate("decision", "Old one.", segmentIds[0]),
                new SummaryHighlightCandidate("risk", "Another old one.", segmentIds[1]),
            ]));
        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            highlights: [new SummaryHighlightCandidate("question", "The only new one.", null)]));
        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var summary = await client.GetJsonAsync<AiSummaryDto>(
            Url($"/api/v1/meetings/{meetingId}/summary"));

        // Not three. A regeneration that appended would leave the reader with claims from a summary
        // that no longer exists, indistinguishable from the current ones.
        summary!.Highlights.ShouldHaveSingleItem().Text.ShouldBe("The only new one.");
    }

    [Fact]
    public async Task AHighlightCitingAnUnknownSegment_IsStoredUnattributed()
    {
        // A citation that does not resolve is worse than none: it looks checkable and is not.
        var (client, session) = await SignInAsync();
        var (meetingId, _) = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            highlights:
            [
                new SummaryHighlightCandidate("decision", "Invented citation.", Guid.CreateVersion7()),
            ]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var summary = await client.GetJsonAsync<AiSummaryDto>(
            Url($"/api/v1/meetings/{meetingId}/summary"));

        var highlight = summary!.Highlights.ShouldHaveSingleItem();
        highlight.Text.ShouldBe("Invented citation.");
        highlight.SourceSegmentId.ShouldBeNull();
        highlight.AtSeconds.ShouldBeNull();
    }

    [Fact]
    public async Task TheTranscriptHandedToTheModel_CarriesSegmentIds()
    {
        // The ids are what let the model cite a line back. Without them a highlight can only
        // paraphrase, and nothing links a claim to the moment it came from.
        var (_, session) = await SignInAsync();
        var (meetingId, segmentIds) = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary());
        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        _fixture.Llm.LastTranscript.ShouldNotBeNull();
        _fixture.Llm.LastTranscript.ShouldContain(segmentIds[0].ToString());
        _fixture.Llm.LastTranscript.ShouldContain("Alex Rivera:");
    }

    /* ---------------------------------------------------------------------- */
    /* The regeneration endpoint                                              */
    /* ---------------------------------------------------------------------- */

    [Fact]
    public async Task Regenerate_Returns202AndQueuesTheMeeting()
    {
        var (client, session) = await SignInAsync();
        var (meetingId, _) = await SeedRecordedMeetingAsync(session);

        var response = await client.PostAsync(Url($"/api/v1/meetings/{meetingId}/summary"), null);

        // 202, not 200: the work is accepted, not done. Holding the request open for a model call
        // would tie up a connection for minutes.
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var accepted = (await response.Content.ReadJsonAsync<JobAcceptedDto>())!;
        accepted.JobId.ShouldNotBeNullOrWhiteSpace();

        (await SummaryStatusOf(meetingId)).ShouldBe(SummaryStatus.Queued);
    }

    [Fact]
    public async Task RegeneratingAMeetingWithNoTranscript_IsRefusedRatherThanQueued()
    {
        // Queuing a job that is certain to fail is noise. Nothing about waiting changes the answer.
        var (client, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session, recorded: false);

        var response = await client.PostAsync(Url($"/api/v1/meetings/{meetingId}/summary"), null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ASummary_IsInvisibleToAnotherWorkspace()
    {
        var (_, owner) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var (meetingId, _) = await SeedRecordedMeetingAsync(owner);

        _fixture.Llm.Returns(FakeLlmProvider.Summary("Confidential."));
        await RunSummarisationAsync(meetingId, owner.User.OrganizationId);

        var response = await outsider.GetAsync(Url($"/api/v1/meetings/{meetingId}/summary"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AMeetingWithNoSummaryYet_Is404RatherThanAnEmptyBody()
    {
        var (client, session) = await SignInAsync();
        var (meetingId, _) = await SeedRecordedMeetingAsync(session);

        var response = await client.GetAsync(Url($"/api/v1/meetings/{meetingId}/summary"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    /// <summary>
    /// Runs the job the way Hangfire would.
    /// </summary>
    /// <remarks>
    /// The real job class in a real DI scope, so the principal staging and the handler are both
    /// exercised. The worker is disabled in the fixture — otherwise it would race these assertions
    /// to run the very job under test.
    /// </remarks>
    private async Task RunSummarisationAsync(
        Guid meetingId,
        Guid organizationId,
        bool expectFailure = false)
    {
        await using var scope = _fixture.CreateDbScope();
        var job = scope.ServiceProvider.GetRequiredService<ISummarizeMeetingJob>();

        if (expectFailure)
        {
            // The job rethrows so Hangfire records the attempt and applies its retry policy; the
            // terminal state has already been written by the time it throws.
            await Should.ThrowAsync<Exception>(() => job.RunAsync(meetingId, organizationId));
            return;
        }

        await job.RunAsync(meetingId, organizationId);
    }

    private async Task<SummaryStatus> SummaryStatusOf(Guid meetingId)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        return await context.Meetings
            .IgnoreQueryFilters()
            .Where(meeting => meeting.Id == meetingId)
            .Select(meeting => meeting.SummaryStatus)
            .FirstAsync();
    }

    private async Task<(Guid MeetingId, IReadOnlyList<Guid> SegmentIds)> SeedRecordedMeetingAsync(
        AuthResponse session)
    {
        var meetingId = await SeedMeetingAsync(session, recorded: true);

        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var lines = new[]
        {
            (0, 4_000, "We should ship the migration on Friday."),
            (4_000, 9_000, "Agreed, but we still need a rollback plan."),
        };

        var ids = new List<Guid>();

        foreach (var (start, end, text) in lines)
        {
            var segment = TranscriptSegment.Create(
                meetingId,
                speakerId: null,
                speakerName: "Alex Rivera",
                start,
                end,
                text,
                confidence: 0.96);

            context.TranscriptSegments.Add(segment);
            ids.Add(segment.Id);
        }

        await context.SaveChangesAsync();

        return (meetingId, ids);
    }

    private async Task<Guid> SeedMeetingAsync(AuthResponse session, bool recorded)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var meeting = Meeting.Schedule(
            session.User.OrganizationId,
            session.User.Id,
            "Migration planning",
            "Seeded by the summary suite.",
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1),
            MeetingPlatform.Zoom);

        if (recorded)
        {
            meeting.StartRecording();
            meeting.Complete(3600);
        }

        context.Meetings.Add(meeting);
        await context.SaveChangesAsync();

        return meeting.Id;
    }

    private async Task<(HttpClient Client, AuthResponse Session)> SignInAsync()
    {
        var address = $"user-{Guid.CreateVersion7():n}@northwind.io";
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
