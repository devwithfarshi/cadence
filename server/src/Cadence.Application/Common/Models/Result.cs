using System.Diagnostics.CodeAnalysis;

namespace Cadence.Application.Common.Models;

/// <summary>
/// The outcome of an operation: either success, or an <see cref="Models.Error"/> explaining why not.
/// </summary>
/// <remarks>
/// <para>
/// Expected failures — "that meeting does not exist", "you are the last owner" — are returned, not
/// thrown. Exceptions stay for the genuinely exceptional, which means a stack trace in the log
/// actually signals a defect instead of a user typing a bad id (§10.3).
/// </para>
/// <para>
/// Construction is guarded: a success carrying an error, or a failure carrying none, is a
/// contradiction and throws immediately rather than being discovered three layers up.
/// </para>
/// </remarks>
public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// A <see cref="Result"/> that carries a value when it succeeds.
/// </summary>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>The value produced. Throws if the result is a failure — check first.</summary>
    [NotNull]
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    /// <summary>
    /// Lets a handler write <c>return meeting;</c> instead of <c>return Result.Success(meeting);</c>.
    /// A null value converts to a failure rather than a success wrapping null, so the "not found"
    /// case cannot be smuggled through as a success.
    /// </summary>
    public static implicit operator Result<TValue>(TValue? value) =>
        value is not null
            ? Success(value)
            : Failure<TValue>(Error.Failure("result.null_value", "The operation produced no value."));

    /// <summary>Maps the value when successful; propagates the error untouched when not.</summary>
    public Result<TOut> Map<TOut>(Func<TValue, TOut> map) =>
        IsSuccess ? Success(map(Value)) : Failure<TOut>(Error);
}
