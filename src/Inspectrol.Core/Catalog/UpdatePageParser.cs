using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Inspectrol.Core.Catalog;

/// <param name="FileUrl">File on the Inspector file server, the only host files are downloaded from.</param>
/// <param name="ForeignFileUrl">Tomahawk and Cenmax pages link to files that Inspector does not host.</param>
/// <param name="Warnings">Verbatim and in page order. The app does not decide which of them matter.</param>
/// <param name="RequirementUnclear">
/// Something looks like a version requirement but could not be parsed. Such an update must not proceed silently.
/// </param>
/// <param name="ArchiveFileUrls">Other Inspector files linked from the page, such as intermediate firmware.</param>
public sealed record UpdatePage(
    string? FileUrl,
    string? ForeignFileUrl,
    IReadOnlyList<string> Warnings,
    Version? MinimumFirmware,
    bool RequirementUnclear,
    IReadOnlyList<string> ArchiveFileUrls);

public static partial class UpdatePageParser
{
    private const string FileHost = "inspector-update.me";

    // Page content without the header, menu and footer.
    private const string ContentSelector = "div.item_info_section";

    // A warning is assembled from adjacent inline nodes and ends at the nearest of these.
    private static readonly HashSet<string> BlockTags =
    [
        "address", "article", "aside", "blockquote", "body", "dd", "div", "dl", "dt",
        "fieldset", "figure", "footer", "form", "h1", "h2", "h3", "h4", "h5", "h6",
        "header", "hr", "li", "main", "nav", "ol", "p", "pre", "section", "table",
        "tbody", "td", "tfoot", "th", "thead", "tr", "ul",
    ];

    public static UpdatePage Parse(string html)
    {
        var document = new HtmlParser().ParseDocument(html);

        var button = document.QuerySelector("a.url_file_btn");
        var href = NormaliseUrl(button?.GetAttribute("href"));

        if (button is not null && href is null)
            throw new CatalogFormatException(
                Strings.UpdatePageParser_ButtonWithoutAddress);

        // No button on an update page means the site changed. An empty result would look like a missing update.
        if (button is null && LooksLikeUpdatePage(document))
            throw new CatalogFormatException(
                Strings.UpdatePageParser_ButtonMissing);

        var onInspectorServer = IsInspectorFile(href);

        var archive = document.QuerySelectorAll("a[href]")
            .Select(link => NormaliseUrl(link.GetAttribute("href")))
            .Where(IsInspectorFile)
            .Select(url => url!)
            .Where(url => url != href)
            .Distinct()
            .ToList();

        // Outside the content block the cookie banner and delivery terms would be taken for warnings.
        var content = document.QuerySelector(ContentSelector) ?? document.Body;
        var warnings = content is null ? [] : FindWarnings(content);
        var minimum = MinimumFirmwareFrom(warnings);

        return new UpdatePage(
            onInspectorServer ? href : null,
            onInspectorServer ? null : href,
            warnings,
            minimum,
            minimum is null && HasRequirementHint(content),
            archive);
    }

    private static bool LooksLikeUpdatePage(IDocument document) =>
        document.QuerySelector($"{ContentSelector}, div.bx_item_title") is not null
        || document.QuerySelectorAll("h1").Any(h => UpdateTitleRegex().IsMatch(h.TextContent));

    /// <remarks>
    /// Inspector splits a warning across adjacent inline elements (the version in one span, the rest of the sentence
    /// with a link in the next), so the whole inline run around the marker word is taken. A warning split across two
    /// blocks loses its second half; a version requirement lost that way is still caught by
    /// <see cref="HasRequirementHint"/>.
    /// </remarks>
    private static IReadOnlyList<string> FindWarnings(IElement content)
    {
        var found = new List<string>();

        foreach (var element in content.QuerySelectorAll("*"))
        {
            if (!WarningRegex().IsMatch(element.TextContent))
                continue;

            // Only the deepest match, or one warning comes back once per enclosing element.
            if (element.Children.Any(child => WarningRegex().IsMatch(child.TextContent)))
                continue;

            var text = BlockTextAround(element);
            if (text.Length > 0 && !found.Contains(text))
                found.Add(text);
        }

        return found;
    }

