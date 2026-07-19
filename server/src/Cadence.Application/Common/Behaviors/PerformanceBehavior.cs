using System.Diagnostics;
using Cadence.Application.Common.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Common.Behaviors;

/// <summary>
/// Warns when a request takes longer than it should.
/// </summary>
/// <remarks>
/// Innermost behavior, so the measurement covers the handler and its database work rather than the
/// pipeline's own overhead. A warning, not a failure — the point is that a regression shows up in
/// the logs before a user reports it (§16.5).
/// </remarks>
public sealed class PerformanceBehavior<TMessage, TResponse>(
    ILogger<PerformanceBehavior<TMessage, TResponse>> logger,
    ICurrentUser currentUser)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    private const int WarnThresholdMs = 500;

    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var timestamp = Stopwatch.GetTimestamp();

        var response = await next(message, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(timestamp);
        if (elapsed.TotalMilliseconds > WarnThresholdMs)
        {
            logger.LogWarning(
                "Slow request: {RequestName} took {ElapsedMs} ms (user {UserId}, org {OrganizationId})",
                typeof(TMessage).Name,
                (long)elapsed.TotalMilliseconds,
                currentUser.Id,
                currentUser.OrganizationId);
        }

        return response;
    }
}
