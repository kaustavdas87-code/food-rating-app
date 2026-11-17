namespace FoodRatingApp.Model;

/// <summary>
/// FSA rating schemes. Values match the FSA API's SchemeType ids.
/// </summary>
public enum RatingScheme
{
    /// <summary>Food Hygiene Rating Scheme (England, Wales, Northern Ireland): 5 to 0, or Exempt.</summary>
    Fhrs = 1,

    /// <summary>Food Hygiene Information Scheme (Scotland): Pass and Eat Safe, Pass, Improvement Required.</summary>
    Fhis = 2,
}
