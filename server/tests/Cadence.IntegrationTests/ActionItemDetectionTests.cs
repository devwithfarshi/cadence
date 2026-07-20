using System.Net;
using System.Net.Http.Headers;
using Cadence.Application.Common.Abstractions;
using Cadence.Application.Modules.ActionItems;
using Cadence.Application.Modules.Auth;
using Cadence.Application.Modules.Summaries;
using Cadence.Application.Modules.Users;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using Cadence.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace Cadence.IntegrationTests;

/// <summary>
/// Turning the commitments a model found into real tasks — and, more often, not.
/// </summary>
/// <remarks>
/// The rule these protect is the one the <c>RequireActionItemReview</c> preference exists for:
/// <b>extraction must never silently assign work to a colleague</b>. It is defended the same way
/// §23.3 is — by asserting the <i>absence</i> of rows, because a test that only checks what a
/// correctly extracted task looks like passes just as happily when the gate is gone.
/// </remarks>
[Collection(DatabaseCollection.Name)]
public sealed class ActionItemDetectionTests
{
    private readonly AuthFixture _fixture;

    public ActionItemDetectionTests(AuthFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task WithReviewRequired_NoTasksAreCreated()
    {
        // The default, and the assertion that matters: the model flagged two commitments and the
        // workspace ends up with zero tasks. Anything that "helpfully" creates them anyway would
        // pass every other test in this file.
        var (client, session) = await SignInAsync();
        var meetingId = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            actionItems:
            [
                new ActionItemCandidate("Write the rollback plan", null, "medium", null),
                new ActionItemCandidate("Book the maintenance window", null, "high", null),
            ]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        (await TasksOf(client, meetingId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task WithReviewTurnedOff_TheCommitmentsBecomeTasks()
    {
        var (client, session) = await SignInAsync();
        await AllowAutoCreationAsync(client);
        var meetingId = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            actionItems: [new ActionItemCandidate("Write the rollback plan", null, "medium", null)]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var task = (await TasksOf(client, meetingId)).ShouldHaveSingleItem();
        task.Title.ShouldBe("Write the rollback plan");
        task.Status.ShouldBe(ActionItemStatus.Todo);
        // Filed by the organizer, so the creator is stable across a regeneration.
        task.CreatorId.ShouldBe(session.User.Id);
    }

    [Fact]
    public async Task WithAutoExtractTurnedOff_NothingIsCreatedEither()
    {
        var (client, session) = await SignInAsync();
        await SetAiPreferencesAsync(client, autoExtract: false, requireReview: false);
        var meetingId = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            actionItems: [new ActionItemCandidate("Write the rollback plan", null, "medium", null)]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        (await TasksOf(client, meetingId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAssignee_IsResolvedFromThePeopleWhoWereInTheMeeting()
    {
        // Matching the name against the workspace directory instead would let "Sam" in a meeting Sam
        // never attended assign work to a different Sam entirely.
        var (client, session) = await SignInAsync();
        await AllowAutoCreationAsync(client);

        var me = await client.GetJsonAsync<UserDto>(Url("/api/v1/users/me"));
        var meetingId = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            actionItems:
            [
                new ActionItemCandidate("Mine to do", me!.Name, "medium", null),
                new ActionItemCandidate("Nobody's in particular", "Someone Else", "medium", null),
            ]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var tasks = await TasksOf(client, meetingId);

        tasks.Single(task => task.Title == "Mine to do").AssigneeId.ShouldBe(session.User.Id);
        // A name nobody in the room answers to leaves the task unassigned, which is the honest
        // answer when nothing establishes who committed to it.
        tasks.Single(task => task.Title == "Nobody's in particular").AssigneeId.ShouldBeNull();
    }

    [Fact]
    public async Task ACitationIsKeptOnlyWhenItResolves()
    {
        var (client, session) = await SignInAsync();
        await AllowAutoCreationAsync(client);
        var meetingId = await SeedRecordedMeetingAsync(session);

        var segmentId = await FirstSegmentIdAsync(meetingId);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            actionItems:
            [
                new ActionItemCandidate("Traceable", null, "medium", segmentId),
                new ActionItemCandidate("Invented citation", null, "medium", Guid.CreateVersion7()),
            ]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var tasks = await TasksOf(client, meetingId);

        tasks.Single(task => task.Title == "Traceable").SourceSegmentId.ShouldBe(segmentId);
        tasks.Single(task => task.Title == "Invented citation").SourceSegmentId.ShouldBeNull();
    }

    [Fact]
    public async Task RegeneratingASummary_DoesNotFileTheSameCommitmentTwice()
    {
        // Hangfire delivers at least once and a user can regenerate by hand, so this path runs more
        // than once for some meeting. Without the check every rerun duplicates the whole list.
        var (client, session) = await SignInAsync();
        await AllowAutoCreationAsync(client);
        var meetingId = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            actionItems: [new ActionItemCandidate("Write the rollback plan", null, "medium", null)]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);
        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        (await TasksOf(client, meetingId)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task ATaskDeletedByHand_IsNotResurrectedByARegeneration()
    {
        // Deleting an auto-created task is a decision about that commitment. A rerun must not
        // quietly undo it — which is why the duplicate check counts soft-deleted rows as existing.
        var (client, session) = await SignInAsync();
        await AllowAutoCreationAsync(client);
        var meetingId = await SeedRecordedMeetingAsync(session);

        _fixture.Llm.Returns(FakeLlmProvider.Summary(
            actionItems: [new ActionItemCandidate("Write the rollback plan", null, "medium", null)]));

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        var task = (await TasksOf(client, meetingId)).ShouldHaveSingleItem();
        (await client.DeleteAsync(Url($"/api/v1/action-items/{task.Id}")))
            .StatusCode.ShouldBe(HttpStatusCode.NoContent);

        await RunSummarisationAsync(meetingId, session.User.OrganizationId);

        (await TasksOf(client, meetingId)).ShouldBeEmpty();
    }

    /* ---------------------------------------------------------------------- */
    /* Helpers                                                                */
    /* ---------------------------------------------------------------------- */

    private static async Task<IReadOnlyList<ActionItemDto>> TasksOf(HttpClient client, Guid meetingId)
    {
        var tasks = await client.GetJsonAsync<IReadOnlyList<ActionItemDto>>(
            Url($"/api/v1/meetings/{meetingId}/action-items"));

        return tasks!;
    }

    /// <summary>Turns off the review gate, which is on by default.</summary>
    private static Task AllowAutoCreationAsync(HttpClient client) =>
        SetAiPreferencesAsync(client, autoExtract: true, requireReview: false);

    private static async Task SetAiPreferencesAsync(
        HttpClient client,
        bool autoExtract,
        bool requireReview)
    {
        var preferences = await client.GetJsonAsync<PreferencesDto>(
            Url("/api/v1/users/me/preferences"));

        var updated = preferences! with
        {
            Ai = preferences.Ai with
            {
                AutoExtractActionItems = autoExtract,
                RequireActionItemReview = requireReview,
            },
        };

        (await client.PutJsonAsync(Url("/api/v1/users/me/preferences"), updated))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task RunSummarisationAsync(Guid meetingId, Guid organizationId)
    {
        await using var scope = _fixture.CreateDbScope();
        var job = scope.ServiceProvider.GetRequiredService<ISummarizeMeetingJob>();

        await job.RunAsync(meetingId, organizationId);
    }

    private async Task<Guid> FirstSegmentIdAsync(Guid meetingId)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        return await context.TranscriptSegments
            .IgnoreQueryFilters()
            .Where(segment => segment.MeetingId == meetingId)
            .OrderBy(segment => segment.StartMs)
            .Select(segment => segment.Id)
            .FirstAsync();
    }

    private async Task<Guid> SeedRecordedMeetingAsync(AuthResponse session)
    {
        using var scope = _fixture.CreateDbScope();
        var context = scope.ServiceProvider.GetRequiredService<CadenceDbContext>();

        var user = await context.Users
            .IgnoreQueryFilters()
            .FirstAsync(candidate => candidate.Id == session.User.Id);

        var meeting = Meeting.Schedule(
            session.User.OrganizationId,
            session.User.Id,
            "Migration planning",
            "Seeded by the detection suite.",
            DateTimeOffset.UtcNow.AddHours(-2),
            DateTimeOffset.UtcNow.AddHours(-1),
            MeetingPlatform.Zoom);

        // The organizer attends their own meeting, which is what makes them resolvable by name.
        meeting.AddParticipant(user.Id, user.Name, user.Email, ParticipantRole.Host);
        meeting.StartRecording();
        meeting.Complete(3600);

        context.Meetings.Add(meeting);

        foreach (var (start, end, text) in new[]
        {
            (0, 4_000, "We should ship the migration on Friday."),
            (4_000, 9_000, "I'll write the rollback plan."),
        })
        {
            context.TranscriptSegments.Add(TranscriptSegment.Create(
                meeting.Id,
                speakerId: user.Id,
                speakerName: user.Name,
                start,
                end,
                text,
                confidence: 0.96));
        }

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
