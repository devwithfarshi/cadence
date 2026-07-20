using Cadence.Application.Common.Models;
using Microsoft.AspNetCore.Mvc;

namespace Cadence.Api.Common;

/// <summary>
/// Turns a failed <see cref="Result"/> into the same <c>problem+json</c> shape the exception
/// handler produces.
/// </summary>
/// <remarks>
/// Handlers return <c>Result</c> for expected failures rather than throwing, so those never reach
/// <see cref="GlobalExceptionHandler"/>. Without this the API would have two error shapes — one for
/// thrown failures and one for returned ones — and the client would need two branches to read them.
/// </remarks>
public static class ResultExtensions
{
    private const string TypeBase = "https://cadence.app/errors/";

    /// <summary>
    /// Maps an <see cref="ErrorType"/> to its status. The single place HTTP meets a domain failure;
    /// handlers classify, this translates.
    /// </summary>
    public static IResult ToProblem(this Result result, HttpContext context)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("A successful result has no problem to report.");
        }

        var (status, slug, title) = result.Error.Type switch
        {
            ErrorType.Validation => (StatusCodes.Status400BadRequest, "validation", "The request is invalid."),
            ErrorType.Unauthorized => (StatusCodes.Status401Unauthorized, "unauthorized", "Authentication is required."),
            ErrorType.Forbidden => (StatusCodes.Status403Forbidden, "forbidden", "You do not have permission to perform this action."),
            ErrorType.NotFound => (StatusCodes.Status404NotFound, "not-found", "The requested resource was not found."),
            ErrorType.Conflict => (StatusCodes.Status409Conflict, "conflict", "The request conflicts with the current state of the resource."),
            _ => (StatusCodes.Status500InternalServerError, "internal", "An unexpected error occurred."),
        };

        var problem = new ProblemDetails
        {
            Status = status,
            Type = $"{TypeBase}{slug}",
            Title = title,
            Detail = result.Error.Description,
            Instance = context.Request.Path,
        };

        // The machine-readable code the client switches on. `title` and `detail` are for humans and
        // may be reworded; this may not.
        problem.Extensions["code"] = result.Error.Code;
        problem.Extensions["correlationId"] = context.TraceIdentifier;

        return TypedResults.Problem(problem);
    }
}