    private static string BlockTextAround(IElement element)
    {
        var block = NearestBlock(element);
        var run = new List<INode>();

        foreach (var child in block.ChildNodes)
        {
            if (child is IElement childElement && BlockTags.Contains(childElement.LocalName))
            {
                if (ContainsNode(run, element))
                    return TextOf(run);
                run.Clear();
            }
            else
            {
                run.Add(child);
            }
        }

        return ContainsNode(run, element) ? TextOf(run) : "";
    }

    private static bool ContainsNode(List<INode> run, IElement element) =>
        run.Any(node => node == element || node.Contains(element));

    private static IElement NearestBlock(IElement element)
    {
        var parent = element.ParentElement;
        while (parent is not null && !BlockTags.Contains(parent.LocalName))
            parent = parent.ParentElement;
        return parent ?? element;
    }

    private static string TextOf(IEnumerable<INode> nodes)
    {
        var text = new StringBuilder();
        foreach (var node in nodes)
            Append(node, text);
        return NormaliseText(text.ToString());

        // A <br> has no text, but the words on either side of it must not run together.
        static void Append(INode node, StringBuilder text)
        {
            switch (node)
            {
                case IText content:
                    text.Append(content.Data);
                    break;
                case IElement element when element.LocalName == "br":
                    text.Append(' ');
                    break;
                case IElement element:
                    foreach (var child in element.ChildNodes)
                        Append(child, text);
                    break;
            }
        }
    }

    /// <remarks>
    /// Only warnings are searched: the rest of the page has unrelated requirements of the same form
    /// ("Android не ниже версии 5.1.2"). The strictest requirement wins, so a firmware page may pick up the database
    /// requirement. That can cause an unnecessary refusal, which is the safe side.
    /// </remarks>
    private static Version? MinimumFirmwareFrom(IReadOnlyList<string> warnings)
    {
        Version? strictest = null;

        foreach (var warning in warnings)
        {
            foreach (Match match in MinimumVersionRegex().Matches(warning))
            {
                if (!Version.TryParse(match.Groups["version"].Value, out var version))
                    continue;
                if (strictest is null || version > strictest)
                    strictest = version;
            }
        }

        return strictest;
    }

    /// <remarks>
    /// Inspector words requirements in many ways ("не ниже версии X", "X и выше", "только поверх X", "X или новее")
    /// and only one is parsed. Reading an unparsed one as "no requirement" could put firmware over an unsuitable
    /// version, so such a page is flagged and refused.
    /// </remarks>
    private static bool HasRequirementHint(IElement? content)
    {
        if (content is null)
            return false;

        var text = NormaliseText(content.TextContent);
        return RequirementBeforeVersionRegex().IsMatch(text) || RequirementAfterVersionRegex().IsMatch(text);
    }

    // Links on the site may have surrounding spaces or a malformed scheme such as "https:///host/...".
    private static string? NormaliseUrl(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return null;
        return SlashesRegex().Replace(href.Trim(), "https://");
    }

    private static string NormaliseText(string? text) =>
        WhitespaceRegex().Replace(text ?? "", " ").Trim();

    private static bool IsInspectorFile(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed)
        && (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp)
        && (parsed.Host.Equals(FileHost, StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith("." + FileHost, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"^https?:/+")]
    private static partial Regex SlashesRegex();

    [GeneratedRegex("ВНИМАНИЕ|ВАЖНО", RegexOptions.IgnoreCase)]
    private static partial Regex WarningRegex();

    [GeneratedRegex("обновление|emap", RegexOptions.IgnoreCase)]
    private static partial Regex UpdateTitleRegex();

    [GeneratedRegex(@"не ниже(?:\s+версии)?(?:\s+ПО)?(?:\s+SW)?\s*v?\.?\s*(?<version>\d+(?:\.\d+){1,3})", RegexOptions.IgnoreCase)]
    private static partial Regex MinimumVersionRegex();

    [GeneratedRegex(@"(не ниже|не старше|не менее|поверх|начиная с)[^.!?]{0,40}?\d+\.\d+", RegexOptions.IgnoreCase)]
    private static partial Regex RequirementBeforeVersionRegex();

    [GeneratedRegex(@"\d+\.\d+[^.!?]{0,20}?(и выше|и новее|или новее|и старше)", RegexOptions.IgnoreCase)]
    private static partial Regex RequirementAfterVersionRegex();
}
