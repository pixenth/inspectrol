using System.IO.Compression;
using Inspectrol.Core.Cards;
using SharpCompress.Archives;
using SharpCompress.Readers;

namespace Inspectrol.Core.Downloading;

/// <remarks>
/// Firmware and database archives hold loose files that go into the card root. eMap archives hold an "eMap" folder that
/// the dashcam expects in the card root with its whole structure, so those files keep their paths.
/// </remarks>
public static class ArchiveExtractor
{
    public const string MapsFolderName = "eMap";

    private const string MapsFolder = MapsFolderName + "/";

    private static readonly string[] DeviceFileExtensions = [".brn", ".bin", ".img", ".dat"];

    // "Rar!" starts RAR archives of every version.
    private static readonly byte[] RarSignature = [0x52, 0x61, 0x72, 0x21];

    public static IReadOnlyList<CardFile> Extract(FileInfo archive, DirectoryInfo targetFolder)
    {
        targetFolder.Create();

        var extracted = StartsWith(archive, RarSignature)
            ? ExtractRar(archive, targetFolder)
            : ExtractZip(archive, targetFolder);

        var maps = extracted
            .Where(entry => entry.Path.StartsWith(MapsFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (maps.Count > 0)
        {
            var hasMapData = maps.Any(entry =>
                entry.Path.StartsWith(MapsFolder + "map/", StringComparison.OrdinalIgnoreCase) && entry.File.Length > 0);
            if (!hasMapData)
                throw new InvalidDataException(Strings.ArchiveExtractor_NoDeviceFiles);

            return [.. maps.Select(entry => new CardFile(entry.File, entry.Path))];
        }

        // An empty device file is a broken fragment, and the dashcam would take it for firmware.
        var deviceFiles = extracted.Where(entry => IsDeviceFile(entry.File)).ToList();
        if (!deviceFiles.Any(entry => entry.File.Length > 0))
            throw new InvalidDataException(Strings.ArchiveExtractor_NoDeviceFiles);

        return [.. deviceFiles.Select(entry => CardFile.InRoot(entry.File))];
    }

    private static List<(string Path, FileInfo File)> ExtractZip(FileInfo archive, DirectoryInfo targetFolder)
    {
        var extracted = new List<(string Path, FileInfo File)>();

        using var zip = OpenZip(archive);
        foreach (var entry in zip.Entries)
        {
            var path = entry.FullName.Replace('\\', '/');
            if (path.EndsWith('/') || IsMacJunk(path))
                continue;

            var destination = Destination(targetFolder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, overwrite: true);
            extracted.Add((path, new FileInfo(destination)));
        }

        return extracted;
    }

    private static List<(string Path, FileInfo File)> ExtractRar(FileInfo archive, DirectoryInfo targetFolder)
    {
        var extracted = new List<(string Path, FileInfo File)>();

        try
        {
            using var rar = ArchiveFactory.OpenArchive(archive.FullName, new ReaderOptions());
            foreach (var entry in rar.Entries)
            {
                if (entry.IsDirectory || entry.Key is not { } key)
                    continue;

                string path = key.Replace('\\', '/');
                if (IsMacJunk(path))
                    continue;

                var destination = Destination(targetFolder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using (var source = entry.OpenEntryStream())
                using (var target = File.Create(destination))
                    source.CopyTo(target);

                extracted.Add((path, new FileInfo(destination)));
            }

            // A cut-off download may keep a valid header while the entries are lost.
            if (!rar.Entries.Any())
                throw new InvalidDataException(string.Format(Strings.ArchiveExtractor_NotAnArchive, archive.Name));
        }
        catch (Exception error) when (error is not InvalidDataException and not UnauthorizedAccessException
                                      && (error is not IOException || error is EndOfStreamException))
        {
            // Damaged or truncated archive. Disk errors while writing stay IOExceptions.
            throw new InvalidDataException(string.Format(Strings.ArchiveExtractor_NotAnArchive, archive.Name), error);
        }

        return extracted;
    }

    // Refuses entries that would land outside the target folder ("zip slip").
    private static string Destination(DirectoryInfo targetFolder, string entryPath)
    {
        var root = Path.GetFullPath(targetFolder.FullName + Path.DirectorySeparatorChar);
        var destination = Path.GetFullPath(Path.Combine(targetFolder.FullName, entryPath));

        if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(string.Format(Strings.ArchiveExtractor_SuspiciousPath, entryPath));

        return destination;
    }

    public static bool IsDeviceFile(FileInfo file) =>
        DeviceFileExtensions.Contains(file.Extension.ToLowerInvariant());

    // macOS metadata. An AppleDouble file such as "._firmware.bin" has the firmware extension, so an archive holding
    // only such a file would otherwise pass as an update.
    private static bool IsMacJunk(string entryPath) =>
        entryPath.Split('/', '\\').Any(part =>
            part.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)
            || part.StartsWith("._", StringComparison.Ordinal));

    private static ZipArchive OpenZip(FileInfo archive)
    {
        try
        {
            return ZipFile.OpenRead(archive.FullName);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException(
                string.Format(Strings.ArchiveExtractor_NotAnArchive, archive.Name));
        }
    }

    private static bool StartsWith(FileInfo file, byte[] signature)
    {
        using var stream = file.OpenRead();
        var head = new byte[signature.Length];
        return stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false) == head.Length
            && head.AsSpan().SequenceEqual(signature);
    }
}
