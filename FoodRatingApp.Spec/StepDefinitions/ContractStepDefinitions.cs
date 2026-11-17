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
    [Scope(Tag = "contract")]
    public class ContractStepDefinitions
    {
        private static readonly WebApplicationFactory<Program> Factory =
            new ContractWebApplicationFactory();
        private static readonly HttpClient _client = Factory.CreateClient();
        private static List<FsaAuthority> _stubbedAuthorities = [];

        private readonly ApiScenarioContext _context;

        public ContractStepDefinitions(ApiScenarioContext context)
        {
            _context = context;
        }

        [Given("the following authorities exist:")]
        public void GivenTheFollowingAuthoritiesExist(Table table)
        {
            _stubbedAuthorities = table
                .Rows.Select(row => new FsaAuthority
                {
                    LocalAuthorityId = int.Parse(row["id"]),
                    Name = row["name"],
                })
                .ToList();
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

        [Then(@"the response should contain at least (\d+) authorit(?:y|ies)")]
        public async Task ThenTheResponseShouldContainAtLeastAuthorities(int minimumCount)
        {
            var authorities = await ReadSuccessfulItems();
            authorities.Count.Should().BeGreaterThanOrEqualTo(minimumCount);
        }

        [Then("each authority has a valid identifier")]
        public async Task ThenEachAuthorityHasAValidNumericIdentifier()
        {
            foreach (var authority in await ReadSuccessfulItems())
            {
                authority.TryGetProperty("id", out var id).Should().BeTrue("every authority needs an id");
                id.ValueKind.Should().Be(JsonValueKind.Number);
                id.GetInt32().Should().BePositive();
            }
        }

        [Then("each authority has a displayable name")]
        public async Task ThenEachAuthorityHasADisplayableName()
        {
            foreach (var authority in await ReadSuccessfulItems())
            {
                authority.TryGetProperty("name", out var name).Should().BeTrue("every authority needs a name");
                name.GetString().Should().NotBeNullOrWhiteSpace();
            }
        }

        [Then("the response content type should be {string}")]
        public void ThenTheResponseContentTypeShouldBe(string expectedContentType)
        {
            _context.Response.Should().NotBeNull();
            _context.Response!.Content.Headers.ContentType?.MediaType.Should().Be(expectedContentType);
        }

        [Then("the response should contain {int} rating items")]
        public async Task ThenTheResponseShouldContainRatingItems(int expectedCount)
        {
            using var doc = await ReadSuccessfulJsonArray();
            doc.RootElement.GetArrayLength().Should().Be(expectedCount);
        }

        [Then("the sum of all rating values should equal {double}")]
        public async Task ThenTheSumOfAllRatingValuesShouldEqual(double expectedSum)
        {
            using var doc = await ReadSuccessfulJsonArray();
            var sum = doc.RootElement.EnumerateArray().Sum(item => item.GetProperty("value").GetDouble());
            // Each value is rounded to 2 decimal places, so allow a little rounding drift.
            sum.Should().BeApproximately(expectedSum, 0.1);
        }

        [Then("the ratings should include the following categories:")]
        public async Task ThenTheRatingsShouldIncludeTheFollowingCategories(Table table)
        {
            using var doc = await ReadSuccessfulJsonArray();
            var actual = doc.RootElement.EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToList();
            var expected = table.Rows.Select(row => row["category"]).ToList();

            // Order matters: the UI shows ratings in the order the API returns them.
            actual.Should().Equal(expected);
        }

        private async Task<JsonDocument> ReadSuccessfulJsonArray()
        {
            _context.Response.Should().NotBeNull();
            _context.Response!.IsSuccessStatusCode.Should().BeTrue($"the API returned {(int)_context.Response.StatusCode}");
            var doc = JsonDocument.Parse(await _context.Response.Content.ReadAsStringAsync());
            doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            return doc;
        }

        /// <summary>The records in the response: the array itself, or "items" of a paged response.</summary>
        private async Task<List<JsonElement>> ReadSuccessfulItems()
        {
            _context.Response.Should().NotBeNull();
            _context.Response!.IsSuccessStatusCode.Should().BeTrue($"the API returned {(int)_context.Response.StatusCode}");
            using var doc = JsonDocument.Parse(await _context.Response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("items", out var items))
            {
                root = items;
            }
            root.ValueKind.Should().Be(JsonValueKind.Array);
            return root.EnumerateArray().Select(e => e.Clone()).ToList();
        }

        private sealed class ContractWebApplicationFactory : WebApplicationFactory<Program>
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

        /// <summary>
        /// Authority 2 is Scottish (FHIS); every other id is English (FHRS).
        /// </summary>
        private sealed class StubFsaClient : IFsaClient
        {
            // Like the real FSA API, returns only the requested page.
            public Task<FsaAuthorityList> GetAuthoritiesPage(int pageNumber, int pageSize) =>
                Task.FromResult(new FsaAuthorityList
                {
                    Authorities = _stubbedAuthorities.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList(),
                });

            public Task<FsaAuthority?> GetAuthority(int authorityId) =>
                Task.FromResult<FsaAuthority?>(new FsaAuthority
                {
                    LocalAuthorityId = authorityId,
                    Name = $"Authority {authorityId}",
                    SchemeType = authorityId == 2 ? (int)RatingScheme.Fhis : (int)RatingScheme.Fhrs,
                });

            public Task<IReadOnlyList<FsaEstablishment>> GetEstablishments(int authorityId)
            {
                string[] ratings = authorityId == 2
                    ? ["Pass", "Improvement Required", "Pass and Eat Safe", "Pass", "Awaiting Inspection"]
                    : ["3", "5", "Exempt", "1", "5", "4", "0", "2", "AwaitingInspection"];

                return Task.FromResult<IReadOnlyList<FsaEstablishment>>(
                    ratings.Select(r => new FsaEstablishment { RatingValue = r }).ToList());
            }
        }
    }
}
