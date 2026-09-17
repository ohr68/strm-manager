using Microsoft.AspNetCore.Http;
using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Common.Presentation.ApiResults;

public static class ResultExtensions
{
    public static IResult Match(this Result result, Func<IResult> onSuccess, Func<Result, IResult> onFailure) =>
        result.IsSuccess ? onSuccess() : onFailure(result);

    public static IResult Match<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess, Func<Result, IResult> onFailure) =>
        result.IsSuccess ? onSuccess(result.Value) : onFailure(result);
}
