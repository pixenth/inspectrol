using System.Text.RegularExpressions;
using Inspectrol.Core.Catalog;

namespace Inspectrol.Core.Devices;

public static partial class DeviceRecognition
{
    // One recording is not enough to notice a card moved between dashcams; reading all of them is wasted work on a
    // card with thousands of files.
    private const int RecordingsToSample = 24;

    // A shorter tag would match almost any model.
    private const int MinimumTagLength = 3;

    // Models already updated through Inspectrol on a real device.
    public static IReadOnlyList<string> VerifiedModels { get; } = [];

    public static CardRecognition Recognise(
        DirectoryInfo cardRoot,
        IReadOnlyList<CatalogModel> catalog,
        IReadOnlyList<string>? verifiedModels = null)
    {
        var recordings = CardRecordings.Find(cardRoot);
        if (recordings.Count == 0)
            return RecogniseByUpdateFiles(cardRoot, catalog);

        var tags = ReadTags(recordings);
        if (tags.Count == 0)
        {
            return CardRecognition.Unknown(
                Strings.DeviceRecognition_TagsUnreadable);
        }

        var models = tags.Select(tag => tag.ModelTag).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (models.Count > 1)
        {
            return CardRecognition.Unknown(
                string.Format(Strings.DeviceRecognition_SeveralDevices, string.Join(", ", models)));
        }

        // The first tag comes from the newest recording, which shows the current firmware.
        var newest = tags[0];
        var found = Match(newest.ModelTag, catalog);

        if (found.Count == 0)
        {
            return CardRecognition.Unknown(
                string.Format(Strings.DeviceRecognition_ModelNotOnSite, newest.ModelTag));
        }

        if (found.Count > 1)
        {
            return CardRecognition.Unknown(
                string.Format(Strings.DeviceRecognition_SeveralModelsMatch, newest.ModelTag, string.Join(", ", found.Select(model => model.Name))));
        }

        var verified = (verifiedModels ?? VerifiedModels)
            .Contains(found[0].Name, StringComparer.OrdinalIgnoreCase);

        return CardRecognition.Recognised(new CardDevice(
            found[0],
            newest.Firmware,
            verified ? RecognitionConfidence.Verified : RecognitionConfidence.Guessed));
    }

    /// <remarks>
    /// Firmware is called "firmware.bin" for every model, but the camera database is named after the model
    /// ("AtlaSDB.bin"). Neither carries a version, and the files may be left over from another dashcam, so this is
    /// only a guess.
    /// </remarks>
    private static CardRecognition RecogniseByUpdateFiles(DirectoryInfo cardRoot, IReadOnlyList<CatalogModel> catalog)
    {
        var names = DatabaseFileModels(cardRoot);
        if (names.Count == 0)
        {
            return CardRecognition.Unknown(
                Strings.DeviceRecognition_NoRecordings);
        }

        var found = names
            .SelectMany(name => Match(name, catalog))
            .DistinctBy(model => model.Name)
            .ToList();

        if (found.Count != 1)
        {
            return CardRecognition.Unknown(
                Strings.DeviceRecognition_UpdateFilesAmbiguous);
        }

        return CardRecognition.Recognised(
            new CardDevice(found[0], Firmware: null, RecognitionConfidence.Guessed, FromUpdateFiles: true));
    }

    private static List<string> DatabaseFileModels(DirectoryInfo cardRoot)
    {
        try
        {
            return [.. cardRoot.EnumerateFiles("*.bin")
                .Select(file => DatabaseFileRegex().Match(file.Name))
                .Where(match => match.Success)
                .Select(match => match.Groups["model"].Value)];
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    [GeneratedRegex(@"^(?<model>.+?)DB\.bin$", RegexOptions.IgnoreCase)]
    private static partial Regex DatabaseFileRegex();

    private static List<RecordingTag> ReadTags(IReadOnlyList<FileInfo> recordings)
    {
        var tags = new List<RecordingTag>();

        foreach (var file in Sample(recordings))
        {
            var tag = RecordingTagReader.Read(file);
            if (tag is not null)
                tags.Add(tag);
        }

        return tags;
    }

    // Spread evenly over the life of the card, so recordings from another dashcam are noticed even in the middle.
    private static IEnumerable<FileInfo> Sample(IReadOnlyList<FileInfo> recordings)
    {
        if (recordings.Count <= RecordingsToSample)
            return recordings;

        var step = (double)(recordings.Count - 1) / (RecordingsToSample - 1);
        return Enumerable.Range(0, RecordingsToSample)
            .Select(index => recordings[(int)Math.Round(index * step)])
            .Distinct();
    }

    // Site names include the brand ("Inspector Atlas") and tags do not ("AtlaS"), hence the suffix match.
    // The site itself spells both "Atlas" and "AtlaS".
    private static List<CatalogModel> Match(string modelTag, IReadOnlyList<CatalogModel> catalog)
    {
        var tag = Normalise(modelTag);
        if (tag.Length < MinimumTagLength)
            return [];

        var exact = catalog.Where(model => Normalise(model.Name) == tag).ToList();
        if (exact.Count > 0)
            return exact;

        return catalog.Where(model => Normalise(model.Name).EndsWith(tag, StringComparison.Ordinal)).ToList();
    }

    private static string Normalise(string text) =>
        new([.. text.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
}
