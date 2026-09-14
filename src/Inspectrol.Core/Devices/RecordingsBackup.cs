namespace Inspectrol.Core.Devices;

public sealed record RecordingsSummary(int Count, long TotalBytes, DateTime? Oldest, DateTime? Newest);

/// <remarks>
/// Dates come from file timestamps because recording names carry no year. The dashcam clock can be reset, but a
/// recording with a wrong timestamp then shows up as the oldest, which is harmless for the last-day selection.
/// </remarks>
public static class RecordingsBackup
{
    private const long FreeSpaceReserveBytes = 512L * 1024 * 1024;

    public static RecordingsSummary Summarise(IReadOnlyList<FileInfo> recordings)
    {
        if (recordings.Count == 0)
            return new RecordingsSummary(0, 0, null, null);

        return new RecordingsSummary(
            recordings.Count,
            recordings.Sum(file => file.Length),
            recordings.Min(file => file.LastWriteTime),
            recordings.Max(file => file.LastWriteTime));
    }

    public static IReadOnlyList<FileInfo> LastDay(IReadOnlyList<FileInfo> recordings)
    {
        if (recordings.Count == 0)
            return [];

        var day = recordings.Max(file => file.LastWriteTime).Date;
        return [.. recordings.Where(file => file.LastWriteTime.Date == day)];
    }

    public static IReadOnlyList<FileInfo> Between(IReadOnlyList<FileInfo> recordings, DateTime from, DateTime to) =>
        [.. recordings.Where(file => file.LastWriteTime.Date >= from.Date && file.LastWriteTime.Date <= to.Date)];

    // requiredFreeBytesOverride is a test hook.
    public static async Task CopyAsync(
        IReadOnlyList<FileInfo> files,
        DirectoryInfo target,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        long? requiredFreeBytesOverride = null)
    {
        target.Create();

        var needed = requiredFreeBytesOverride ?? files.Sum(file => file.Length) + FreeSpaceReserveBytes;
        var free = FreeSpaceOn(target);
        if (free is not null && free < needed)
        {
            throw new IOException(
                string.Format(Strings.RecordingsBackup_NotEnoughSpace, Megabytes(needed), Megabytes(free.Value)));
        }

        long copied = 0;
        var total = Math.Max(1, files.Sum(file => file.Length));

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destination = UniqueName(target, file.Name);
            await Task.Run(() => file.CopyTo(destination, overwrite: false), cancellationToken);
            EnsureCopyIsComplete(file.FullName, destination);

            copied += file.Length;
            progress?.Report((double)copied / total);
        }

        progress?.Report(1);
    }

    /// <remarks>
    /// The card may be erased after the backup, so a truncated copy is a lost recording. An interrupted copy
    /// (disconnected card reader, USB failure) shows in the size alone, and a byte compare of hundreds of gigabytes
    /// would take as long as the copy. The partial file is deleted so a retry does not leave a "(2)" copy next to it.
    /// </remarks>
    internal static void EnsureCopyIsComplete(string originalPath, string copyPath)
    {
        if (new FileInfo(copyPath).Length == new FileInfo(originalPath).Length)
            return;

        File.Delete(copyPath);
        throw new IOException(
            string.Format(Strings.RecordingsBackup_CopyIncomplete, Path.GetFileName(originalPath)));
    }

    private static long Megabytes(long bytes) => bytes / 1024 / 1024;

    private static long? FreeSpaceOn(DirectoryInfo folder)
    {
        try
        {
            return new DriveInfo(Path.GetPathRoot(folder.FullName)!).AvailableFreeSpace;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            // Unknown free space is no reason to refuse copying.
            return null;
        }
    }

    // Recording names repeat (no year, and the counter wraps around), and an overwritten recording is gone for good.
    private static string UniqueName(DirectoryInfo target, string fileName)
    {
        var path = Path.Combine(target.FullName, fileName);
        if (!File.Exists(path))
            return path;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);

        for (var number = 2; ; number++)
        {
            path = Path.Combine(target.FullName, $"{name} ({number}){extension}");
            if (!File.Exists(path))
                return path;
        }
    }
}
