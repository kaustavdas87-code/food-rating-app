using System.Net;
using System.Text.Json;
using FoodRatingApp.Model;

namespace FoodRatingApp.Services;

public class FsaClient : IFsaClient
{
    public const string DefaultBaseUrl = "https://api.ratings.food.gov.uk/";

    /// <summary>The FSA API caps a page at 5000 establishments.</summary>
    internal const int EstablishmentPageSize = 5000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _baseUrl;

    public FsaClient(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        var baseUrl = configuration["Fsa:BaseUrl"];
        _baseUrl = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.TrimEnd('/') + "/";
    }

    public async Task<FsaAuthorityList> GetAuthoritiesPage(int pageNumber, int pageSize)
    {
        // The "basic" endpoint pages on the FSA side and returns authorities sorted by name.
        // (The plain /Authorities endpoint ignores paging and always returns every authority.)
        return await GetFsaResource<FsaAuthorityList>($"Authorities/basic/{pageNumber}/{pageSize}");
    }

    public async Task<FsaAuthority?> GetAuthority(int authorityId)
    {
        string urlPart = "Authorities";
        using var response = await SendWithRetry($"{_baseUrl}{urlPart}/{authorityId}");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        return await Deserialize<FsaAuthority>(response);
    }

    public async Task<IReadOnlyList<FsaEstablishment>> GetEstablishments(int authorityId)
    {
        // FSA returns establishments sorted by rating, so every page must be read
        // or the percentages would be skewed towards the first page's ratings.
        var firstPage = await GetEstablishmentPage(authorityId, 1);
        var establishments = new List<FsaEstablishment>(firstPage.Meta.TotalCount);
        establishments.AddRange(firstPage.Establishments);

        // Pages are fetched one at a time: the FSA API rate-limits (HTTP 429) parallel requests.
        for (var pageNumber = 2; pageNumber <= firstPage.Meta.TotalPages; pageNumber++)
        {
            var page = await GetEstablishmentPage(authorityId, pageNumber);
            establishments.AddRange(page.Establishments);
        }

        return establishments;
    }

    private Task<FsaEstablishmentList> GetEstablishmentPage(int authorityId, int pageNumber) =>
        GetFsaResource<FsaEstablishmentList>(
            $"Establishments?localAuthorityId={authorityId}&pageSize={EstablishmentPageSize}&pageNumber={pageNumber}");

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Add("x-api-version", "2");
        return client;
    }

    private async Task<T> GetFsaResource<T>(string path)
    {
        using var response = await SendWithRetry($"{_baseUrl}{path}");
        response.EnsureSuccessStatusCode();
        return await Deserialize<T>(response);
    }

    /// <summary>Reads JSON from an FSA response, throwing <see cref="FsaApiException"/> if it is unusable.</summary>
    private static async Task<T> Deserialize<T>(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<T>(stream)
                ?? throw new FsaApiException($"The FSA API returned an empty {typeof(T).Name}.");
        }
        catch (JsonException ex)
        {
            throw new FsaApiException($"The FSA API returned invalid JSON for {typeof(T).Name}.", ex);
        }
    }

    /// <summary>
    /// Sends a GET, retrying when the FSA API rate-limits us (HTTP 429).
    /// Waits for the server's Retry-After when given, otherwise backs off 1s, 2s, 4s.
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetry(string url)
    {
        const int maxRetries = 3;
        var client = CreateClient();

        for (var attempt = 0; ; attempt++)
        {
            var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == maxRetries)
            {
                return response;
            }

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            response.Dispose();
            await Task.Delay(delay > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10) : delay);
        }
    }
}
