using Inspectrol.Core.Cards;

namespace Inspectrol.Tests;

public class CardRequirementsTests
{
    private const long Gigabyte = 1_000_000_000;

    [Fact]
    public void Big_card_of_an_ordinary_model_gets_exfat()
    {
        var rules = CardRequirements.For("Inspector Barracuda");

        Assert.Equal(CardFileSystem.ExFat, rules.FileSystemFor(64 * Gigabyte));
        Assert.Equal(CardFileSystem.Fat32, rules.FileSystemFor(16 * Gigabyte));
    }

    [Fact]
    public void Air_series_allows_only_small_cards_with_fat32()
    {
        var rules = CardRequirements.For("Inspector Star Air");

        Assert.Equal(CardFileSystem.Fat32, rules.FileSystemFor(16 * Gigabyte));
        Assert.Equal(32 * Gigabyte, rules.MaximumBytes);
    }

    [Fact]
    public void Card_that_is_too_big_is_refused_with_an_explanation()
    {
        var rules = CardRequirements.For("Inspector Star Air");

        var problem = rules.Check(64 * Gigabyte);

        Assert.NotNull(problem);
        Assert.Contains("32", problem);
    }

    [Fact]
    public void Suitable_card_has_no_problem()
    {
        Assert.Null(CardRequirements.For("Inspector Barracuda").Check(64 * Gigabyte));
    }

    [Fact]
    public void Card_that_is_too_small_is_refused_too()
    {
        // The Barracuda manual lists cards from 16 to 256 GB.
        var problem = CardRequirements.For("Inspector Barracuda").Check(8 * Gigabyte);

        Assert.NotNull(problem);
        Assert.Contains("16", problem);
    }

    [Fact]
    public void Unknown_model_gets_the_ordinary_rules()
    {
        var rules = CardRequirements.For("Inspector Чего-то-новое");

        Assert.Equal(CardFileSystem.ExFat, rules.FileSystemFor(64 * Gigabyte));
    }

    [Fact]
    public void Air_series_matches_whatever_the_site_calls_the_model()
    {
        // The site may call a model "Inspector Shot Air Pro" or "Shot Air".
        Assert.Equal(32 * Gigabyte, CardRequirements.For("Inspector Shot Air Pro").MaximumBytes);
        Assert.Equal(32 * Gigabyte, CardRequirements.For("Split Air").MaximumBytes);
    }
}
