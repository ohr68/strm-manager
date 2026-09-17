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

        string description = string.Join(" ", validationResult.Errors.Select(failure => failure.ErrorMessage));
        return Result.Failure(Error.Validation("Validation.Error", description));
    }
}
