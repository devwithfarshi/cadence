using System.Net;
using System.Net.Http.Headers;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Transcripts;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using Cadence.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// The transcript read: ordering, search, unit conversion and its tenant boundary.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class TranscriptFlowTests
{
    private readonly AuthFixture _fixture;

    public TranscriptFlowTests(AuthFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Transcript_IsReturnedInPlaybackOrder()
    {
        var (client, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        // Inserted out of order on purpose: playback order is the query's job, not the insert's.
        await SeedSegmentsAsync(
            meetingId,
            (12_000, 15_000, "Third thing."),
            (0, 4_000, "First thing."),
            (4_500, 11_000, "Second thing."));

        var transcript = await client.GetJsonAsync<List<TranscriptSegmentDto>>(
            Url($"/api/v1/meetings/{meetingId}/transcript"));

        transcript!.Select(segment => segment.Text)
            .ShouldBe(["First thing.", "Second thing.", "Third thing."]);
    }

    [Fact]
    public async Task Transcript_ReportsOffsetsInSecondsThoughTheyAreStoredInMilliseconds()
    {
        // The entity keeps milliseconds because that is what speech-to-text emits and rounding at
        // ingest makes segments overlap or leave unrecoverable gaps. The player takes seconds, so
        // the conversion happens once, here.
        var (client, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        await SeedSegmentsAsync(meetingId, (1_500, 4_250, "Half-second boundaries survive."));

        var transcript = await client.GetJsonAsync<List<TranscriptSegmentDto>>(
            Url($"/api/v1/meetings/{meetingId}/transcript"));

        var segment = transcript.ShouldHaveSingleItem();
        segment.StartSeconds.ShouldBe(1.5);
        segment.EndSeconds.ShouldBe(4.25);
    }

    [Fact]
    public async Task Transcript_SearchFiltersToMatchingLines()
    {
        var (client, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        await SeedSegmentsAsync(
            meetingId,
            (0, 1_000, "We should ship the migration on Friday."),
            (1_000, 2_000, "Agreed on the timeline."),
            (2_000, 3_000, "The MIGRATION needs a rollback plan."));

        var matches = await client.GetJsonAsync<List<TranscriptSegmentDto>>(
            Url($"/api/v1/meetings/{meetingId}/transcript?search=migration"));

        matches!.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Transcript_IsInvisibleToAnotherWorkspace()
    {
        // TranscriptSegment is deliberately not ITenantScoped — it hangs off its meeting and dies
        // with it — so the meeting is what carries the boundary. Querying segments by meeting id
        // alone would hand another workspace's transcript to anyone who guessed an id.
        var (_, owner) = await SignInAsync();
        var (outsider, _) = await SignInAsync();

        var meetingId = await SeedMeetingAsync(owner.User.OrganizationId, owner.User.Id);
        await SeedSegmentsAsync(meetingId, (0, 1_000, "Confidential."));

        var response = await outsider.GetAsync(Url($"/api/v1/meetings/{meetingId}/transcript"));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Transcript_OfAMeetingWithNoRecording_IsEmptyRatherThanMissing()
    {
        // An empty list, not a 404: the meeting exists and simply has nothing said in it yet, and
        // the transcript pane should render "nothing here" rather than an error.
        var (client, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        var transcript = await client.GetJsonAsync<List<TranscriptSegmentDto>>(
            Url($"/api/v1/meetings/{meetingId}/transcript"));

        transcript.ShouldBeEmpty();
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    private async Task<Guid> SeedMeetingAsync(Guid organizationId, Guid organizerId)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var meeting = Meeting.Schedule(
            organizationId,
            organizerId,
            "Recorded session",
            "Seeded by the transcript suite.",
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1),
            MeetingPlatform.Zoom);

        context.Meetings.Add(meeting);
        await context.SaveChangesAsync();

        return meeting.Id;
    }

    private async Task SeedSegmentsAsync(
        Guid meetingId,
        params (int StartMs, int EndMs, string Text)[] lines)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        foreach (var line in lines)
        {
            context.TranscriptSegments.Add(TranscriptSegment.Create(
                meetingId,
                speakerId: null,
                speakerName: "Alex Rivera",
                line.StartMs,
                line.EndMs,
                line.Text,
                confidence: 0.96));
        }

        await context.SaveChangesAsync();
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
