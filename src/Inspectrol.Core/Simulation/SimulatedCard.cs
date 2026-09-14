using Inspectrol.Core.Devices;
using Inspectrol.Core.Downloading;
using Inspectrol.Core.Formatting;

namespace Inspectrol.Core.Simulation;

public enum SimulatedDashcam { AtlaS, Sparta, Barracuda }

public enum SimulatedContent { Recordings, Empty, UpdateFiles, Photos }

/// <remarks>
/// While the card is out of the computer its folder is moved aside, so code that checks the path sees it disappear
/// the same way a removed drive does.
/// </remarks>
public sealed class SimulatedCard
{
    private const long Gigabyte = 1_000_000_000;
    private const int ClipBytes = 1024 * 1024;

    private readonly DirectoryInfo _outside;
    private readonly DirectoryInfo _secondDrive;
    private volatile bool _inserted;
    private int _nextClip = 1001;

    private SimulatedCard(DirectoryInfo home)
    {
        Home = home;
        Root = new DirectoryInfo(Path.Combine(home.FullName, "F"));
        _outside = new DirectoryInfo(Path.Combine(home.FullName, "F.removed"));
        _secondDrive = new DirectoryInfo(Path.Combine(home.FullName, "E"));
    }

    // Raised on any thread.
    public event EventHandler? Changed;

    public DirectoryInfo Home { get; }

    public DirectoryInfo Root { get; }

    // Wherever the card is now.
    public DirectoryInfo Contents => new(_inserted ? Root.FullName : _outside.FullName);

    public bool Inserted => _inserted;

    public long TotalBytes { get; set; } = 128 * Gigabyte;

    public string FileSystem { get; set; } = "exFAT";

    public SimulatedDashcam Dashcam { get; set; }

    public Version Firmware { get; set; } = new(1, 0, 3);

    public bool NeedsAdministrator { get; set; }

    public bool AlmostFull { get; set; }

    public bool SecondDriveConnected { get; set; }

    public bool RealDownloads { get; set; }

