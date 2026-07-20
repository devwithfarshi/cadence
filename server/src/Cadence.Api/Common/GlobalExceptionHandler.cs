using Cadence.Application.Common.Exceptions;
using Cadence.Domain.Common;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Common;

/// <summary>
/// Turns any unhandled exception into one RFC 9457 <c>problem+json</c> response.
/// </summary>
/// <remarks>
/// One shape for every failure means the client needs exactly one error branch — and the client
/// already renders <c>problem+json</c> into form errors and toasts (§10.2).
/// </remarks>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    private const string TypeBase = "https://cadence.app/errors/";

    public async ValueTask<bool> TryHandleAsync(
        HttpContext context,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = Map(exception, context);

        // Expected failures are noise at Error level; a 500 is a defect and should page someone.
        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                exception,
                "Unhandled exception on {Method} {Path} ({CorrelationId})",
                context.Request.Method,
                context.Request.Path,
                context.TraceIdentifier);
        }
        else
        {
            logger.LogInformation(
                "{ExceptionType} on {Method} {Path}: {Message}",
                exception.GetType().Name,
                context.Request.Method,
                context.Request.Path,
                exception.Message);
        }

        context.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            Exception = exception,
            ProblemDetails = problem,
        });
    }

    private static ProblemDetails Map(Exception exception, HttpContext context)
    {
        var problem = exception switch
        {
            ValidationException validation => Problem(
                StatusCodes.Status400BadRequest,
                "validation",
                "One or more validation errors occurred.",
                "See 'errors' for field-level messages.",
                validation.Errors),

            UnauthorizedAccessException => Problem(
                StatusCodes.Status401Unauthorized,
                "unauthorized",
                "Authentication is required.",
                exception.Message),

            ForbiddenException => Problem(
                StatusCodes.Status403Forbidden,
                "forbidden",
                "You do not have permission to perform this action.",
                exception.Message),

            NotFoundException => Problem(
                StatusCodes.Status404NotFound,
                "not-found",
                "The requested resource was not found.",
                exception.Message),

            ConflictException => Problem(
                StatusCodes.Status409Conflict,
                "conflict",
                "The request conflicts with the current state of the resource.",
                exception.Message),

            // A broken business rule is well-formed input that is not allowed — 422, not 400. The
            // message is written for a user to read, so it is safe to return.
            DomainException => Problem(
                StatusCodes.Status422UnprocessableEntity,
                "domain-rule",
                "The request could not be completed.",
                exception.Message),

            OperationCanceledException => Problem(
                StatusCodes.Status499ClientClosedRequest,
                "cancelled",
                "The request was cancelled.",
                "The client closed the connection before the request completed."),

            // A body that does not deserialise is the caller's mistake, not ours. Minimal APIs raise
            // this before any handler runs, so without a case here every malformed payload — most
            // commonly an enum value the API does not define — is reported as a server defect.
            // The message is the framework's own and names the offending path, with no internal
            // detail in it.
            BadHttpRequestException badRequest => Problem(
                StatusCodes.Status400BadRequest,
                "malformed-request",
                "The request could not be read.",
                badRequest.InnerException?.Message ?? badRequest.Message),

            // Everything else is a defect. The message is deliberately generic: a stack trace or
            // SQL fragment in a response is reconnaissance handed to an attacker (§10.3). The
            // correlation id below is what ties this back to the full detail in the log.
            _ => Problem(
                StatusCodes.Status500InternalServerError,
                "internal",
                "An unexpected error occurred.",
                "The error has been logged. Quote the correlation id when reporting it."),
        };

        problem.Instance = context.Request.Path;
        problem.Extensions["correlationId"] = context.TraceIdentifier;

        return problem;
    }

    private static ProblemDetails Problem(
        int status,
        string slug,
        string title,
        string detail,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Type = $"{TypeBase}{slug}",
            Title = title,
            Detail = detail,
        };

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return problem;
    }
}
