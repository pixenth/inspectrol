namespace Inspectrol.Core.Devices;

public sealed record RemovableCard(
    DirectoryInfo Root,
    string Label,
    long TotalBytes,
    long FreeBytes,
    bool LooksLikeDashcam);

public static class RemovableCards
{
    public static IReadOnlyList<RemovableCard> Find()
    {
        var found = new List<RemovableCard>();

        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType != DriveType.Removable || !drive.IsReady)
                    continue;

                var root = new DirectoryInfo(drive.RootDirectory.FullName);
                found.Add(new RemovableCard(
                    root,
                    drive.VolumeLabel,
                    drive.TotalSize,
                    drive.AvailableFreeSpace,
                    LooksLikeDashcam(root)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The drive disappeared between enumeration and query; skip it.
            }
        }

        return found;
    }

    private static bool LooksLikeDashcam(DirectoryInfo root)
    {
        try
        {
            return CardRecordings.RecordingFolders.Any(
                folder => Directory.Exists(Path.Combine(root.FullName, folder)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
