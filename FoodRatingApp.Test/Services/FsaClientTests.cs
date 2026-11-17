using System.Net;
using System.Text;
using FoodRatingApp.Services;
using Microsoft.Extensions.Configuration;

namespace FoodRatingApp.Test.Services;

public class FsaClientTests
{
    /// <summary>Answers each request with the next queued response and records what was asked for.</summary>
    private sealed class FakeHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        private int _next;
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var respond = responses[Math.Min(_next++, responses.Length - 1)];
            return Task.FromResult(respond(request));
        }
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static Func<HttpRequestMessage, HttpResponseMessage> Status(HttpStatusCode status) =>
        _ => new HttpResponseMessage(status) { Content = new StringContent("") };

    private static FsaClient CreateClient(FakeHandler handler, string? baseUrl = null)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false));
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Fsa:BaseUrl"] = baseUrl })
            .Build();
        return new FsaClient(factory, config);
    }

    private static string EstablishmentsPage(int totalPages, params string[] ratings) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            establishments = ratings.Select(r => new { RatingValue = r }),
            meta = new { totalCount = ratings.Length, totalPages },
        });

    [Fact]
    public async Task GetAuthoritiesPage_RequestsOnlyThatPageWithApiVersionHeader()
    {
        var handler = new FakeHandler(Json("""{"authorities":[{"LocalAuthorityId":1,"Name":"A"}]}"""));

        var result = await CreateClient(handler).GetAuthoritiesPage(3, 25);

        result.Authorities.Should().ContainSingle(a => a.Name == "A");
        handler.Requests.Should().ContainSingle("only the one page is requested");
        handler.Requests[0].RequestUri!.ToString().Should().Be("https://api.ratings.food.gov.uk/Authorities/basic/3/25");
        handler.Requests[0].Headers.GetValues("x-api-version").Should().Equal("2");
    }

    [Fact]
    public async Task GetAuthoritiesPage_UsesConfiguredBaseUrl()
    {
        var handler = new FakeHandler(Json("""{"authorities":[]}"""));

        await CreateClient(handler, "http://fake-fsa.test/v2").GetAuthoritiesPage(1, 20);

        handler.Requests[0].RequestUri!.ToString().Should().Be("http://fake-fsa.test/v2/Authorities/basic/1/20");
    }

    [Fact]
    public async Task GetAuthority_NotFound_ReturnsNull()
    {
        var result = await CreateClient(new FakeHandler(Status(HttpStatusCode.NotFound))).GetAuthority(999);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAuthority_ReadsSchemeType()
    {
        var handler = new FakeHandler(Json("""{"LocalAuthorityId":197,"Name":"Aberdeen City","SchemeType":2}"""));

        var result = await CreateClient(handler).GetAuthority(197);

        result!.SchemeType.Should().Be(2);
    }

    [Fact]
    public async Task GetEstablishments_ReadsEveryPage()
    {
        var handler = new FakeHandler(
            Json(EstablishmentsPage(3, "5", "5")),
            Json(EstablishmentsPage(3, "4")),
            Json(EstablishmentsPage(3, "Exempt")));

        var result = await CreateClient(handler).GetEstablishments(374);

        result.Select(e => e.RatingValue).Should().Equal("5", "5", "4", "Exempt");
        handler.Requests.Select(r => r.RequestUri!.Query).Should().Equal(
            "?localAuthorityId=374&pageSize=5000&pageNumber=1",
            "?localAuthorityId=374&pageSize=5000&pageNumber=2",
            "?localAuthorityId=374&pageSize=5000&pageNumber=3");
    }

    [Fact]
    public async Task GetAuthoritiesPage_RateLimitedThenOk_Retries()
    {
        var handler = new FakeHandler(
            _ => new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new(TimeSpan.Zero) } },
            Json("""{"authorities":[]}"""));

        var result = await CreateClient(handler).GetAuthoritiesPage(1, 20);

        result.Authorities.Should().BeEmpty();
        handler.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAuthoritiesPage_ServerError_ThrowsHttpRequestExceptionWithStatus()
    {
        var act = () => CreateClient(new FakeHandler(Status(HttpStatusCode.InternalServerError))).GetAuthoritiesPage(1, 20);

        (await act.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    [Theory]
    [InlineData("<html>Service down</html>")]
    [InlineData("null")]
    public async Task GetAuthoritiesPage_UnreadableBody_ThrowsFsaApiException(string body)
    {
        var act = () => CreateClient(new FakeHandler(Json(body))).GetAuthoritiesPage(1, 20);

        await act.Should().ThrowAsync<FsaApiException>();
    }
}
