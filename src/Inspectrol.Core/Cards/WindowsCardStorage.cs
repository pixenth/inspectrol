using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Inspectrol.Core.Devices;

namespace Inspectrol.Core.Cards;

public sealed class WindowsCardStorage : ICardStorage
{
    private const int CopyBufferBytes = 1024 * 1024;

    public CardSnapshot Describe(DirectoryInfo root)
    {
        var drive = new DriveInfo(root.FullName);

        var recordings = CardRecordings.Find(root);

        return new CardSnapshot(
            drive.TotalSize,
            drive.VolumeLabel ?? "",
            drive.DriveFormat ?? "",
            recordings.Count,
            recordings.Count > 0 ? recordings[0].Name : "");
    }

    public bool IsRemovable(DirectoryInfo root)
    {
        try
        {
            // The whole volume gets formatted, so a volume mounted into a folder is never accepted.
            if (!IsDriveRoot(root))
                return false;

            var drive = new DriveInfo(root.FullName);

            // The system drive is never removable anyway, but a mistake here would erase all of the user's files.
            var system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            if (string.Equals(drive.RootDirectory.FullName, system, StringComparison.OrdinalIgnoreCase))
                return false;

            return drive.DriveType == DriveType.Removable && drive.IsReady;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public bool IsEmpty(DirectoryInfo root)
    {
        try
        {
            // Windows creates this folder right after formatting.
            return !root.EnumerateFileSystemInfos()
                .Any(entry => !entry.Name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public long FreeBytes(DirectoryInfo root)
    {
        try
        {
            return new DriveInfo(root.FullName).AvailableFreeSpace;
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    public async Task FormatAsync(DirectoryInfo root, CardFileSystem fileSystem, CancellationToken cancellationToken)
    {
        if (!IsRemovable(root))
            throw new CardNotRemovableException(string.Format(Strings.WindowsCardStorage_NotRemovable, root.Name));

        var letter = DriveLetter(root);
        var name = fileSystem == CardFileSystem.Fat32 ? "FAT32" : "exFAT";

        var arguments = "-NoProfile -NonInteractive -Command "
            + $"\"Format-Volume -DriveLetter {letter} -FileSystem {name} -Force -Confirm:$false -ErrorAction Stop\"";

        using var process = Process.Start(new ProcessStartInfo("powershell.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        }) ?? throw new IOException(Strings.WindowsCardStorage_FormatNotStarted);

        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode == 0)
            return;

        // Format-Volume usually needs administrator rights, and the app runs as a regular user.
        if (LooksLikeDenied(error))
            throw new CardNeedsAdministratorException(Strings.WindowsCardStorage_NeedsAdministrator);

        throw new IOException(string.Format(Strings.WindowsCardStorage_FormatFailed, error.Trim()));
    }

    // PowerShell error text differs between English and Russian Windows.
    private static bool LooksLikeDenied(string error) =>
        error.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Отказано в доступе", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
        || error.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase)
        || error.Contains("Требуется повышение прав", StringComparison.OrdinalIgnoreCase)
        || error.Contains("elevated", StringComparison.OrdinalIgnoreCase);

    public async Task CopyToCardAsync(
        IReadOnlyList<CardFile> files,
        DirectoryInfo root,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        long copied = 0;
        var total = Math.Max(1, files.Sum(file => file.Length));
        var buffer = new byte[CopyBufferBytes];

        foreach (var file in files)
        {
            var destination = file.PathOn(root);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using var source = file.Source.OpenRead();
            await using var target = new FileStream(
                destination, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferBytes,
                // Bypass the system cache so completion is not reported before the data is on the card.
                FileOptions.WriteThrough);

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                progress?.Report((double)copied / total);
            }

            await target.FlushAsync(cancellationToken);
        }

        progress?.Report(1);
    }

    public async Task<bool> VerifyAsync(IReadOnlyList<CardFile> files, DirectoryInfo root, CancellationToken cancellationToken)
    {
        foreach (var file in files)
        {
            var written = new FileInfo(file.PathOn(root));
            if (!written.Exists || written.Length != file.Length)
                return false;

            if (!await SameContentAsync(file.Source, written, cancellationToken))
                return false;
        }

        return true;
    }

    private static async Task<bool> SameContentAsync(FileInfo left, FileInfo right, CancellationToken cancellationToken)
    {
        await using var leftStream = left.OpenRead();
        await using var rightStream = right.OpenRead();

        var leftHash = await SHA256.HashDataAsync(leftStream, cancellationToken);
        var rightHash = await SHA256.HashDataAsync(rightStream, cancellationToken);

        return leftHash.AsSpan().SequenceEqual(rightHash);
    }

    // Lock, dismount and eject need no administrator rights on a removable drive.
    public Task<bool> TryEjectAsync(DirectoryInfo root, CancellationToken cancellationToken)
    {
        try
        {
            using var volume = CreateFile(
                $@"\\.\{DriveLetter(root)}:",
                GenericRead | GenericWrite,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                0,
                IntPtr.Zero);

            if (volume.IsInvalid)
                return Task.FromResult(false);

            return Task.FromResult(
                Control(volume, FsctlLockVolume)
                && Control(volume, FsctlDismountVolume)
                && Control(volume, IoctlStorageEjectMedia));
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
        {
            return Task.FromResult(false);
        }
    }

    private static bool IsDriveRoot(DirectoryInfo root)
    {
        var full = root.FullName;
        return full.Length is 3
            && char.IsLetter(full[0])
            && full[1] == ':'
            && (full[2] == Path.DirectorySeparatorChar || full[2] == Path.AltDirectorySeparatorChar);
    }

    private static char DriveLetter(DirectoryInfo root) => char.ToUpperInvariant(root.FullName[0]);

    private static bool Control(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code) =>
        DeviceIoControl(handle, code, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero);

    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlDismountVolume = 0x00090020;
    private const uint IoctlStorageEjectMedia = 0x002D4808;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string fileName, uint access, uint share, IntPtr security,
        uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        Microsoft.Win32.SafeHandles.SafeFileHandle device, uint code,
        IntPtr inBuffer, uint inSize, IntPtr outBuffer, uint outSize,
        out uint returned, IntPtr overlapped);
}
