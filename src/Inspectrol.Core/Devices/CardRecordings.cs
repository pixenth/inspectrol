namespace Inspectrol.Core.Devices;

public static class CardRecordings
{
    // Sparta and AtlaS write to subfolders of DCIM such as 100MEDIA (AtlaS also to PARKING), Barracuda straight to
    // VIDEO. All three put protected recordings in EVENT.
    public static readonly string[] RecordingFolders = ["DCIM", "VIDEO", "EVENT", "PARKING"];

    // Sparta and AtlaS record MP4, Barracuda records AVI.
    private static readonly string[] RecordingExtensions = [".MP4", ".AVI"];

    public static IReadOnlyList<FileInfo> Find(DirectoryInfo cardRoot)
    {
        var found = new List<FileInfo>();

        foreach (var name in RecordingFolders)
        {
            var folder = new DirectoryInfo(Path.Combine(cardRoot.FullName, name));
            if (!folder.Exists)
                continue;

            try
            {
                found.AddRange(folder
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Where(IsRecording));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The card was removed during the scan; return what was found so far.
            }
        }

        return [.. found.OrderByDescending(file => file.LastWriteTimeUtc)];
    }

    private static bool IsRecording(FileInfo file) =>
        RecordingExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)
        // Files starting with "._" are AppleDouble metadata left by macOS, not recordings.
        && !file.Name.StartsWith("._", StringComparison.Ordinal);
}
