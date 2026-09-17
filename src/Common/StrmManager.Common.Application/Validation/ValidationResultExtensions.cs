using FluentValidation.Results;
using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Common.Application.Validation;

public static class ValidationResultExtensions
{
    public static Result ToApplicationResult(this ValidationResult validationResult)
    {
        if (validationResult.IsValid)
        {
            return Result.Success();
        }

        return Result.Failure(BuildValidationError(validationResult));
    }

    public static Result<TValue> ToApplicationResult<TValue>(this ValidationResult validationResult)
    {
        if (validationResult.IsValid)
        {
            throw new InvalidOperationException("A valid FluentValidation result has no error to convert.");
        }

        return Result.Failure<TValue>(BuildValidationError(validationResult));
    }

    private static Error BuildValidationError(ValidationResult validationResult)
    {
        string description = string.Join(" ", validationResult.Errors.Select(failure => failure.ErrorMessage));
        return Error.Validation("Validation.Error", description);
    }
}
