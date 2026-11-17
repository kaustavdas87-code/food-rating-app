using FoodRatingApp.Model;

namespace FoodRatingApp.Services;

/// <summary>
/// Turns raw FSA establishment ratings into an ordered percentage distribution.
/// The rating schemes are treated as fixed.
/// </summary>
public static class RatingCalculator
{
    /// <summary>
    /// FHRS ratings in display order: descending numerically, then Exempt, as in the user story's example.
    /// A rating of 0 is not displayed, so 0-rated establishments are not counted in the percentages.
    /// </summary>
    private static readonly (string FsaValue, string Label)[] FhrsRatings =
    [
        ("5", "5"),
        ("4", "4"),
        ("3", "3"),
        ("2", "2"),
        ("1", "1"),
        ("Exempt", "Exempt"),
    ];

    /// <summary>FHIS ratings in the required display order.</summary>
    private static readonly (string FsaValue, string Label)[] FhisRatings =
    [
        ("Pass and Eat Safe", "Pass and Eat Safe"),
        ("Pass", "Pass"),
        ("Improvement Required", "Improvement Required"),
    ];

    public static IReadOnlyList<(string FsaValue, string Label)> RatingsFor(RatingScheme scheme) =>
        scheme == RatingScheme.Fhis ? FhisRatings : FhrsRatings;

    /// <summary>
    /// Calculates the share of each rating in the scheme, in display order.
    /// Every rating in the scheme is returned, including ones with 0%.
    /// Values outside the scheme (e.g. "Awaiting Inspection") are ignored, so the
    /// percentages are of rated establishments and sum to 100 (subject to rounding).
    /// </summary>
    public static List<AuthorityRatingItem> Calculate(RatingScheme scheme, IEnumerable<string?> ratingValues)
    {
        var ratings = RatingsFor(scheme);
        var counts = ratings.ToDictionary(r => r.FsaValue, _ => 0, StringComparer.OrdinalIgnoreCase);

        foreach (var value in ratingValues)
        {
            var key = value?.Trim();
            if (key is not null && counts.TryGetValue(key, out var count))
            {
                counts[key] = count + 1;
            }
        }

        var total = counts.Values.Sum();

        return ratings
            .Select(r => new AuthorityRatingItem
            {
                Name = r.Label,
                Value = total == 0 ? 0 : Math.Round(counts[r.FsaValue] * 100.0 / total, 2),
            })
            .ToList();
    }
}
