using Cadence.Api.Common;
using Microsoft.AspNetCore.SignalR;
using Cadence.Application.Modules.Transcripts;
using Mediator;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Cadence.Api.Realtime;

/// <summary>
/// Writes the buffered transcript to Postgres on a timer, and drains it on shutdown.
/// </summary>
/// <remarks>
/// The interval is the live transcript's durability window: anything captured since the last tick
/// exists only in memory. Two seconds keeps that window small enough that a crash costs a sentence
/// or two, while still collapsing a meeting's per-utterance inserts into one round trip.
/// </remarks>
public sealed class TranscriptFlushService(
    TranscriptIngestBuffer buffer,
    IServiceScopeFactory scopeFactory,
    ILogger<TranscriptFlushService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAllAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }

        // One last pass with a fresh token: the stopping token is already cancelled, and a flush
        // that inherits it would abandon exactly the lines this is here to save.
        await FlushAllAsync(CancellationToken.None);
    }

    /// <summary>
    /// Flushes one meeting immediately, ahead of the timer.
    /// </summary>
    /// <remarks>
    /// Used when a meeting's buffer crosses its threshold, so a busy meeting does not sit on a large
    /// backlog for the rest of the tick.
    /// </remarks>
    public async Task FlushAsync(Guid meetingId, CancellationToken cancellationToken = default)
    {
        var (organizationId, segments) = buffer.Drain(meetingId);

        if (segments.Count == 0)
        {
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();

        // A background flush has no HTTP request, so ICurrentUser finds no principal, the tenant
        // filter falls back to Guid.Empty and the command reports the meeting as not found — every
        // buffered line discarded, with only a log line to say so. The workspace is therefore
        // carried on the buffer from the push, where the hub had already established that a real
        // caller could see this meeting, and staged here so the filter still applies rather than
        // being bypassed.
        scope.ServiceProvider.GetRequiredService<ScopedPrincipal>().Principal =
            ScopedPrincipal.ForOrganization(organizationId);

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        try
        {
            var result = await sender.Send(
                new AppendSegmentsCommand(meetingId, segments),
                cancellationToken);

            if (result.IsFailure)
            {
                // Dropped rather than retried. The common failure is a meeting that has since
                // ended, where re-queuing would spin forever against a transcript that is closed.
                logger.LogWarning(
                    "Discarded {Count} buffered segments for meeting {MeetingId}: {Error}",
                    segments.Count,
                    meetingId,
                    result.Error.Description);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to flush {Count} buffered segments for meeting {MeetingId}",
                segments.Count,
                meetingId);
        }
    }

    private async Task FlushAllAsync(CancellationToken cancellationToken)
    {
        foreach (var meetingId in buffer.PendingMeetings)
        {
            await FlushAsync(meetingId, cancellationToken);
        }
    }
}

/// <summary>
/// Broadcasts live meeting events over SignalR.
/// </summary>
/// <remarks>
/// The Api-layer adapter for <see cref="IMeetingBroadcaster"/>. It lives here because a hub context
/// is an ASP.NET type and Application may not reference ASP.NET — an architecture test fails the
/// build if it does.
/// </remarks>
public sealed class SignalRMeetingBroadcaster(IHubContext<MeetingHub> hub) : IMeetingBroadcaster
{
    public Task SegmentsAppendedAsync(
        Guid meetingId,
        IReadOnlyList<TranscriptSegmentDto> segments,
        CancellationToken cancellationToken = default) =>
        hub.Clients
            .Group(MeetingHub.GroupFor(meetingId))
            .SendAsync("SegmentsAppended", meetingId, segments, cancellationToken);

    public Task MeetingEndedAsync(Guid meetingId, CancellationToken cancellationToken = default) =>
        hub.Clients
            .Group(MeetingHub.GroupFor(meetingId))
            .SendAsync("MeetingEnded", meetingId, cancellationToken);
}

/// <summary>
/// Lets a browser authenticate a websocket, which cannot carry an <c>Authorization</c> header.
/// </summary>
/// <remarks>
/// The WebSocket API gives no way to set request headers, so SignalR sends the token as
/// <c>?access_token=</c> instead. This is scoped to hub paths deliberately: accepting a bearer token
/// from the query string on ordinary endpoints would put credentials into access logs, browser
/// history and <c>Referer</c> headers.
/// </remarks>
public static class HubAuthentication
{
    public const string Path = "/hubs/meetings";

    public static JwtBearerEvents WithHubQueryStringToken(this JwtBearerEvents events)
    {
        var existing = events.OnMessageReceived;

        events.OnMessageReceived = async context =>
        {
            if (existing is not null)
            {
                await existing(context);
            }

            var token = context.Request.Query["access_token"];

            if (!string.IsNullOrEmpty(token)
                && context.HttpContext.Request.Path.StartsWithSegments(Path))
            {
                context.Token = token;
            }
        };

        return events;
    }
}
