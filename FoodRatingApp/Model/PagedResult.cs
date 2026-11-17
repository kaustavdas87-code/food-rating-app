namespace FoodRatingApp.Model;

/// <summary>
/// One page of results for lazy loading: the client asks for the next page only when it needs it.
/// There is no total count, because the FSA API does not give one for a single page and
/// working it out would mean downloading every page.
/// </summary>
public class PagedResult<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public bool HasPrevious => Page > 1;

    /// <summary>
    /// True when this page came back full, so there may be another page. When the last page is
    /// exactly full, asking for the next one returns no items and <c>HasNext = false</c>.
    /// </summary>
    public bool HasNext { get; init; }
}
