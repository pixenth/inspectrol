using Inspectrol.Core.Devices;

namespace Inspectrol.Tests;

public class RecordingsBackupTests
{
    private static FileInfo Recording(DirectoryInfo folder, string name, DateTime written, int bytes = 1024)
    {
        var path = Path.Combine(folder.FullName, name);
        File.WriteAllBytes(path, new byte[bytes]);
        File.SetLastWriteTime(path, written);
        return new FileInfo(path);
    }

    private static readonly DateTime Day1 = new(2026, 9, 10, 8, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Day2 = new(2026, 9, 12, 9, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Day2Later = new(2026, 9, 12, 19, 30, 0, DateTimeKind.Local);

    [Fact]
    public void Summarises_count_size_and_dates()
    {
        var folder = Directory.CreateTempSubdirectory();
        List<FileInfo> files =
        [
            Recording(folder, "a.MP4", Day1, 2000),
            Recording(folder, "b.MP4", Day2, 3000),
        ];

        var summary = RecordingsBackup.Summarise(files);

        Assert.Equal(2, summary.Count);
        Assert.Equal(5000, summary.TotalBytes);
        Assert.Equal(Day1.Date, summary.Oldest!.Value.Date);
        Assert.Equal(Day2.Date, summary.Newest!.Value.Date);
    }

    [Fact]
    public void Empty_card_summarises_to_nothing()
    {
        var summary = RecordingsBackup.Summarise([]);

        Assert.Equal(0, summary.Count);
        Assert.Equal(0, summary.TotalBytes);
        Assert.Null(summary.Oldest);
        Assert.Null(summary.Newest);
    }

    [Fact]
    public void Last_day_takes_every_recording_of_the_newest_day()
    {
        var folder = Directory.CreateTempSubdirectory();
        List<FileInfo> files =
        [
            Recording(folder, "a.MP4", Day1),
            Recording(folder, "b.MP4", Day2),
            Recording(folder, "c.MP4", Day2Later),
        ];

        var lastDay = RecordingsBackup.LastDay(files);

        Assert.Equal(2, lastDay.Count);
        Assert.DoesNotContain(lastDay, file => file.Name == "a.MP4");
    }

    [Fact]
    public void Last_day_ignores_a_recording_with_a_reset_clock()
    {
        // A real AtlaS card held a recording dated 2020-01-01 because the dashcam clock had reset.
        // That file is the oldest one, so it must not move the last day.
        var folder = Directory.CreateTempSubdirectory();
        List<FileInfo> files =
        [
            Recording(folder, "сбитые-часы.MP4", new DateTime(2020, 1, 1, 0, 3, 0, DateTimeKind.Local)),
            Recording(folder, "свежая.MP4", Day2),
        ];

        var lastDay = RecordingsBackup.LastDay(files);

        Assert.Equal("свежая.MP4", Assert.Single(lastDay).Name);
    }

    [Fact]
    public void Between_takes_whole_days_inclusive()
    {
        var folder = Directory.CreateTempSubdirectory();
        List<FileInfo> files =
        [
            Recording(folder, "a.MP4", Day1),
            Recording(folder, "b.MP4", Day2Later),
        ];

        var chosen = RecordingsBackup.Between(files, Day1.Date, Day2.Date);

        Assert.Equal(2, chosen.Count);
    }

    [Fact]
    public async Task Copies_files_and_reports_progress()
    {
        var folder = Directory.CreateTempSubdirectory();
        var target = Directory.CreateTempSubdirectory();
        List<FileInfo> files = [Recording(folder, "a.MP4", Day1), Recording(folder, "b.MP4", Day2)];
        var reported = new List<double>();

        await RecordingsBackup.CopyAsync(files, target, new Progress<double>(reported.Add), CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(target.FullName, "a.MP4")));
        Assert.True(File.Exists(Path.Combine(target.FullName, "b.MP4")));
        Assert.NotEmpty(reported);
        Assert.True(reported[^1] >= 0.99, $"последний прогресс {reported[^1]}");
    }

    [Fact]
    public async Task Does_not_lose_recordings_with_the_same_name()
    {
        // File names repeat across folders on a card: they carry no year, and the counter wraps around.
        var first = Directory.CreateTempSubdirectory();
        var second = Directory.CreateTempSubdirectory();
        var target = Directory.CreateTempSubdirectory();
        List<FileInfo> files = [Recording(first, "06151456_3758.MP4", Day1), Recording(second, "06151456_3758.MP4", Day2)];

        await RecordingsBackup.CopyAsync(files, target, null, CancellationToken.None);

        Assert.Equal(2, target.GetFiles().Length);
    }

    [Fact]
    public async Task Refuses_to_copy_when_there_is_no_room()
    {
        var folder = Directory.CreateTempSubdirectory();
        List<FileInfo> files = [Recording(folder, "a.MP4", Day1)];

        var error = await Assert.ThrowsAsync<IOException>(
            () => RecordingsBackup.CopyAsync(files, Directory.CreateTempSubdirectory(), null,
                CancellationToken.None, requiredFreeBytesOverride: long.MaxValue));

        Assert.Contains("места", error.Message);
    }

    [Fact]
    public void Accepts_a_copy_of_the_same_size()
    {
        var folder = Directory.CreateTempSubdirectory();
        var original = Recording(folder, "a.MP4", Day1, 2048);
        var copy = Recording(Directory.CreateTempSubdirectory(), "a.MP4", Day1, 2048);

        RecordingsBackup.EnsureCopyIsComplete(original.FullName, copy.FullName);

        Assert.True(File.Exists(copy.FullName));
    }

    [Fact]
    public void Refuses_a_copy_that_came_out_short_and_removes_it()
    {
        var folder = Directory.CreateTempSubdirectory();
        var original = Recording(folder, "a.MP4", Day1, 2048);
        var copy = Recording(Directory.CreateTempSubdirectory(), "a.MP4", Day1, 1024);

        var error = Assert.Throws<IOException>(
            () => RecordingsBackup.EnsureCopyIsComplete(original.FullName, copy.FullName));

        Assert.Contains("a.MP4", error.Message);
        Assert.Contains("не целиком", error.Message);
        Assert.False(File.Exists(copy.FullName));
    }
}
