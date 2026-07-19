using System.Diagnostics;
using Cadence.Application.Common.Abstractions;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Common.Behaviors;

/// <summary>
/// Outermost behavior: scopes every log line inside a request to that request.
/// </summary>
/// <remarks>
/// Runs first so that anything the inner behaviors log — a validation failure, a cache miss, a slow
/// handler — is already tagged with the request name and the caller. Without the scope those lines
/// are unattributable in production.
/// </remarks>
public sealed class LoggingBehavior<TMessage, TResponse>(
    ILogger<LoggingBehavior<TMessage, TResponse>> logger,
    ICurrentUser currentUser)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TMessage).Name;

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestName"] = requestName,
            ["UserId"] = currentUser.Id,
            ["OrganizationId"] = currentUser.OrganizationId,
        });

        logger.LogInformation("Handling {RequestName}", requestName);

        var timestamp = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(message, cancellationToken);

            logger.LogInformation(
                "Handled {RequestName} in {ElapsedMs} ms",
                requestName,
                Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

            return response;
        }
        catch (Exception exception)
        {
            // Logged here and rethrown: this is the only place that knows the request name, and
            // the API's exception handler is the only place that decides the HTTP response.
            logger.LogError(
                exception,
                "{RequestName} failed after {ElapsedMs} ms",
                requestName,
                Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds);

            throw;
        }
    }
}
