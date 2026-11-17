using System.Text.Json;
using FoodRatingApp.Model;
using FoodRatingApp.Services;
using FoodRatingApp.Spec;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Reqnroll;

namespace FoodRatingApp.Spec.StepDefinitions
{
    [Binding]
    [Scope(Tag = "integration")]
    public class FoodRatingsApiStepDefinitions
    {
        private static readonly WebApplicationFactory<Program> Factory =
            new CustomWebApplicationFactory();
        private static readonly HttpClient _client = Factory.CreateClient();

        private readonly ApiScenarioContext _context;

        public FoodRatingsApiStepDefinitions(ApiScenarioContext context)
        {
            _context = context;
        }

        [Given("the API endpoint is {string}")]
        public void GivenTheAPIEndpointIs(string endpoint)
        {
            _context.ApiEndpoint = endpoint;
        }

        [When("the API is called")]
        public async Task WhenTheAPIIsCalled()
        {
            _context.ApiEndpoint.Should().NotBeNullOrEmpty();
            var uri = new Uri(_context.ApiEndpoint!, UriKind.Absolute);
            _context.Response = await _client.GetAsync(uri.PathAndQuery);
        }

        [Then(@"the response HTTP status code should be (\d+) [A-Za-z ]+")]
        public void ThenTheResponseHTTPStatusCodeShouldBe(int expectedStatusCode)
        {
            _context.Response.Should().NotBeNull();
            ((int)_context.Response!.StatusCode).Should().Be(expectedStatusCode);
        }

        [Then("the response should contain {string}")]
        public async Task ThenTheResponseShouldContain(string expectedContent)
        {
            _context.Response.Should().NotBeNull();
            var content = await _context.Response!.Content.ReadAsStringAsync();
            content.Should().Contain(expectedContent);
        }

        [Then("the response should not contain {string}")]
        public async Task ThenTheResponseShouldNotContain(string unexpectedContent)
        {
            _context.Response.Should().NotBeNull();
            var content = await _context.Response!.Content.ReadAsStringAsync();
            content.Should().NotContain(unexpectedContent);
        }

        [Then("the response body should contain JSON field {string}")]
        public async Task ThenTheResponseBodyShouldContainJsonField(string fieldName)
        {
            _context.Response.Should().NotBeNull();
            var content = await _context.Response!.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            // A paged response keeps its records under "items".
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
            {
                root = items;
            }
            var element = root.ValueKind == JsonValueKind.Array ? root[0] : root;
            element.TryGetProperty(fieldName, out _).Should().BeTrue($"Expected JSON field '{fieldName}' was not found in response");
        }

        private sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
        {
            protected override void ConfigureWebHost(IWebHostBuilder builder)
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IFsaClient>();
                    services.AddSingleton<IFsaClient, StubFsaClient>();
                });
            }
        }

        private sealed class StubFsaClient : IFsaClient
        {
            private static readonly List<FsaAuthority> Authorities =
            [
                new FsaAuthority { LocalAuthorityId = 1, Name = "Authority 1" },
                new FsaAuthority { LocalAuthorityId = 2, Name = "Authority 2" },
            ];

            // Like the real FSA API, returns only the requested page.
            public Task<FsaAuthorityList> GetAuthoritiesPage(int pageNumber, int pageSize) =>
                Task.FromResult(new FsaAuthorityList
                {
                    Authorities = Authorities.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                });

            // Authority 1 is English (FHRS), 2 is Scottish (FHIS).
            // Authorities 502, 503 and 504 exist but the FSA API fails when their establishments are fetched.
            // Any other id is unknown.
            public Task<FsaAuthority?> GetAuthority(int authorityId) =>
                Task.FromResult<FsaAuthority?>(authorityId switch
                {
                    1 => new FsaAuthority { LocalAuthorityId = 1, Name = "Authority 1", SchemeType = (int)RatingScheme.Fhrs },
                    2 => new FsaAuthority { LocalAuthorityId = 2, Name = "Authority 2", SchemeType = (int)RatingScheme.Fhis },
                    502 or 503 or 504 => new FsaAuthority { LocalAuthorityId = authorityId, Name = "Failing authority" },
                    _ => null,
                });

            public Task<IReadOnlyList<FsaEstablishment>> GetEstablishments(int authorityId) => authorityId switch
            {
                502 => Task.FromException<IReadOnlyList<FsaEstablishment>>(new FsaApiException("FSA returned invalid JSON")),
                503 => Task.FromException<IReadOnlyList<FsaEstablishment>>(new HttpRequestException("No such host is known.")),
                504 => Task.FromException<IReadOnlyList<FsaEstablishment>>(new TaskCanceledException("Timed out", new TimeoutException())),
                _ => Establishments(authorityId),
            };

            private static Task<IReadOnlyList<FsaEstablishment>> Establishments(int authorityId) =>
                Task.FromResult<IReadOnlyList<FsaEstablishment>>(authorityId switch
                {
                    // 5: 50%, 1: 20%, Exempt: 30%. "AwaitingInspection" is not a rating, so it is ignored.
                    1 => Establishments(("5", 5), ("1", 2), ("Exempt", 3), ("AwaitingInspection", 4)),
                    // Pass and Eat Safe: 50%, Pass: 15%, Improvement Required: 35%.
                    2 => Establishments(("Pass and Eat Safe", 10), ("Pass", 3), ("Improvement Required", 7), ("Awaiting Inspection", 2)),
                    _ => [],
                });

            private static List<FsaEstablishment> Establishments(params (string Rating, int Count)[] ratings) =>
                ratings
                    .SelectMany(r => Enumerable.Range(0, r.Count).Select(_ => new FsaEstablishment { RatingValue = r.Rating }))
                    .ToList();
        }
    }
}
