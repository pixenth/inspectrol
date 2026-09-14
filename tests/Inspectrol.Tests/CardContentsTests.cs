using Inspectrol.Core.Devices;

namespace Inspectrol.Tests;

public class CardContentsTests
{
    private static DirectoryInfo Card(params string[] entries)
    {
        var root = Directory.CreateTempSubdirectory();
        foreach (var entry in entries)
        {
            var path = Path.Combine(root.FullName, entry);
            if (entry.EndsWith('/'))
                Directory.CreateDirectory(path);
            else
                File.WriteAllBytes(path, [1, 2, 3]);
        }
        return root;
    }

    [Fact]
    public void Empty_card_is_empty() =>
        Assert.Equal(CardContentKind.Empty, CardContents.Classify(Card()));

    [Fact]
    public void Card_with_recording_folders_holds_recordings() =>
        Assert.Equal(CardContentKind.Recordings, CardContents.Classify(Card("DCIM/", "EVENT/")));

    [Fact]
    public void Card_with_only_update_files_was_prepared_for_an_update()
    {
        // A real AtlaS card after the app has erased it and written an update:
        // only firmware and database files in the root.
        var card = Card("firmware.bin", "firmware2.bin", "rdfw.bin", "AtlaSDB.bin");

        Assert.Equal(CardContentKind.UpdateFiles, CardContents.Classify(card));
    }

    [Fact]
    public void Card_with_emap_maps_was_prepared_for_an_update()
    {
        var card = Card("firmware.bin", "eMap/");

        Assert.Equal(CardContentKind.UpdateFiles, CardContents.Classify(card));
    }

    [Fact]
    public void Card_with_photos_holds_other_files() =>
        Assert.Equal(CardContentKind.OtherFiles, CardContents.Classify(Card("IMG_0001.JPG")));

    [Fact]
    public void Update_files_next_to_a_foreign_folder_are_other_files()
    {
        var card = Card("firmware.bin", "Фото/");

        Assert.Equal(CardContentKind.OtherFiles, CardContents.Classify(card));
    }
}
