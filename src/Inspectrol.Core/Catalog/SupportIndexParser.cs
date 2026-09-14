using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Inspectrol.Core.Catalog;

/// <remarks>
/// A category (<c>li.p-support__item</c>) contains models (nested <c>li.p-support__item</c>). Inside a model, each
/// <c>li.p-support__subitem</c> holds pairs of a title <c>span</c> followed by a <c>div.p-support__subitem-otv</c>
/// with the link.
/// </remarks>
public static partial class SupportIndexParser
{
    private const string SupportPrefix = "/support/";

    public static IReadOnlyList<CatalogModel> Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var models = new List<CatalogModel>();

        foreach (var item in document.QuerySelectorAll("li.p-support__item"))
        {
            // A category contains model items and is not a model itself.
            if (item.QuerySelector("li.p-support__item") is not null)
                continue;

            var subItems = item.QuerySelectorAll("li.p-support__subitem");
            if (subItems.Length == 0)
                continue;

            var name = Normalise(DirectSpanText(item));
            if (name.Length == 0)
                continue;

            var updates = subItems.SelectMany(ReadUpdates).ToList();
            if (updates.Count > 0)
                models.Add(new CatalogModel(Normalise(FindCategoryName(item)), name, updates));
        }

        EnsureNothingLost(document, models);
        return models;
    }

    // A silently incomplete model list would look complete, which is worse than an explicit failure.
    private static void EnsureNothingLost(IDocument document, List<CatalogModel> models)
    {
        var parsed = models.Sum(model => model.Updates.Count);
        var looksLikeSupportPage = document.QuerySelector("li.p-support__item") is not null;

        if (looksLikeSupportPage && parsed == 0)
            throw new CatalogFormatException(
                Strings.SupportIndexParser_NoUpdatesRead);

        var inMarkup = document
            .QuerySelectorAll("div.p-support__subitem-otv a[href]")
            .Count(link => IsSupportLink(link.GetAttribute("href")));

        if (parsed < inMarkup)
            throw new CatalogFormatException(
                string.Format(Strings.SupportIndexParser_UpdatesLost, inMarkup, parsed));
    }

    private static bool IsSupportLink(string? href)
    {
        var url = href?.Trim();
        return url is not null
            && url.StartsWith(SupportPrefix, StringComparison.OrdinalIgnoreCase)
            && !url.Contains("..");
    }

    // Also strips invisible characters: zero-width space, BOM, left-to-right mark and soft hyphen.
    private static string Normalise(string text) =>
        string.Join(' ', text
            .Replace("\u200b", "").Replace("\ufeff", "").Replace("\u200e", "").Replace("\u00ad", "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string DirectSpanText(IElement element) =>
        element.Children.FirstOrDefault(c => c.LocalName == "span")?.TextContent.Trim() ?? "";

    private static string FindCategoryName(IElement modelItem)
    {
        var parent = modelItem.ParentElement;
        while (parent is not null)
        {
            if (parent.LocalName == "li" && parent.ClassList.Contains("p-support__item"))
                return DirectSpanText(parent);
            parent = parent.ParentElement;
        }
        return "";
    }

    private static IEnumerable<UpdateLink> ReadUpdates(IElement subItem)
    {
        string? title = null;
        foreach (var child in subItem.Children)
        {
            if (child.LocalName == "span")
            {
                title = Normalise(child.TextContent);
            }
            else if (child.ClassList.Contains("p-support__subitem-otv"))
            {
                var href = child.QuerySelector("a")?.GetAttribute("href")?.Trim();
                if (title is { Length: > 0 } && IsSupportLink(href))
                    yield return new UpdateLink(Classify(title), title, href!, RegionOf(title));
                title = null;
            }
        }
    }

    private static string? RegionOf(string title)
    {
        var text = title.ToLowerInvariant();
        if (text.Contains("для рф") || text.Contains("из rf") || text.Contains("для россии"))
            return "РФ";
        if (text.Contains("узбекистан") || text.Contains("из uz"))
            return "УЗ";
        return null;
    }

    private static UpdateKind Classify(string title)
    {
        var text = title.ToLowerInvariant();
        if (text.Contains("emap")) return UpdateKind.EMap;
        if (text.Contains("по и базы данных")) return UpdateKind.FirmwareAndDatabase;
        if (text.Contains("заводск")) return UpdateKind.FactoryFirmware;
        if (text.Contains("обновление базы")) return UpdateKind.Database;
        if (FirmwareRegex().IsMatch(text)) return UpdateKind.Firmware;
        return UpdateKind.Unknown;
    }

    // Matches "(обновление ПО) v.1.2.5" and "обновление ПО для РФ", but not "…неудачного обновления ПО".
    [GeneratedRegex(@"обновление\s+по\b")]
    private static partial Regex FirmwareRegex();
}
