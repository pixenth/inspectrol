namespace Inspectrol.Core.Cards;

public enum CardFileSystem { Fat32, ExFat }

public sealed record CardRules(long MinimumBytes, long MaximumBytes, long Fat32UpToBytes)
{
    public CardFileSystem FileSystemFor(long cardBytes) =>
        cardBytes <= Fat32UpToBytes ? CardFileSystem.Fat32 : CardFileSystem.ExFat;

    public string? Check(long cardBytes)
    {
        if (cardBytes > MaximumBytes)
        {
            return string.Format(Strings.CardRules_TooLarge, Gigabytes(cardBytes), Gigabytes(MaximumBytes));
        }

        if (cardBytes < MinimumBytes)
        {
            return string.Format(Strings.CardRules_TooSmall, Gigabytes(cardBytes), Gigabytes(MinimumBytes));
        }

        return null;
    }

    // Round, so a card sold as 16 GB is reported as 16 and not 15.
    private static long Gigabytes(long bytes) => (long)Math.Round(bytes / 1_000_000_000d);
}

// Collected by hand from Inspector manuals; the site does not publish card requirements in a uniform way.
// Unknown models get the ordinary rules.
public static class CardRequirements
{
    // Decimal, as on card labels and in the manuals. A card labelled 16 GB has about 15.9 billion bytes and would
    // fail a binary 16 GiB minimum.
    private const long Gigabyte = 1_000_000_000;

    private static readonly CardRules Ordinary = new(16 * Gigabyte, 256 * Gigabyte, 32 * Gigabyte);

    // Air series manuals allow only cards up to 32 GB, FAT32.
    private static readonly CardRules AirSeries = new(16 * Gigabyte, 32 * Gigabyte, 32 * Gigabyte);

    private static readonly string[] AirModels =
        ["Star Air", "Shot Air", "Shot Air Pro", "Split Air"];

    public static CardRules For(string modelName) =>
        AirModels.Any(air => modelName.Contains(air, StringComparison.OrdinalIgnoreCase))
            ? AirSeries
            : Ordinary;
}
