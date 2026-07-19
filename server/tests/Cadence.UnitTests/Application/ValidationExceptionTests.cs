using Cadence.Application.Common.Exceptions;
using FluentValidation.Results;
using Shouldly;

namespace Cadence.UnitTests.Application;

public class ValidationExceptionTests
{
    [Fact]
    public void FailuresAreGroupedByField_ReadyForProblemJson()
    {
        var exception = new ValidationException(
        [
            new ValidationFailure("Title", "Title is required."),
            new ValidationFailure("Title", "Title must be under 200 characters."),
            new ValidationFailure("StartsAt", "Start time is required."),
        ]);

        exception.Errors.Count.ShouldBe(2);
        exception.Errors["Title"].Length.ShouldBe(2);
        exception.Errors["StartsAt"].ShouldBe(["Start time is required."]);
    }

    [Fact]
    public void TheSameMessageTwiceOnAField_IsReportedOnce()
    {
        // Two validators can independently produce the same message; the user should see it once.
        var exception = new ValidationException(
        [
            new ValidationFailure("Title", "Title is required."),
            new ValidationFailure("Title", "Title is required."),
        ]);

        exception.Errors["Title"].ShouldBe(["Title is required."]);
    }

    [Fact]
    public void NoFailures_YieldsAnEmptyErrorMap_NotNull()
    {
        new ValidationException().Errors.ShouldBeEmpty();
    }
}
