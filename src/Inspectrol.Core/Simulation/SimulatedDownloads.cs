using System.Text.RegularExpressions;
using Inspectrol.Core.Cards;
using Inspectrol.Core.Downloading;

namespace Inspectrol.Core.Simulation;

// Produces files named like the ones in real Inspector archives, unless RealDownloads asks for the real thing.
public sealed partial class SimulatedDownloads(HttpClient http, SimulatedCard card) : IUpdateDownloads
{
    private readonly WebUpdateDownloads _web = new(http);

    public TimeSpan DownloadDuration { get; init; } = TimeSpan.FromSeconds(4);

    public async Task<FileInfo> DownloadAsync(string url, DirectoryInfo folder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (card.RealDownloads)
            return await _web.DownloadAsync(url, folder, progress, cancellationToken);

        var name = Path.GetFileName(new Uri(url).LocalPath);
        var duration = IsMaps(name) ? DownloadDuration * 4 : DownloadDuration;

        const int steps = 40;
        for (var step = 1; step <= steps; step++)
        {
            await Task.Delay(duration / steps, cancellationToken);
            progress?.Report((double)step / steps);
        }

        folder.Create();
        var path = Path.Combine(folder.FullName, name);
        await File.WriteAllTextAsync(path, url, cancellationToken);
        return new FileInfo(path);
    }

    public IReadOnlyList<CardFile> Extract(FileInfo archive, DirectoryInfo folder)
    {
        if (card.RealDownloads)
            return _web.Extract(archive, folder);

        var name = Path.GetFileNameWithoutExtension(archive.Name);

        if (IsMaps(name))
        {
            return
            [
                Make(folder, "eMap/map/cis/map.kdb", 20_000_000),
                Make(folder, "eMap/media/cis/cis_16c.fnt", 100_000),
                Make(folder, "eMap/system.ini", 10),
                Make(folder, "eMap/reinstall.dat", 0),
            ];
        }

        if (DatabaseName().Match(name) is { Success: true } database)
        {
            var model = new string([.. database.Groups["model"].Value.Where(char.IsLetterOrDigit)])
                .Replace("inspector", "", StringComparison.OrdinalIgnoreCase);
            return [Make(folder, $"{model}DB.bin", 3_000_000)];
        }

        if (name.Contains("barracuda", StringComparison.OrdinalIgnoreCase))
            return [Make(folder, "BarracudaFW.BRN", 8_000_000)];

        // Signature models ship the radar part as separate files; their archives are named like "AtlaS_1.0.5+E3.0".
        if (name.Contains("+E", StringComparison.Ordinal))
        {
            return
            [
                Make(folder, "firmware.bin", 6_000_000),
                Make(folder, "firmware2.bin", 6_000_000),
                Make(folder, "rdfw.bin", 70_000),
            ];
        }

        return [Make(folder, "firmware.bin", 6_000_000)];
    }

    private static bool IsMaps(string name) => name.Contains("emap", StringComparison.OrdinalIgnoreCase);

    private static CardFile Make(DirectoryInfo folder, string cardPath, long bytes)
    {
        var path = Path.Combine(folder.FullName, cardPath.Replace('/', Path.DirectorySeparatorChar));
        SimulatedCard.WriteRandom(path, bytes);
        return new CardFile(new FileInfo(path), cardPath);
    }

    // "AtlaSDB_37", "HookDB_0926", "inspector-Delta DB-09092026"
    [GeneratedRegex(@"^(?<model>.*?)[\s_-]*DB(?:[\s_-]|$)")]
    private static partial Regex DatabaseName();
}
