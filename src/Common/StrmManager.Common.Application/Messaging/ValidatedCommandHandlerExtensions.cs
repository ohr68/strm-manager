using FluentValidation;
using FluentValidation.Results;
using StrmManager.Common.Application.Validation;
using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Common.Application.Messaging;

/// <summary>
/// Collapses the validate-then-handle sequence every command endpoint would otherwise
/// repeat by hand into one call, without a MediatR-style pipeline: endpoints still
/// resolve both the validator and the handler explicitly via DI, this just removes the
/// boilerplate "if invalid, map and return" branch.
/// </summary>
public static class ValidatedCommandHandlerExtensions
{
    public static async Task<Result<TResponse>> HandleValidated<TCommand, TResponse>(
        this ICommandHandler<TCommand, TResponse> handler,
        TCommand command,
        IValidator<TCommand> validator,
        CancellationToken cancellationToken)
        where TCommand : ICommand<TResponse>
    {
        ValidationResult validationResult = await validator.ValidateAsync(command, cancellationToken);

        if (!validationResult.IsValid)
        {
            return validationResult.ToApplicationResult<TResponse>();
        }

        return await handler.Handle(command, cancellationToken);
    }
}
