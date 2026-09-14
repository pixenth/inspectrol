using Inspectrol.Core.Cards;

namespace Inspectrol.Tests;

public class CardWriterTests
{
    private const long Gigabyte = 1_000_000_000;

    private static readonly CardSnapshot Card64 = new(64 * Gigabyte, "", "exFAT", 315, "09111429_3157.AVI");

    private sealed class FakeStorage : ICardStorage
    {
        public CardSnapshot Snapshot { get; set; } = Card64;
        public bool Removable { get; set; } = true;
        public bool Empty { get; set; } = true;
        public long Free { get; set; } = 60 * Gigabyte;
        public bool VerifyResult { get; set; } = true;
        public bool EjectResult { get; set; } = true;
        public Exception? FailOnFormat { get; set; }
        public Exception? FailOnCopy { get; set; }
        public Exception? FailOnDescribe { get; set; }

        public bool Formatted { get; private set; }
        public bool Copied { get; private set; }
        public CardFileSystem? FormattedAs { get; private set; }

        public CardSnapshot Describe(DirectoryInfo root) =>
            FailOnDescribe is null ? Snapshot : throw FailOnDescribe;

        public bool IsRemovable(DirectoryInfo root) => Removable;

        public bool IsEmpty(DirectoryInfo root) => Empty;

        public long FreeBytes(DirectoryInfo root) => Free;

        public Task FormatAsync(DirectoryInfo root, CardFileSystem fileSystem, CancellationToken cancellationToken)
        {
            if (FailOnFormat is not null)
                throw FailOnFormat;
            Formatted = true;
            FormattedAs = fileSystem;
            return Task.CompletedTask;
        }

        public Task CopyToCardAsync(IReadOnlyList<CardFile> files, DirectoryInfo root, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            if (FailOnCopy is not null)
                throw FailOnCopy;
            Copied = true;
            progress?.Report(1);
            return Task.CompletedTask;
        }

        public Task<bool> VerifyAsync(IReadOnlyList<CardFile> files, DirectoryInfo root, CancellationToken cancellationToken) =>
            Task.FromResult(VerifyResult);

        public Task<bool> TryEjectAsync(DirectoryInfo root, CancellationToken cancellationToken) =>
            Task.FromResult(EjectResult);
    }

