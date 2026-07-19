using FluentValidation;
using Mediator;
using ValidationException = Cadence.Application.Common.Exceptions.ValidationException;

namespace Cadence.Application.Common.Behaviors;

/// <summary>
/// Runs every registered validator for a request before its handler.
/// </summary>
/// <remarks>
/// Sits ahead of caching and the transaction, so a malformed request never reaches Redis, the
/// database or a handler. Validators are found by convention from the assembly, which means adding
/// one is enough — nothing has to be wired up (§10.1).
/// </remarks>
public sealed class ValidationBehavior<TMessage, TResponse>(IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message,
        MessageHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!validators.Any())
        {
            return await next(message, cancellationToken);
        }

        // One ValidationContext per validator. A shared context accumulates failures across
        // validators, so every result would then report the same combined (duplicated) list.
        var results = await Task.WhenAll(
            validators.Select(validator =>
                validator.ValidateAsync(new ValidationContext<TMessage>(message), cancellationToken)));

        var failures = results.SelectMany(result => result.Errors).Where(failure => failure is not null).ToArray();

        return failures.Length != 0
            ? throw new ValidationException(failures)
            : await next(message, cancellationToken);
    }
}
