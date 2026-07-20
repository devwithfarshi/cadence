using System.Net.Http.Headers;
using Cadence.Api.Realtime;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Transcripts;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using Cadence.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// The live meeting channel, driven by a real SignalR client over the test server.
/// </summary>
/// <remarks>
/// A real hub connection rather than calling hub methods directly. The things most likely to be
/// wrong here are not in the method bodies: whether the JWT survives the negotiate handshake,
/// whether group membership actually routes a message, and whether <c>ICurrentUser</c> resolves at
/// all inside a hub invocation. Invoking the class directly would pass while every one of those was
/// broken.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class MeetingHubTests
{
    private readonly AuthFixture _fixture;

    public MeetingHubTests(AuthFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task JoiningAMeeting_RequiresItToBeVisibleToTheCaller()
    {
        // Group membership *is* the authorization boundary: joining a group is what grants the
        // stream, so a meeting id from another workspace has to be refused here rather than trusted.
        var (_, owner) = await SignInAsync();
        var (_, outsider) = await SignInAsync();

        var meetingId = await SeedMeetingAsync(owner.User.OrganizationId, owner.User.Id);

        await using var connection = await ConnectAsync(outsider.AccessToken);

        var refused = await Should.ThrowAsync<HubException>(
            () => connection.InvokeAsync("JoinMeeting", meetingId));

        refused.Message.ShouldContain("could not be found");
    }

    [Fact]
    public async Task JoiningAMeeting_SucceedsForItsOwnWorkspace()
    {
        // The test that proves ICurrentUser resolves inside a hub invocation at all. Without the
        // principal filter this fails silently in the worst way: the tenant filter falls back to
        // Guid.Empty, the meeting is "not found", and nothing anywhere throws.
        var (_, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        await using var connection = await ConnectAsync(session.AccessToken);

        await Should.NotThrowAsync(() => connection.InvokeAsync("JoinMeeting", meetingId));
    }

    [Fact]
    public async Task AnUnauthenticatedConnection_IsRefused()
    {
        await Should.ThrowAsync<Exception>(() => ConnectAsync(accessToken: null));
    }

    [Fact]
    public async Task APushedSegment_ReachesTheOtherWatchersImmediately()
    {
        // Broadcast and persistence run at deliberately different speeds: watchers see the line now,
        // the database sees it on the next flush. This covers the "now" half.
        var (_, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        await using var speaker = await ConnectAsync(session.AccessToken);
        await using var watcher = await ConnectAsync(session.AccessToken);

        var received = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        watcher.On<Guid, AppendSegmentRequest>("SegmentCaptured", (_, segment) =>
            received.TrySetResult(segment.Text));

        await speaker.InvokeAsync("JoinMeeting", meetingId);
        await watcher.InvokeAsync("JoinMeeting", meetingId);

        await speaker.InvokeAsync(
            "PushSegment",
            meetingId,
            new AppendSegmentRequest(null, "Alex Rivera", 0, 2_000, "Can everyone hear me?", 0.97, false));

        var text = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        text.ShouldBe("Can everyone hear me?");
    }

    [Fact]
    public async Task AWatcherOfAnotherMeeting_ReceivesNothing()
    {
        var (_, session) = await SignInAsync();
        var watched = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);
        var other = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        await using var speaker = await ConnectAsync(session.AccessToken);
        await using var watcher = await ConnectAsync(session.AccessToken);

        var received = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        watcher.On<Guid, AppendSegmentRequest>("SegmentCaptured", (_, segment) =>
            received.TrySetResult(segment.Text));

        await speaker.InvokeAsync("JoinMeeting", other);
        await watcher.InvokeAsync("JoinMeeting", watched);

        await speaker.InvokeAsync(
            "PushSegment",
            other,
            new AppendSegmentRequest(null, "Alex Rivera", 0, 1_000, "Different room.", 0.9, false));

        // A group per meeting is what keeps two concurrent meetings from bleeding into each other.
        var delivered = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        delivered.ShouldNotBe(received.Task);
    }

    [Fact]
    public async Task PushedSegments_AreBufferedAndFlushedAsABatch()
    {
        // The "later" half: the write is batched, so nothing is in Postgres immediately, and the
        // whole batch lands in one go.
        var (client, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        await using var speaker = await ConnectAsync(session.AccessToken);
        await speaker.InvokeAsync("JoinMeeting", meetingId);

        for (var index = 0; index < 5; index++)
        {
            await speaker.InvokeAsync(
                "PushSegment",
                meetingId,
                new AppendSegmentRequest(
                    null,
                    "Alex Rivera",
                    index * 1_000,
                    (index * 1_000) + 900,
                    $"Line {index}.",
                    0.95,
                    false));
        }

        // Not written yet — one insert per utterance is exactly what the buffer exists to avoid.
        (await StoredSegmentCountAsync(meetingId)).ShouldBe(0);

        await WaitForSegmentsAsync(meetingId, expected: 5);

        var transcript = await client.GetJsonAsync<List<TranscriptSegmentDto>>(
            Url($"/api/v1/meetings/{meetingId}/transcript"));

        transcript!.Count.ShouldBe(5);
        transcript.Select(segment => segment.Text).ShouldBe(
            ["Line 0.", "Line 1.", "Line 2.", "Line 3.", "Line 4."]);
    }

    [Fact]
    public async Task TheFlushedBatch_IsBroadcastWithItsStoredIds()
    {
        // The echo a client gets on push has no id — the line has none until it is written. The
        // flush broadcast is what lets the client reconcile its provisional lines with real rows.
        var (_, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(session.User.OrganizationId, session.User.Id);

        await using var connection = await ConnectAsync(session.AccessToken);
        await connection.InvokeAsync("JoinMeeting", meetingId);

        var stored = new TaskCompletionSource<IReadOnlyList<TranscriptSegmentDto>>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<Guid, IReadOnlyList<TranscriptSegmentDto>>("SegmentsAppended", (_, segments) =>
            stored.TrySetResult(segments));

        await connection.InvokeAsync(
            "PushSegment",
            meetingId,
            new AppendSegmentRequest(null, "Alex Rivera", 0, 1_000, "Reconcile me.", 0.95, false));

        var segments = await stored.Task.WaitAsync(TimeSpan.FromSeconds(15));

        var segment = segments.ShouldHaveSingleItem();
        segment.Id.ShouldNotBe(Guid.Empty);
        segment.Text.ShouldBe("Reconcile me.");
    }

    [Fact]
    public async Task PushingToAMeetingInAnotherWorkspace_IsRefused()
    {
        // Re-checked on push rather than inferred from an earlier join: a connection can call this
        // without ever having joined, and "they must have joined first" is an assumption about a
        // client we do not control.
        var (_, owner) = await SignInAsync();
        var (_, outsider) = await SignInAsync();

        var meetingId = await SeedMeetingAsync(owner.User.OrganizationId, owner.User.Id);

        await using var connection = await ConnectAsync(outsider.AccessToken);

        await Should.ThrowAsync<HubException>(() => connection.InvokeAsync(
            "PushSegment",
            meetingId,
            new AppendSegmentRequest(null, "Intruder", 0, 1_000, "Let me in.", 0.9, false)));

        await Task.Delay(TimeSpan.FromSeconds(3));

        (await StoredSegmentCountAsync(meetingId)).ShouldBe(0);
    }

    [Fact]
    public async Task SegmentsForAFinishedMeeting_AreRefusedRatherThanAppendedLate()
    {
        // A transcript that keeps growing after the meeting ended would contradict the summary
        // already generated from it.
        var (client, session) = await SignInAsync();
        var meetingId = await SeedMeetingAsync(
            session.User.OrganizationId,
            session.User.Id,
            completed: true);

        await using var connection = await ConnectAsync(session.AccessToken);
        await connection.InvokeAsync("JoinMeeting", meetingId);

        await connection.InvokeAsync(
            "PushSegment",
            meetingId,
            new AppendSegmentRequest(null, "Alex Rivera", 0, 1_000, "One more thing.", 0.9, false));

        // The push itself is accepted — the buffer does not know the meeting's state — and the flush
        // discards it rather than retrying forever against a closed transcript.
        await Task.Delay(TimeSpan.FromSeconds(4));

        (await StoredSegmentCountAsync(meetingId)).ShouldBe(0);

        var transcript = await client.GetJsonAsync<List<TranscriptSegmentDto>>(
            Url($"/api/v1/meetings/{meetingId}/transcript"));

        transcript.ShouldBeEmpty();
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    /// <summary>
    /// Opens a hub connection over the in-memory test server.
    /// </summary>
    /// <remarks>
    /// The token goes in the query string, which is how a browser authenticates a websocket — the
    /// WebSocket API gives no way to set request headers. <c>HttpMessageHandlerFactory</c> is what
    /// routes the connection at the <c>TestServer</c> instead of a real socket.
    /// </remarks>
    private async Task<HubConnection> ConnectAsync(string? accessToken)
    {
        var server = _fixture.Server;

        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(server.BaseAddress, HubAuthentication.Path.TrimStart('/')),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    options.AccessTokenProvider = () => Task.FromResult(accessToken);
                })
            .Build();

        await connection.StartAsync();

        return connection;
    }

    private async Task<int> StoredSegmentCountAsync(Guid meetingId)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        return await context.TranscriptSegments
            .IgnoreQueryFilters()
            .CountAsync(segment => segment.MeetingId == meetingId);
    }

    /// <summary>Polls until the flush loop has written the batch, or gives up.</summary>
    /// <remarks>
    /// Polling rather than a fixed sleep: the flush runs on a timer, so a sleep tuned to it is a
    /// test that fails on a loaded machine and passes on a quiet one.
    /// </remarks>
    private async Task WaitForSegmentsAsync(Guid meetingId, int expected)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await StoredSegmentCountAsync(meetingId) >= expected)
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException(
            $"Only {await StoredSegmentCountAsync(meetingId)} of {expected} segments were flushed.");
    }

    private async Task<Guid> SeedMeetingAsync(
        Guid organizationId,
        Guid organizerId,
        bool completed = false)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var meeting = Meeting.Schedule(
            organizationId,
            organizerId,
            "Live session",
            "Seeded by the hub suite.",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            MeetingPlatform.GoogleMeet);

        if (completed)
        {
            meeting.StartRecording();
            meeting.Complete(600);
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

        var session = (await response.Content.ReadJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", session.AccessToken);

        return (client, session);
    }

    private static Uri Url(string relative) => new(relative, UriKind.Relative);
}
