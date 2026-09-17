using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace StrmManager.Api.Middleware;

/// <summary>
/// Last-resort handler for exceptions that escape the Result/Error pattern - i.e.
/// genuine technical failures (a bug, a database/filesystem error), never expected
/// domain/application outcomes, which are represented as a failed Result and mapped to
/// HTTP by StrmManager.Common.Presentation.ApiResults instead of being thrown.
/// </summary>
internal sealed partial class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment environment) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        LogUnhandledException(logger, exception, httpContext.Request.Method, httpContext.Request.Path);

        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
        };

        if (environment.IsDevelopment())
        {
            problemDetails.Extensions["exceptionType"] = exception.GetType().FullName;
            problemDetails.Extensions["exceptionMessage"] = exception.Message;
        }

        httpContext.Response.StatusCode = problemDetails.Status.Value;

        // WriteAsJsonAsync always overwrites Content-Type unless told otherwise - setting
        // it beforehand is not enough, it has to go through the contentType parameter.
        await httpContext.Response.WriteAsJsonAsync(
            problemDetails,
            options: null,
            contentType: "application/problem+json",
            cancellationToken: cancellationToken);

        return true;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception processing {Method} {Path}")]
    private static partial void LogUnhandledException(ILogger logger, Exception exception, string method, PathString path);
}
