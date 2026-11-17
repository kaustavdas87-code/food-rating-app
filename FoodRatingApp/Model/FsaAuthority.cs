namespace FoodRatingApp.Model;

public class FsaAuthority
{
    public int LocalAuthorityId { get; set; }

    public required string Name { get; set; }

    /// <summary>FSA scheme type id: 1 = FHRS (England, Wales, NI), 2 = FHIS (Scotland).</summary>
    public int SchemeType { get; set; } = (int)RatingScheme.Fhrs;

    public override string ToString()
    {
        return $"FsaAuthority[id={LocalAuthorityId}, name='{Name}', scheme={SchemeType}]";
    }
}
