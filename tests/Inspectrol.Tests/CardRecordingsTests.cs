using Inspectrol.Core.Devices;

namespace Inspectrol.Tests;

public class CardRecordingsTests
{
    private static DirectoryInfo Card(params (string Path, DateTime Written)[] files)
    {
        var root = Directory.CreateTempSubdirectory();
        foreach (var (path, written) in files)
        {
            var full = Path.Combine(root.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, [1, 2, 3]);
            File.SetLastWriteTimeUtc(full, written);
        }
        return root;
    }

    private static readonly DateTime Old = new(2024, 6, 14, 16, 35, 0, DateTimeKind.Utc);
    private static readonly DateTime Recent = new(2026, 9, 12, 14, 3, 0, DateTimeKind.Utc);

    [Fact]
    public void Finds_recordings_in_all_three_folders()
    {
        var card = Card(
            ("DCIM/104MEDIA/09121403_7523.MP4", Recent),
            ("EVENT/100MEDIA/06141632_7973.MP4", Old),
            ("PARKING/104MEDIA/09121337_2465.MP4", Old));

        Assert.Equal(3, CardRecordings.Find(card).Count);
    }

    [Fact]
    public void Newest_recording_comes_first()
    {
        var card = Card(
            ("EVENT/100MEDIA/06141632_7973.MP4", Old),
            ("DCIM/104MEDIA/09121403_7523.MP4", Recent));

        Assert.Equal("09121403_7523.MP4", CardRecordings.Find(card)[0].Name);
    }

    [Fact]
    public void Skips_leftovers_from_a_mac()
    {
        // A real Sparta card held the macOS leftover "._08111658_8558.MP4", which is not a recording.
        var card = Card(
            ("DCIM/100MEDIA/._08111658_8558.MP4", Recent),
            ("DCIM/101MEDIA/06151456_3758.MP4", Old));

        Assert.Equal("06151456_3758.MP4", Assert.Single(CardRecordings.Find(card)).Name);
    }

    [Fact]
    public void Skips_service_folders()
    {
        var card = Card(
            ("System Volume Information/WPSettings.dat", Recent),
            (".Spotlight-V100/store.db", Recent),
            ("DCIM/101MEDIA/06151456_3758.MP4", Old));

        Assert.Single(CardRecordings.Find(card));
    }

    [Fact]
    public void Ignores_files_that_are_not_recordings()
    {
        var card = Card(
            ("DCIM/101MEDIA/readme.txt", Recent),
            ("DCIM/101MEDIA/06151456_3758.MP4", Old));

        Assert.Single(CardRecordings.Find(card));
    }

    [Fact]
    public void Returns_nothing_for_a_card_without_recordings()
    {
        Assert.Empty(CardRecordings.Find(Directory.CreateTempSubdirectory()));
    }

    [Fact]
    public void Returns_nothing_for_a_missing_folder()
    {
        var missing = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"inspectrol-{Guid.NewGuid():N}"));

        Assert.Empty(CardRecordings.Find(missing));
    }

    [Fact]
    public void Finds_recordings_of_a_barracuda_card()
    {
        // Barracuda writes AVI files straight into the VIDEO folder, without subfolders.
        var card = Card(("VIDEO/09111429_3157.AVI", Recent));

        var found = CardRecordings.Find(card);

        Assert.Equal("09111429_3157.AVI", Assert.Single(found).Name);
    }

    [Fact]
    public void Mixes_folders_and_formats_from_different_generations()
    {
        var card = Card(
            ("VIDEO/09111429_3157.AVI", Old),
            ("DCIM/101MEDIA/06151456_3758.MP4", Recent));

        var found = CardRecordings.Find(card);

        Assert.Equal(2, found.Count);
        Assert.Equal("06151456_3758.MP4", found[0].Name);
    }
}
