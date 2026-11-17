using System.Diagnostics;
using System.Net;
using FoodRatingApp.Services;
using Microsoft.AspNetCore.Mvc;

namespace FoodRatingApp.Middleware;

/// <summary>
/// Catches any unhandled exception from the rest of the pipeline and returns a ProblemDetails
/// response with a meaningful status code. Failures of the upstream FSA API become 502/503/504
/// instead of a generic 500, and no exception details or stack traces are sent to the client.
/// </summary>
public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    /// <summary>Status code nginx popularised for "client closed request".</summary>
    public const int ClientClosedRequest = 499;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        // The browser went away: nothing to send and nothing went wrong on our side.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {Path} was cancelled by the client.", context.Request.Path);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ClientClosedRequest;
            }
            return;
        }

        var (status, title, detail) = Map(exception);

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Path}", context.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "FSA API failure on {Path}: {Title}", context.Request.Path, title);
        }

        // Too late to change the status code once the body has started; let the server abort the response.
        if (context.Response.HasStarted)
        {
            throw exception;
        }

        context.Response.Clear();
        context.Response.StatusCode = status;
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests })
        {
            context.Response.Headers.RetryAfter = "30";
        }

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path,
        };
        problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");
    }

    /// <summary>Maps an exception to the status code, title and client-safe detail to return.</summary>
    public static (int Status, string Title, string Detail) Map(Exception exception) => exception switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => (
            StatusCodes.Status503ServiceUnavailable,
            "Food ratings service is busy",
            "The Food Standards Agency API is rate limiting requests. Please try again shortly."),

        HttpRequestException { StatusCode: null } => (
            StatusCodes.Status503ServiceUnavailable,
            "Food ratings service unavailable",
            "The Food Standards Agency API could not be reached. Please try again later."),

        HttpRequestException http => (
            StatusCodes.Status502BadGateway,
            "Food ratings service error",
            $"The Food Standards Agency API returned an error ({(int)http.StatusCode!}). Please try again later."),

        FsaApiException => (
            StatusCodes.Status502BadGateway,
            "Invalid response from food ratings service",
            "The Food Standards Agency API returned a response that could not be read."),

        TimeoutException or OperationCanceledException => (
            StatusCodes.Status504GatewayTimeout,
            "Food ratings service timed out",
            "The Food Standards Agency API took too long to respond. Please try again."),

        _ => (
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred",
            "Something went wrong while processing your request."),
    };
}