    private static readonly DirectoryInfo Card = new(@"F:\");

    // Real files, because the writer checks that every file exists.
    private static IReadOnlyList<CardFile> RealFiles(params string[] names)
    {
        var folder = Directory.CreateTempSubdirectory();
        return [.. names.Select(name =>
        {
            var path = Path.Combine(folder.FullName, name);
            File.WriteAllBytes(path, new byte[1024]);
            return CardFile.InRoot(new FileInfo(path));
        })];
    }

    private static Task<CardWriteResult> Write(
        FakeStorage storage,
        IReadOnlyList<CardFile>? files = null,
        CardSnapshot? confirmed = null,
        string model = "Inspector Barracuda") =>
        new CardWriter(storage).WriteAsync(
            files ?? RealFiles("BarracudaFW.BRN"), Card, confirmed ?? storage.Snapshot, model, null, CancellationToken.None);

    [Fact]
    public async Task Writes_the_card_and_ejects_it()
    {
        var storage = new FakeStorage();

        var result = await Write(storage);

        Assert.True(result.Success);
        Assert.Equal(CardWriteStage.Ejected, result.Stage);
        Assert.True(storage.Formatted);
        Assert.True(storage.Copied);
        Assert.Equal(CardFileSystem.ExFat, storage.FormattedAs);
    }

    [Fact]
    public async Task Small_card_is_formatted_as_fat32()
    {
        var storage = new FakeStorage { Snapshot = new(16 * Gigabyte, "", "FAT32", 0, "") };

        await Write(storage);

        Assert.Equal(CardFileSystem.Fat32, storage.FormattedAs);
    }

    [Fact]
    public async Task Never_formats_a_disk_that_is_not_removable()
    {
        var storage = new FakeStorage { Removable = false };

        var result = await Write(storage);

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.False(result.CardAlreadyErased);
        Assert.Contains("не съёмный", result.Problem!);
    }

    [Fact]
    public async Task Refuses_when_the_card_was_swapped_after_the_confirmation()
    {
        var storage = new FakeStorage();

        var result = await Write(storage, confirmed: new CardSnapshot(32 * Gigabyte, "ФОТО", "FAT32", 0, ""));

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.Contains("не та", result.Problem!);
    }

    [Fact]
    public async Task Notices_a_swap_between_two_cards_of_the_same_size()
    {
        var storage = new FakeStorage { Snapshot = Card64 with { RecordingCount = 190, NewestRecording = "09121403_7523.MP4" } };

        var result = await Write(storage, confirmed: Card64);

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.Contains("не та", result.Problem!);
    }

    [Fact]
    public async Task Refuses_a_card_the_model_does_not_accept()
    {
        var storage = new FakeStorage();

        var result = await Write(storage, model: "Inspector Star Air");

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.Contains("32", result.Problem!);
    }

    [Fact]
    public async Task Refuses_when_the_card_disappeared_before_formatting()
    {
        var storage = new FakeStorage { FailOnDescribe = new IOException("нет карты") };

        var result = await Write(storage);

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.Contains("пропала", result.Problem!);
    }

    [Fact]
    public async Task Never_formats_when_a_file_is_missing()
    {
        var missing = new FileInfo(Path.Combine(Path.GetTempPath(), $"нет-{Guid.NewGuid():N}.BRN"));

        var storage = new FakeStorage();
        var result = await Write(storage, files: [CardFile.InRoot(missing)]);

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.Contains("не найден", result.Problem!);
    }

    [Fact]
    public async Task Never_formats_when_a_file_is_empty()
    {
        var folder = Directory.CreateTempSubdirectory();
        var empty = Path.Combine(folder.FullName, "firmware.bin");
        File.WriteAllBytes(empty, []);

        var storage = new FakeStorage();
        var result = await Write(storage, files: [CardFile.InRoot(new FileInfo(empty))]);

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.Contains("пустой", result.Problem!);
    }

    [Fact]
    public async Task Never_formats_when_two_files_share_a_name()
    {
        // One pass can include several archives, and all their files go into the card root.
        var first = RealFiles("firmware.bin")[0];
        var second = RealFiles("firmware.bin")[0];

        var storage = new FakeStorage();
        var result = await Write(storage, files: [first, second]);

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
        Assert.Contains("одинаково", result.Problem!);
    }

    [Fact]
    public async Task Says_the_card_may_be_erased_when_formatting_itself_breaks()
    {
        var storage = new FakeStorage { FailOnFormat = new IOException("карту вынули") };

        var result = await Write(storage);

        Assert.False(result.Success);
        Assert.True(result.CardAlreadyErased);
        Assert.Equal(CardWriteStage.Formatting, result.Stage);
    }

    [Fact]
    public async Task Reports_that_administrator_rights_are_needed_as_a_separate_fact()
    {
        var storage = new FakeStorage { FailOnFormat = new CardNeedsAdministratorException("Windows не дала очистить карту") };

        var result = await Write(storage);

        Assert.False(result.Success);
        Assert.True(result.NeedsAdministrator);
        Assert.DoesNotContain("переключатель", result.Problem!);
    }

    [Fact]
    public async Task Explains_the_lock_switch_when_the_card_is_read_only()
    {
        var storage = new FakeStorage { FailOnCopy = new UnauthorizedAccessException() };

        var result = await Write(storage);

        Assert.False(result.Success);
        Assert.False(result.NeedsAdministrator);
        Assert.Contains("переключатель", result.Problem!);
    }

    [Fact]
    public async Task Says_plainly_that_the_card_is_already_empty_when_writing_breaks()
    {
        var storage = new FakeStorage { FailOnCopy = new IOException("карту вынули") };

        var result = await Write(storage);

        Assert.False(result.Success);
        Assert.True(result.CardAlreadyErased);
        Assert.True(result.FilesPartlyWritten);
        Assert.Contains("связь с картой пропала", result.Problem!);
    }

    [Fact]
    public async Task Keeps_the_technical_reason_out_of_the_problem_text()
    {
        var storage = new FakeStorage { FailOnCopy = new IOException("Устройство не готово.") };

        var result = await Write(storage);

        Assert.Equal("Устройство не готово.", result.Details);
        Assert.DoesNotContain("Устройство не готово", result.Problem!);
    }

    [Fact]
    public async Task Warns_when_the_written_files_do_not_match()
    {
        var storage = new FakeStorage { VerifyResult = false };

        var result = await Write(storage);

        Assert.False(result.Success);
        Assert.True(result.CardAlreadyErased);
        Assert.True(result.FilesPartlyWritten);
        Assert.Contains("не полностью", result.Problem!);
    }

    [Fact]
    public async Task Reports_partly_written_files_when_writing_without_erasing_breaks()
    {
        var storage = new FakeStorage { FailOnCopy = new IOException("карту вынули") };

        var result = await new CardWriter(storage).WriteWithoutErasingAsync(
            RealFiles("BarracudaFW.BRN"), Card, storage.Snapshot, "Inspector Barracuda", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.CardAlreadyErased);
        Assert.True(result.FilesPartlyWritten);
    }

    [Fact]
    public async Task Succeeds_even_if_the_card_cannot_be_ejected()
    {
        var storage = new FakeStorage { EjectResult = false };

        var result = await Write(storage);

        Assert.True(result.Success);
        Assert.Equal(CardWriteStage.Verified, result.Stage);
    }

    [Fact]
    public async Task Refuses_an_empty_list_of_files()
    {
        var storage = new FakeStorage();

        var result = await new CardWriter(storage).WriteAsync(
            [], Card, storage.Snapshot, "Inspector Barracuda", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(storage.Formatted);
    }

    [Fact]
    public async Task Writes_empty_marker_files_inside_a_folder()
    {
        // eMap maps come with an empty "reinstall.dat" marker in the "eMap" folder.
        var marker = new FileInfo(Path.Combine(Directory.CreateTempSubdirectory().FullName, "reinstall.dat"));
        File.WriteAllBytes(marker.FullName, []);
        var storage = new FakeStorage();

        var result = await Write(storage, files: [.. RealFiles("firmware.bin"), new CardFile(marker, "eMap/reinstall.dat")]);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Refuses_two_files_with_the_same_path_on_the_card()
    {
        var storage = new FakeStorage();
        var map = RealFiles("map.kdb")[0].Source;

        var result = await Write(storage, files: [new CardFile(map, "eMap/map.kdb"), new CardFile(map, "eMap/MAP.kdb")]);

        Assert.False(result.Success);
        Assert.Contains("одинаково", result.Problem!);
    }

    private static Task<CardWriteResult> WriteWithoutErasing(
        FakeStorage storage, IReadOnlyList<CardFile>? files = null) =>
        new CardWriter(storage).WriteWithoutErasingAsync(
            files ?? RealFiles("BarracudaFW.BRN"), Card, storage.Snapshot, "Inspector Barracuda", null, CancellationToken.None);

    [Fact]
    public async Task Writes_without_erasing_and_leaves_the_card_intact()
    {
        var storage = new FakeStorage();

        var result = await WriteWithoutErasing(storage);

        Assert.True(result.Success);
        Assert.False(storage.Formatted);
        Assert.True(storage.Copied);
        Assert.False(result.CardAlreadyErased);
    }

    [Fact]
    public async Task Refuses_to_write_without_erasing_when_there_is_no_room()
    {
        var storage = new FakeStorage { Free = 100 };

        var result = await WriteWithoutErasing(storage);

        Assert.False(result.Success);
        Assert.False(storage.Copied);
        Assert.False(result.CardAlreadyErased);
        Assert.Contains("не хватает места", result.Problem!);
    }

    [Fact]
    public async Task Writing_without_erasing_never_touches_a_fixed_disk()
    {
        var storage = new FakeStorage { Removable = false };

        var result = await WriteWithoutErasing(storage);

        Assert.False(result.Success);
        Assert.False(storage.Copied);
    }

    private static Task<CardWriteResult> WritePrepared(FakeStorage storage, long? expected = null) =>
        new CardWriter(storage).WriteToPreparedCardAsync(
            RealFiles("BarracudaFW.BRN"), Card, expected ?? storage.Snapshot.TotalBytes,
            "Inspector Barracuda", null, CancellationToken.None);

    [Fact]
    public async Task Writes_to_a_card_the_person_formatted_themselves()
    {
        var storage = new FakeStorage { Snapshot = Card64 with { RecordingCount = 0, NewestRecording = "" } };

        var result = await WritePrepared(storage);

        Assert.True(result.Success);
        Assert.False(storage.Formatted);
        Assert.True(storage.Copied);
    }

    [Fact]
    public async Task Prepared_path_accepts_a_slightly_different_volume_size()
    {
        var storage = new FakeStorage { Snapshot = Card64 with { TotalBytes = 64 * Gigabyte - 900_000_000, RecordingCount = 0, NewestRecording = "" } };

        var result = await WritePrepared(storage, expected: 64 * Gigabyte);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Prepared_path_refuses_a_card_that_was_not_cleared()
    {
        var storage = new FakeStorage { Empty = false };

        var result = await WritePrepared(storage);

        Assert.False(result.Success);
        Assert.Contains("не отформатирована", result.Problem!);
    }

    [Fact]
    public async Task Prepared_path_refuses_the_wrong_file_system()
    {
        // Windows Explorer often offers NTFS for a removable drive, and the dashcam cannot read an NTFS card.
        var storage = new FakeStorage { Snapshot = Card64 with { FileSystem = "NTFS", RecordingCount = 0, NewestRecording = "" } };

        var result = await WritePrepared(storage);

        Assert.False(result.Success);
        Assert.Contains("exFAT", result.Problem!);
    }

    [Fact]
    public async Task Prepared_path_still_refuses_a_different_card()
    {
        var storage = new FakeStorage { Snapshot = Card64 with { RecordingCount = 0, NewestRecording = "" } };

        var result = await WritePrepared(storage, expected: 32 * Gigabyte);

        Assert.False(result.Success);
        Assert.False(storage.Copied);
        Assert.Contains("не та", result.Problem!);
    }
}
