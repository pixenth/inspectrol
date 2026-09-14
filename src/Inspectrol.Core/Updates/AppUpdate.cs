using System.Security.Cryptography;
using System.Text.Json;
using Inspectrol.Core.Downloading;

namespace Inspectrol.Core.Updates;

public sealed record AppUpdate(Version Version, Uri Url, string Sha256, long? Size, string? Notes);

public static class UpdateChecker
{
    // Any failure means "no update": the check must never get in the way of updating a dashcam.
    public static async Task<AppUpdate?> CheckAsync(HttpClient http, Uri source, Version current, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(source, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var update = Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return update is not null && Normalise(update.Version) > Normalise(current) ? update : null;
        }
        // Broken servers fail in odd ways, such as an unknown charset in Content-Type throwing InvalidOperationException.
        catch (Exception)
        {
            return null;
        }
    }

    public static AppUpdate? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            if (!Version.TryParse(Text(root, "version"), out var version))
                return null;

            if (!Uri.TryCreate(Text(root, "url"), UriKind.Absolute, out var url)
                || !IsAllowed(url)
                || !url.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var sha256 = Text(root, "sha256");
            if (sha256 is not { Length: 64 } || !sha256.All(Uri.IsHexDigit))
                return null;

            long? size = root.TryGetProperty("size", out var sizeValue)
                && sizeValue.ValueKind == JsonValueKind.Number
                && sizeValue.TryGetInt64(out var bytes)
                && bytes > 0
                    ? bytes
                    : null;

            return new AppUpdate(version, url, sha256.ToLowerInvariant(), size, Text(root, "notes"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Plain HTTP is accepted only from this computer, for testing against a local server.
    public static bool IsAllowed(Uri url) =>
        url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback);

    // "0.2" and "0.2.0.0" must compare equal to "0.2.0".
    private static Version Normalise(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text
            ? text
            : null;
}

public static class UpdateDownloader
{
    public static async Task<FileInfo> DownloadAsync(
        HttpClient http,
        AppUpdate update,
        DirectoryInfo folder,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var file = await new FileDownloader(http).DownloadAsync(update.Url.AbsoluteUri, folder, progress, cancellationToken);

        if (update.Size is { } size && file.Length != size)
        {
            file.Delete();
            throw new InvalidDataException(string.Format(Strings.UpdateDownloader_WrongSize, file.Length, size));
        }

        string hash;
        await using (var stream = file.OpenRead())
            hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));

        if (hash != update.Sha256)
        {
            file.Delete();
            throw new InvalidDataException(Strings.UpdateDownloader_WrongHash);
        }

        return file;
    }
}
