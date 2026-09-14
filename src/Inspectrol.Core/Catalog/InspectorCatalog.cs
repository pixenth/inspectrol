using System.Text.RegularExpressions;
using Inspectrol.Core.Planning;

namespace Inspectrol.Core.Catalog;

public sealed partial class InspectorCatalog(HttpClient http)
{
    private const string BaseUrl = "https://www.rd-inspector.ru";

    // File server folder where Inspector keeps older firmware versions.
    private const string ArchiveFolder = "/SOFT/ARCHIVE/";

    private static readonly string[] ArchiveExtensions = [".zip", ".rar"];

    // Brands come first in model names on the site but never appear in file names.
    private static readonly string[] BrandWords = ["Inspector", "Tomahawk", "Cenmax"];

    public async Task<IReadOnlyList<CatalogModel>> GetModelsAsync(CancellationToken cancellationToken)
    {
        var html = await http.GetStringAsync($"{BaseUrl}/support/", cancellationToken);
        var models = SupportIndexParser.Parse(html);
        if (models.Count == 0)
            throw new CatalogFormatException(Strings.InspectorCatalog_ModelListUnreadable);
        return models;
    }

    public async Task<ModelUpdateInfo> GetUpdateInfoAsync(CatalogModel model, string? region, CancellationToken cancellationToken)
    {
        if (UpdatedOverCable(model))
        {
            throw new CatalogFormatException(
                string.Format(Strings.InspectorCatalog_UpdatedOverCable, model.Name));
        }

        var firmwareLink = SelectFirmware(model, region);
        // Some models have firmware only, with no camera database on the site. That is not a site change.
        var databaseLink = model.Updates.FirstOrDefault(u => u.Kind == UpdateKind.Database);
        UpdatePage? databasePage = databaseLink is null ? null : await LoadAsync(databaseLink.PageUrl, cancellationToken);
        if (databasePage is not null && databasePage.FileUrl is null)
        {
            // Sister brands link to files on their own sites. That is a known limitation, not a site change.
            throw new CatalogFormatException(databasePage.ForeignFileUrl is not null
                ? string.Format(Strings.InspectorCatalog_DatabaseOnForeignSite, model.Name)
                : Strings.InspectorCatalog_DatabaseFileLinkMissing);
        }

        if (databaseLink is null && SelectFirmware(model, region) is null)
            throw new CatalogFormatException(string.Format(Strings.InspectorCatalog_NothingPublished, model.Name));

        UpdatePage? firmwarePage = firmwareLink is null ? null : await LoadAsync(firmwareLink.PageUrl, cancellationToken);

        var mapsLink = model.Updates.FirstOrDefault(u => u.Kind == UpdateKind.EMap);
        var mapsUrl = mapsLink is null ? null : await MapsFileAsync(mapsLink.PageUrl, cancellationToken);

        var archive = firmwarePage is null
            ? []
            : firmwarePage.ArchiveFileUrls
                .Select(url => (Url: url, Version: ArchiveFirmwareVersion(url, model.Name)))
                .Where(found => found.Version is not null)
                .Select(found => new ArchiveFirmware(found.Version!, found.Url))
                .OrderBy(firmware => firmware.Version)
                .ToList();

        // Firmware warnings matter only when the firmware changes. A warning found on both pages stays with the
        // database, whose warnings are always shown.
        var databaseWarnings = (databasePage?.Warnings ?? []).Distinct().ToList();
        var firmwareWarnings = (firmwarePage?.Warnings ?? []).Distinct().Except(databaseWarnings).ToList();

        return new ModelUpdateInfo(
            LatestFirmware: firmwareLink is null ? null : VersionFromTitle(firmwareLink.Title),
            FirmwareUrl: firmwarePage?.FileUrl,
            FirmwareRequires: firmwarePage?.MinimumFirmware,
            DatabaseDate: databaseLink is null ? null : DateFromTitle(databaseLink.Title),
            DatabaseUrl: databasePage?.FileUrl,
            DatabaseRequiresFirmware: databasePage?.MinimumFirmware,
            ArchiveFirmwares: archive,
            FirmwareWarnings: firmwareWarnings,
            DatabaseWarnings: databaseWarnings,
            FirmwareRequirementUnclear: firmwarePage?.RequirementUnclear ?? false,
            DatabaseRequirementUnclear: databasePage?.RequirementUnclear ?? false,
            EMapUrl: mapsUrl);
    }

