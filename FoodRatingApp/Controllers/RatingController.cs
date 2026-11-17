using FoodRatingApp.Model;
using FoodRatingApp.Services;

namespace FoodRatingApp.Controllers;

[Route("api")]
[ApiController]
public class RatingController(IFsaClient fsaClient) : Controller
{
    private readonly IFsaClient _fsaClient = fsaClient;

    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    /// <summary>
    /// Produces one page of authorities for the select dropdown, which lazy-loads further pages
    /// as the user asks for them. Only the requested page is fetched from the FSA API.
    /// </summary>
    /// <param name="page">1-based page number (default 1).</param>
    /// <param name="pageSize">Items per page (default 20, capped at 100).</param>
    /// <returns>A <see cref="PagedResult{T}"/> of authorities, ordered by name.</returns>
    [HttpGet]
    public async Task<IActionResult> GetAsync([FromQuery] int page = 1, [FromQuery] int pageSize = DefaultPageSize)
    {
        if (page < 1)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid page", Detail = "page must be 1 or greater." });
        }
        if (pageSize < 1)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid pageSize", Detail = "pageSize must be 1 or greater." });
        }

        var size = Math.Min(pageSize, MaxPageSize);
        var fsaPage = await _fsaClient.GetAuthoritiesPage(page, size);

        var authorities = (fsaPage.Authorities ?? [])
            .Select(authority => new Authority
            {
                Id = authority.LocalAuthorityId,
                Name = authority.Name,
            })
            .ToList();

        return Json(new PagedResult<Authority>
        {
            Items = authorities,
            Page = page,
            PageSize = size,
            HasNext = authorities.Count == size,
        });
    }

    /// <summary>
    /// Produces the ratings for a specific authority for the table
    /// </summary>
    /// <param name="authorityId">The authority to calculate ratings for</param>
    /// <returns>
    /// The ratings to display, in scheme order: FHRS 5 to 1 then Exempt;
    /// FHIS Pass and Eat Safe, Pass, Improvement Required.
    /// </returns>
    [HttpGet("{authorityId}")]
    public async Task<IActionResult> Get(int authorityId)
    {
        if (authorityId < 1)
        {
            return BadRequest(new ProblemDetails { Title = "Invalid authorityId", Detail = "authorityId must be 1 or greater." });
        }

        var authority = await _fsaClient.GetAuthority(authorityId);
        if (authority is null)
        {
            return NotFound(new ProblemDetails { Title = "Authority not found", Detail = $"No authority with id {authorityId}." });
        }

        var scheme = authority.SchemeType == (int)RatingScheme.Fhis ? RatingScheme.Fhis : RatingScheme.Fhrs;
        var establishments = await _fsaClient.GetEstablishments(authorityId);
        var ratings = RatingCalculator.Calculate(scheme, establishments.Select(e => e.RatingValue));

        return Json(ratings);
    }
}
