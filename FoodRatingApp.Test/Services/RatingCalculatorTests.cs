using FoodRatingApp.Model;
using FoodRatingApp.Services;

namespace FoodRatingApp.Test.Services;

public class RatingCalculatorTests
{
    private static string[] Repeat(string rating, int count) => Enumerable.Repeat(rating, count).ToArray();

    [Fact]
    public void Calculate_Fhrs_ReturnsDescendingNumericOrderThenExempt()
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhrs, ["5"]);

        result.Select(r => r.Name).Should().Equal("5", "4", "3", "2", "1", "Exempt");
    }

    [Fact]
    public void Calculate_Fhis_ReturnsRequiredOrder()
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhis, ["Pass"]);

        result.Select(r => r.Name).Should().Equal("Pass and Eat Safe", "Pass", "Improvement Required");
    }

    [Fact]
    public void Calculate_Fhrs_MatchesUserStoryExample()
    {
        // 5: 50%, 1: 20%, Exempt: 30%
        string[] ratings = [.. Repeat("5", 5), .. Repeat("1", 2), .. Repeat("Exempt", 3)];

        var result = RatingCalculator.Calculate(RatingScheme.Fhrs, ratings);

        result.Select(r => r.Value).Should().Equal(50, 0, 0, 0, 20, 30);
    }

    [Fact]
    public void Calculate_Fhis_MatchesUserStoryExample()
    {
        // Pass and Eat Safe: 50%, Pass: 15%, Improvement Required: 35%
        string[] ratings = [.. Repeat("Pass and Eat Safe", 10), .. Repeat("Pass", 3), .. Repeat("Improvement Required", 7)];

        var result = RatingCalculator.Calculate(RatingScheme.Fhis, ratings);

        result.Select(r => r.Value).Should().Equal(50, 15, 35);
    }

    [Theory]
    [InlineData("AwaitingInspection")]
    [InlineData("Awaiting Inspection")]
    [InlineData("Awaiting Publication")]
    [InlineData("0")] // 0 is not displayed, so it is not counted
    [InlineData("")]
    [InlineData(null)]
    public void Calculate_IgnoresValuesOutsideTheScheme(string? notARating)
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhrs, ["5", "4", notARating, notARating]);

        result.Single(r => r.Name == "5").Value.Should().Be(50);
        result.Single(r => r.Name == "4").Value.Should().Be(50);
    }

    [Fact]
    public void Calculate_FhisRatingsInFhrsAuthority_AreIgnored()
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhrs, ["5", "Pass"]);

        result.Single(r => r.Name == "5").Value.Should().Be(100);
    }

    [Fact]
    public void Calculate_Fhis_ExemptIsNotPartOfTheScheme()
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhis, ["Pass", "Exempt"]);

        result.Should().HaveCount(3);
        result.Single(r => r.Name == "Pass").Value.Should().Be(100);
    }

    [Fact]
    public void Calculate_IsCaseAndWhitespaceInsensitive()
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhis, ["pass and eat safe", " PASS "]);

        result.Select(r => r.Value).Should().Equal(50, 50, 0);
    }

    [Fact]
    public void Calculate_NoRatedEstablishments_ReturnsAllCategoriesAtZero()
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhrs, ["AwaitingInspection"]);

        result.Should().HaveCount(6);
        result.Should().OnlyContain(r => r.Value == 0);
    }

    [Fact]
    public void Calculate_RoundsToTwoDecimalPlacesAndSumsToAbout100()
    {
        var result = RatingCalculator.Calculate(RatingScheme.Fhis, ["Pass and Eat Safe", "Pass", "Improvement Required"]);

        result.Should().OnlyContain(r => r.Value == 33.33);
        result.Sum(r => r.Value).Should().BeApproximately(100, 0.1);
    }
}