    public static SimulatedCard Create(DirectoryInfo parent)
    {
        parent.Create();
        foreach (var old in parent.EnumerateDirectories())
        {
            try
            {
                old.Delete(recursive: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // Still in use by another simulator window.
            }
        }

        var card = new SimulatedCard(parent.CreateSubdirectory(DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        WritePhotos(card._secondDrive.FullName);
        card.Prepare(SimulatedContent.Recordings);
        return card;
    }

    public static string ModelName(SimulatedDashcam dashcam) => dashcam switch
    {
        SimulatedDashcam.Sparta => "Inspector Sparta",
        SimulatedDashcam.Barracuda => "Inspector Barracuda",
        _ => "Inspector AtlaS",
    };

    private string ModelTag => Dashcam switch
    {
        SimulatedDashcam.Sparta => "SPARTA",
        SimulatedDashcam.Barracuda => "BARRACUDA",
        _ => "AtlaS",
    };

    public void Prepare(SimulatedContent content)
    {
        if (_inserted)
            throw new InvalidOperationException("The card contents can only be replaced while the card is out.");

        if (Directory.Exists(_outside.FullName))
            Directory.Delete(_outside.FullName, recursive: true);
        Directory.CreateDirectory(_outside.FullName);
        FileSystem = DefaultFileSystem();

        switch (content)
        {
            case SimulatedContent.Recordings:
                for (var day = 2; day >= 0; day--)
                    AddRecordings(_outside.FullName, 10, DateTime.Now.AddDays(-day).AddHours(-1));
                break;

            case SimulatedContent.UpdateFiles:
                foreach (var (name, bytes) in UpdateFiles())
                    WriteRandom(Path.Combine(_outside.FullName, name), bytes);
                break;

            case SimulatedContent.Photos:
                WritePhotos(_outside.FullName);
                break;
        }

        OnChanged();
    }

    public Task InsertAsync() => MoveAsync(insert: true);

    public Task RemoveAsync() => MoveAsync(insert: false);

    // A trip to the car: the dashcam installs firmware from the card and records a few clips.
    public void RunInDashcam(bool installsUpdate, Version? firmwareOnCard)
    {
        if (_inserted)
            throw new InvalidOperationException("The card must be out of the computer.");

        Directory.CreateDirectory(_outside.FullName);

        if (installsUpdate && firmwareOnCard is not null && HasFirmwareFiles(_outside))
            Firmware = firmwareOnCard;

        AddRecordings(_outside.FullName, 3, DateTime.Now);
        OnChanged();
    }

    public void FormatByHand()
    {
        foreach (var entry in Contents.EnumerateFileSystemInfos())
            Delete(entry);

        FileSystem = DefaultFileSystem();
        OnChanged();
    }

    public IReadOnlyList<RemovableCard> FindCards()
    {
        var cards = new List<RemovableCard>();

        if (_inserted)
            cards.Add(new RemovableCard(Root, "", TotalBytes, FreeBytes(), LooksLikeDashcam()));

        if (SecondDriveConnected)
            cards.Add(new RemovableCard(_secondDrive, "PHOTO", 16 * Gigabyte, 12 * Gigabyte, LooksLikeDashcam: false));

        return cards;
    }

    public long FreeBytes()
    {
        if (AlmostFull)
            return 50_000_000;

        try
        {
            return TotalBytes - Contents.EnumerateFiles("*", SearchOption.AllDirectories).Sum(file => file.Length);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return TotalBytes;
        }
    }

    public string Summary()
    {
        var dashcam = string.Format(Strings.Simulator_SummaryDashcam, ModelName(Dashcam), Firmware);

        try
        {
            var entries = Contents.Exists ? Contents.EnumerateFileSystemInfos().ToList() : [];
            var recordings = Contents.Exists ? CardRecordings.Find(Contents).Count : 0;
            var updates = entries.Count(IsUpdate);
            var others = entries.Count(entry => !IsUpdate(entry)
                && !CardRecordings.RecordingFolders.Contains(entry.Name, StringComparer.OrdinalIgnoreCase));

            var parts = new List<string>();
            if (recordings > 0)
                parts.Add(RussianPlural.Format(recordings, Strings.Common_RecordingForms));
            if (updates > 0)
                parts.Add(string.Format(Strings.Simulator_SummaryUpdateFiles, updates));
            if (others > 0)
                parts.Add(string.Format(Strings.Simulator_SummaryOtherFiles, others));

            var contents = parts.Count == 0
                ? Strings.Simulator_SummaryEmpty
                : string.Format(Strings.Simulator_Summary, string.Join(", ", parts));

            return contents + Environment.NewLine + dashcam;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return dashcam;
        }
    }

    private async Task MoveAsync(bool insert)
    {
        if (_inserted == insert)
            return;

        var (from, to) = insert ? (_outside, Root) : (Root, _outside);

        // A pulled card is gone for the computer at once, even if a write still holds files open for a moment.
        if (!insert)
        {
            _inserted = false;
            OnChanged();
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.CreateDirectory(from.FullName);
                Directory.Move(from.FullName, to.FullName);
                break;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 40)
            {
                await Task.Delay(250);
            }
        }

        if (insert)
        {
            _inserted = true;
            OnChanged();
        }
    }

    private void AddRecordings(string cardFolder, int count, DateTime newest)
    {
        var barracuda = Dashcam == SimulatedDashcam.Barracuda;
        var folder = barracuda
            ? Path.Combine(cardFolder, "VIDEO")
            : Path.Combine(cardFolder, "DCIM", "100MEDIA");

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(cardFolder, "EVENT"));

        for (var index = 0; index < count; index++)
        {
            var time = newest.AddMinutes(-3 * (count - 1 - index));
            var path = Path.Combine(folder, $"{time:MMddHHmm}_{_nextClip++:0000}{(barracuda ? ".AVI" : ".MP4")}");

            if (barracuda)
                FakeRecordings.WriteAvi(path, ModelTag, ClipBytes);
            else
                FakeRecordings.WriteMp4(path, ModelTag, Firmware, ClipBytes);

            File.SetLastWriteTime(path, time);
        }
    }

    private (string Name, long Bytes)[] UpdateFiles() => Dashcam switch
    {
        SimulatedDashcam.Sparta => [("firmware.bin", 4_000_000), ("SpartaDB.bin", 2_000_000)],
        SimulatedDashcam.Barracuda => [("BarracudaFW.BRN", 4_000_000), ("BarracudaDB.bin", 2_000_000)],
        _ => [("firmware.bin", 4_000_000), ("firmware2.bin", 4_000_000), ("rdfw.bin", 70_000), ("AtlaSDB.bin", 2_000_000)],
    };

    private string DefaultFileSystem() => TotalBytes <= 32 * Gigabyte ? "FAT32" : "exFAT";

    private bool LooksLikeDashcam()
    {
        try
        {
            return CardRecordings.RecordingFolders.Any(folder => Directory.Exists(Path.Combine(Root.FullName, folder)));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasFirmwareFiles(DirectoryInfo folder) =>
        folder.EnumerateFiles().Any(file =>
            ArchiveExtractor.IsDeviceFile(file) && !file.Name.Contains("DB", StringComparison.OrdinalIgnoreCase));

    private static bool IsUpdate(FileSystemInfo entry) => entry switch
    {
        FileInfo file => ArchiveExtractor.IsDeviceFile(file),
        _ => entry.Name.Equals(ArchiveExtractor.MapsFolderName, StringComparison.OrdinalIgnoreCase),
    };

    private static void WritePhotos(string folder)
    {
        for (var number = 1; number <= 6; number++)
            WriteRandom(Path.Combine(folder, "Фото", $"IMG_{number:0000}.JPG"), 300_000);
    }

    internal static void WriteRandom(string path, long bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);

        var buffer = new byte[Math.Min(bytes, 1024 * 1024)];
        for (var left = bytes; left > 0; left -= buffer.Length)
        {
            Random.Shared.NextBytes(buffer);
            stream.Write(buffer, 0, (int)Math.Min(buffer.Length, left));
        }
    }

    internal static void Delete(FileSystemInfo entry)
    {
        if (entry is DirectoryInfo folder)
            folder.Delete(recursive: true);
        else
            entry.Delete();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
