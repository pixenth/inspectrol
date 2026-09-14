using Inspectrol.Core.Downloading;

namespace Inspectrol.Core.Devices;

public enum CardContentKind { Recordings, UpdateFiles, Empty, OtherFiles }

// Decides whether a drive may go on towards the step that erases it, so a flash drive with photos is never offered.
public static class CardContents
{
    public static CardContentKind Classify(DirectoryInfo root)
    {
        try
        {
            if (CardRecordings.RecordingFolders.Any(folder => Directory.Exists(Path.Combine(root.FullName, folder))))
                return CardContentKind.Recordings;

            // Windows creates hidden system entries such as "System Volume Information" on any card.
            var visible = root.EnumerateFileSystemInfos()
                .Where(entry => (entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                .ToList();

            if (visible.Count == 0)
                return CardContentKind.Empty;

            return visible.All(IsUpdate)
                ? CardContentKind.UpdateFiles
                : CardContentKind.OtherFiles;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A drive that cannot be read may hold unrelated files.
            return CardContentKind.OtherFiles;
        }
    }

    private static bool IsUpdate(FileSystemInfo entry) => entry switch
    {
        FileInfo file => ArchiveExtractor.IsDeviceFile(file),
        DirectoryInfo folder => folder.Name.Equals(ArchiveExtractor.MapsFolderName, StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
