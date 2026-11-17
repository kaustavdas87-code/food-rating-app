using System.Net;
using System.Text.Json;
using FoodRatingApp.Middleware;
using FoodRatingApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FoodRatingApp.Test.Middleware;

public class GlobalExceptionMiddlewareTests
{
    /// <summary>Named cases so test results read clearly (exception text would otherwise end up in the test name).</summary>
    private static readonly Dictionary<string, Func<Exception>> Exceptions = new()
    {
        ["FSA unreachable"] = () => new HttpRequestException("connection refused"),
        ["FSA rate limited (429)"] = () => new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests),
        ["FSA server error (500)"] = () => new HttpRequestException("upstream error", null, HttpStatusCode.InternalServerError),
        ["FSA forbidden (403)"] = () => new HttpRequestException("upstream forbidden", null, HttpStatusCode.Forbidden),
        ["FSA invalid response"] = () => new FsaApiException("bad json"),
        ["HttpClient timeout"] = () => new TaskCanceledException("timeout", new TimeoutException()),
        ["Timeout"] = () => new TimeoutException(),
        ["Unexpected InvalidOperationException"] = () => new InvalidOperationException("bug"),
        ["Unexpected NullReferenceException"] = () => new NullReferenceException(),
    };

    public static TheoryData<string, int> ExceptionMappings => new()
    {
        { "FSA unreachable", 503 },
        { "FSA rate limited (429)", 503 },
        { "FSA server error (500)", 502 },
        { "FSA forbidden (403)", 502 },
        { "FSA invalid response", 502 },
        { "HttpClient timeout", 504 },
        { "Timeout", 504 },
        { "Unexpected InvalidOperationException", 500 },
        { "Unexpected NullReferenceException", 500 },
    };

    private static GlobalExceptionMiddleware Throwing(Exception exception) =>
        new(_ => throw exception, NullLogger<GlobalExceptionMiddleware>.Instance);

    [Theory]
    [MemberData(nameof(ExceptionMappings))]
    public async Task InvokeAsync_MapsExceptionToStatusCode(string failure, int expectedStatus)
    {
        var context = NewContext();

        await Throwing(Exceptions[failure]()).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(expectedStatus);
        ReadBody(context).GetProperty("status").GetInt32().Should().Be(expectedStatus);
    }

    [Fact]
    public async Task InvokeAsync_NoException_PassesThroughUntouched()
    {
        var context = NewContext();
        var middleware = new GlobalExceptionMiddleware(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; },
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(200);
        context.Response.Body.Length.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_FsaUnreachable_Writes503ProblemDetails()
    {
        var context = NewContext();

        await Throwing(new HttpRequestException("No such host is known.")).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(503);
        context.Response.ContentType.Should().StartWith("application/problem+json");
        var body = ReadBody(context);
        body.GetProperty("title").GetString().Should().Be("Food ratings service unavailable");
        body.GetProperty("instance").GetString().Should().Be("/api/197");
        body.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UnexpectedException_DoesNotLeakDetails()
    {
        var context = NewContext();

        await Throwing(new InvalidOperationException("secret connection string")).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
        var raw = ReadRawBody(context);
        raw.Should().NotContain("secret connection string");
        raw.Should().NotContain("InvalidOperationException");
    }

    [Fact]
    public async Task InvokeAsync_RateLimited_SetsRetryAfter()
    {
        var context = NewContext();

        await Throwing(new HttpRequestException("429", null, HttpStatusCode.TooManyRequests)).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(503);
        context.Response.Headers.RetryAfter.ToString().Should().Be("30");
    }

    [Fact]
    public async Task InvokeAsync_ClientDisconnected_Returns499WithoutBody()
    {
        using var aborted = new CancellationTokenSource();
        aborted.Cancel();
        var context = NewContext();
        context.RequestAborted = aborted.Token;

        await Throwing(new TaskCanceledException()).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(GlobalExceptionMiddleware.ClientClosedRequest);
        context.Response.Body.Length.Should().Be(0);
    }

    private static DefaultHttpContext NewContext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/197";
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadRawBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    private static JsonElement ReadBody(HttpContext context) => JsonDocument.Parse(ReadRawBody(context)).RootElement;
}
