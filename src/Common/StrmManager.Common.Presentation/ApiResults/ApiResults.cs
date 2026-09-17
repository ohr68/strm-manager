using Microsoft.AspNetCore.Http;
using StrmManager.Common.Domain.Abstractions;

namespace StrmManager.Common.Presentation.ApiResults;

public static class ApiResults
{
    public static IResult Problem(Result result)
    {
        if (result.IsSuccess)
        {
            throw new InvalidOperationException("A successful result cannot be converted to a problem response.");
        }

        int statusCode = result.Error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        return Microsoft.AspNetCore.Http.Results.Problem(
            statusCode: statusCode,
            title: result.Error.Code,
            detail: result.Error.Description,
            extensions: new Dictionary<string, object?> { { "errorType", result.Error.Type.ToString() } });
    }
}
