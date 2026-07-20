using Serilog.Context;

namespace Cadence.Api.Common;

/// <summary>
/// Gives every request an id that appears on all of its log lines, its response, and any error.
/// </summary>
/// <remarks>
/// A client-supplied <c>X-Correlation-Id</c> is honoured so a trace can span the browser and the
/// API; otherwise one is generated. The id is pushed into Serilog's <c>LogContext</c>, which is what
/// makes "here's the id from the error toast" enough to find every line of a failed request — the
/// difference between a tractable support request and guesswork (§10.4).
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = Read(context) ?? Guid.CreateVersion7().ToString("n");

        context.TraceIdentifier = correlationId;

        // Set before the response starts, since headers cannot be added once it has.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    /// <summary>
    /// Reads the inbound header, rejecting anything unreasonable.
    /// </summary>
    /// <remarks>
    /// The value reaches logs and a response header, so it is bounded and stripped of control
    /// characters — an unbounded client-controlled string in a log line is a log-injection vector.
    /// </remarks>
    private static string? Read(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue(HeaderName, out var values))
        {
            return null;
        }

        var candidate = values.ToString();

        return candidate.Length is > 0 and <= MaxLength && candidate.All(IsSafe)
            ? candidate
            : null;
    }

    private static bool IsSafe(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
