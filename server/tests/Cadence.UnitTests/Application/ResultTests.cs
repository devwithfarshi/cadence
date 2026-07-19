using Cadence.Application.Common.Models;
using Shouldly;

namespace Cadence.UnitTests.Application;

public class ResultTests
{
    [Fact]
    public void ASuccess_CarriesNoError()
    {
        // The constructor refuses the contradictory combinations outright; the public factories
        // are what guarantee callers never build one.
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void AFailure_ExposesItsError()
    {
        var result = Result.Failure(Error.NotFound("meeting.not_found", "No such meeting."));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("meeting.not_found");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void ReadingTheValueOfAFailure_Throws_RatherThanReturningNull()
    {
        var result = Result.Failure<string>(Error.NotFound("meeting.not_found", "No such meeting."));

        Should.Throw<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void AValueConvertsImplicitlyToASuccess()
    {
        Result<string> result = "ok";

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe("ok");
    }

    [Fact]
    public void ANullValueConvertsToAFailure_NotASuccessWrappingNull()
    {
        // Otherwise `return repository.Find(id);` would report "not found" as a success.
        Result<string> result = (string?)null;

        result.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Map_TransformsASuccess()
    {
        var result = Result.Success(21).Map(value => value * 2);

        result.Value.ShouldBe(42);
    }

    [Fact]
    public void Map_PropagatesAFailureUntouched()
    {
        var error = Error.Forbidden("meeting.forbidden", "Not your workspace.");

        var result = Result.Failure<int>(error).Map(value => value * 2);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(error);
    }

    [Theory]
    [InlineData(ErrorType.Validation)]
    [InlineData(ErrorType.Unauthorized)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.NotFound)]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Failure)]
    public void EveryErrorType_HasAFactoryThatSetsIt(ErrorType type)
    {
        var error = type switch
        {
            ErrorType.Validation => Error.Validation("c", "d"),
            ErrorType.Unauthorized => Error.Unauthorized("c", "d"),
            ErrorType.Forbidden => Error.Forbidden("c", "d"),
            ErrorType.NotFound => Error.NotFound("c", "d"),
            ErrorType.Conflict => Error.Conflict("c", "d"),
            _ => Error.Failure("c", "d"),
        };

        error.Type.ShouldBe(type);
    }
}