    // eMap is optional, so a broken maps page must not block firmware and database updates.
    private async Task<string?> MapsFileAsync(string pageUrl, CancellationToken cancellationToken)
    {
        try
        {
            return (await LoadAsync(pageUrl, cancellationToken)).FileUrl;
        }
        catch (CatalogFormatException)
        {
            return null;
        }
    }

    // Database titles end with day/month/year: "Inspector AtlaS (обновление базы данных) 09/09/26".
    // Only slashes count as separators, so a version such as "1.2.25" is never read as a date.
    internal static DateOnly? DateFromTitle(string title)
    {
        var match = TitleDateRegex().Match(title);
        if (!match.Success)
            return null;

        var day = int.Parse(match.Groups["day"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var month = int.Parse(match.Groups["month"].Value, System.Globalization.CultureInfo.InvariantCulture);
        var year = int.Parse(match.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (year < 100)
            year += 2000;

        return month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;
    }

    [GeneratedRegex(@"(?<!\d)(?<day>\d{1,2})/(?<month>\d{1,2})/(?<year>\d{4}|\d{2})(?!\d)")]
    private static partial Regex TitleDateRegex();

    // Marlin S Pro and Mike S Pro have separate firmware for Russia and Uzbekistan.
    internal static UpdateLink? SelectFirmware(CatalogModel model, string? region)
    {
        var firmware = model.Updates.Where(u => u.Kind == UpdateKind.Firmware).ToList();
        if (firmware.Count == 0)
            return null;

        var regional = firmware.Where(u => u.Region is not null).ToList();
        if (regional.Count == 0)
            return firmware[0];

        if (region is null)
            throw new CatalogFormatException(
                string.Format(Strings.InspectorCatalog_RegionRequired, model.Name, string.Join(", ", regional.Select(u => u.Region))));

        return regional.FirstOrDefault(u => u.Region == region)
            ?? throw new CatalogFormatException(string.Format(Strings.InspectorCatalog_NoFirmwareForRegion, model.Name, region));
    }

    private async Task<UpdatePage> LoadAsync(string pageUrl, CancellationToken cancellationToken)
    {
        var url = pageUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? pageUrl : BaseUrl + pageUrl;
        var html = await http.GetStringAsync(url, cancellationToken);
        return UpdatePageParser.Parse(html);
    }

    internal static Version? VersionFromTitle(string title)
    {
        var match = TitleVersionRegex().Match(title);
        return match.Success && Version.TryParse(match.Groups["version"].Value, out var version) ? version : null;
    }

    /// <summary>
    /// Returns the version of an intermediate firmware file, or null when the link is not one.
    /// </summary>
    /// <remarks>
    /// Folder, extension and model name are all required. A version can be read from almost any file name, and
    /// archives of different models all contain <c>firmware.bin</c>, so this is the only place another model's
    /// firmware can be caught. A file name that does not match makes the planner refuse the chain, which is the
    /// safe failure.
    /// </remarks>
    internal static Version? ArchiveFirmwareVersion(string url, string modelName)
    {
        if (!url.Contains(ArchiveFolder, StringComparison.OrdinalIgnoreCase))
            return null;

        var fileName = Path.GetFileName(new Uri(url, UriKind.RelativeOrAbsolute).IsAbsoluteUri
            ? new Uri(url).LocalPath
            : url);

        if (!ArchiveExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            return null;

        var afterModelName = TextAfterModelName(Path.GetFileNameWithoutExtension(fileName), modelName);
        if (afterModelName is null)
            return null;

        var match = FileVersionRegex().Match(afterModelName);
        return match.Success && Version.TryParse(match.Groups["version"].Value, out var version) ? version : null;
    }

    // Updated over a cable, checked by hand against the site; the page structure alone does not catch these.
    // The Air series (Star Air, Shot Air, Split Air) is deliberately absent: those models update from a card.
    private static readonly string[] CableFamilies =
        ["Spirit", "Scout", "Tau S", "GT", "GTS", "RD X2", "RD X3"];

    // The site does not say this directly. USB model pages offer only a combined firmware and database update.
    private static bool UpdatedOverCable(CatalogModel model)
    {
        if (model.Updates.Any(u => u.Kind == UpdateKind.FirmwareAndDatabase)
            && model.Updates.All(u => u.Kind != UpdateKind.Database))
        {
            return true;
        }

        var withoutBrand = ModelWithoutBrand(model.Name);
        return CableFamilies.Any(family =>
            withoutBrand.Equals(family, StringComparison.OrdinalIgnoreCase)
            || withoutBrand.StartsWith(family + " ", StringComparison.OrdinalIgnoreCase));
    }

    private static string ModelWithoutBrand(string modelName)
    {
        var words = modelName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', ModelBrand(modelName) is null ? words : words[1..]);
    }

    private static string? ModelBrand(string modelName)
    {
        var words = modelName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 1 && BrandWords.Contains(words[0], StringComparer.OrdinalIgnoreCase) ? words[0] : null;
    }

    // "Inspector Marlin S Pro" -> "marlinspro"
    private static string ModelKey(string modelName) => Normalise(ModelWithoutBrand(modelName));

    /// <summary>
    /// Returns the text after the model name in a file name, or null when the file is named after another model.
    /// </summary>
    /// <remarks>
    /// A plain "contains" check is wrong: short model names are prefixes of longer ones on the site (Marlin S and
    /// Marlin S Pro, Scat and Scat SE, Inspector Alfa and Cenmax Alfa Signature), so Marlin S would accept
    /// MARLIN_S_PRO_1.0.5_RF.zip. The name must start at the first word or right after the brand, which rejects shared
    /// eMap files like "Scat SE_SEQ_MapS_AtlaS_…", and be followed by the end, a number or a FW/SW/V mark
    /// ("BARRACUDA_FW_1.2.0", "MARLIN S PRO_SW1.0.4"). Words may be joined, as in "MarlinS_1.6.4+E3.0".
    /// A numeric continuation ("Marlin S Pro" vs "Marlin S Pro 2.0") cannot be told apart without the full model list.
    /// </remarks>
    private static string? TextAfterModelName(string fileName, string modelName)
    {
        var words = WordRegex().Matches(fileName);
        var brand = ModelBrand(modelName);
        var first = brand is not null && words.Count > 0
            && words[0].Value.Equals(brand, StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        var key = ModelKey(modelName);
        var joined = "";
        for (var index = first; index < words.Count; index++)
        {
            joined += words[index].Value.ToLowerInvariant();
            if (!key.StartsWith(joined, StringComparison.Ordinal))
                return null;
            if (joined.Length < key.Length)
                continue;

            var next = index + 1;
            if (next < words.Count && !CanFollowModelName(words[next].Value))
                return null;
            return fileName[(words[index].Index + words[index].Length)..];
        }

        return null;
    }

    private static bool CanFollowModelName(string word) =>
        char.IsAsciiDigit(word[0]) || FirmwareMarkRegex().IsMatch(word);

    private static string Normalise(string text) =>
        new([.. text.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    // Titles mark the version with "v" ("v.1.2.5", "v. 1.0.5"). Without the anchor the first number could come from
    // the model name, such as "2.0" in "Inspector Marlin S Pro 2.0".
    [GeneratedRegex(@"\bv\.?\s*(?<version>\d+\.\d+(?:\.\d+){0,2})", RegexOptions.IgnoreCase)]
    private static partial Regex TitleVersionRegex();

    // File names have no "v" mark ("BARRACUDA_FW_1.2.0.zip"). Taking the first number is safe only because it is
    // applied to the text after the model name.
    [GeneratedRegex(@"(?<version>\d+\.\d+(?:\.\d+){0,2})")]
    private static partial Regex FileVersionRegex();

    // "MARLIN S PRO_SW1.0.4" -> MARLIN, S, PRO, SW1, 0, 4
    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex WordRegex();

    [GeneratedRegex(@"^(?:fw|sw|v)\d*$", RegexOptions.IgnoreCase)]
    private static partial Regex FirmwareMarkRegex();
}
