using System.Text.Json.Serialization;

namespace FoodRatingApp.Model;

/// <summary>
/// One page of results from the FSA <c>/Establishments</c> endpoint.
/// </summary>
public class FsaEstablishmentList
{
    [JsonPropertyName("establishments")]
    public List<FsaEstablishment> Establishments { get; set; } = [];

    [JsonPropertyName("meta")]
    public FsaPageMeta Meta { get; set; } = new();
}

public class FsaEstablishment
{
    /// <summary>The raw FSA rating, e.g. "5", "Exempt", "Pass and Eat Safe", "AwaitingInspection".</summary>
    public string RatingValue { get; set; } = string.Empty;

    public override string ToString() => $"FsaEstablishment[{nameof(RatingValue)}='{RatingValue}']";
}

public class FsaPageMeta
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("pageNumber")]
    public int PageNumber { get; set; }
}
