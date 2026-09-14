using Inspectrol.Core.Catalog;
using Inspectrol.Core.Devices;

namespace Inspectrol.Tests;

public class DeviceRecognitionTests
{
    private static readonly IReadOnlyList<CatalogModel> Catalog =
    [
        new("Сигнатурные комбо", "Inspector Atlas", []),
        new("Комбо", "Inspector Barracuda", []),
        new("Комбо", "Inspector Sparta", []),
        new("Комбо", "Inspector Shark", []),
        new("Комбо", "Inspector Shark Lite", []),
    ];

    private static DirectoryInfo Card(params (string Tag, DateTime Written)[] recordings)
    {
        var root = Directory.CreateTempSubdirectory();
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "DCIM", "101MEDIA"));
        var number = 0;
        foreach (var (tag, written) in recordings)
        {
            var source = Mp4Builder.WithTag(tag);
            var target = Path.Combine(folder.FullName, $"0101000{number++}_{number:0000}.MP4");
            File.Move(source.FullName, target);
            File.SetLastWriteTimeUtc(target, written);
        }
        return root;
    }

    private static readonly DateTime Older = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Newer = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Recognises_a_model_by_its_tag()
    {
        var result = DeviceRecognition.Recognise(Card(("EMTOAtlaS_1.0.3", Newer)), Catalog);

        Assert.Null(result.Explanation);
        Assert.Equal("Inspector Atlas", result.Device!.Model.Name);
        Assert.Equal(new Version(1, 0, 3), result.Device.Firmware);
    }

    [Fact]
    public void Takes_the_firmware_from_the_newest_recording()
    {
        // On a real Sparta card older recordings carried 1.0.0 and newer ones 1.0.1.
        var card = Card(("EMTOSPARTA_1.0.0", Older), ("EMTOSPARTA_1.0.1", Newer));

        var result = DeviceRecognition.Recognise(card, Catalog);

        Assert.Equal(new Version(1, 0, 1), result.Device!.Firmware);
    }

    [Fact]
    public void Unverified_model_is_only_a_guess()
    {
        var result = DeviceRecognition.Recognise(Card(("EMTOAtlaS_1.0.3", Newer)), Catalog);

        Assert.Equal(RecognitionConfidence.Guessed, result.Device!.Confidence);
    }

    [Fact]
    public void Refuses_to_choose_when_the_card_holds_two_models()
    {
        // The card was moved between dashcams.
        var card = Card(("EMTOSPARTA_1.0.1", Older), ("EMTOAtlaS_1.0.3", Newer));

        var result = DeviceRecognition.Recognise(card, Catalog);

        Assert.Null(result.Device);
        Assert.Contains("разных", result.Explanation!);
    }

    [Fact]
    public void Explains_a_card_without_recordings()
    {
        var result = DeviceRecognition.Recognise(Directory.CreateTempSubdirectory(), Catalog);

        Assert.Null(result.Device);
        Assert.Contains("записей", result.Explanation!);
    }

    [Fact]
    public void Explains_recordings_without_a_tag()
    {
        var root = Directory.CreateTempSubdirectory();
        var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "DCIM", "101MEDIA"));
        File.Move(Mp4Builder.WithoutTag().FullName, Path.Combine(folder.FullName, "01010000_0001.MP4"));

        var result = DeviceRecognition.Recognise(root, Catalog);

        Assert.Null(result.Device);
        Assert.Contains("узнать по ним регистратор не удалось", result.Explanation!);
    }

    [Fact]
    public void Explains_a_model_that_is_not_on_the_site()
    {
        var result = DeviceRecognition.Recognise(Card(("EMTONEPTUNE_2.0.0", Newer)), Catalog);

        Assert.Null(result.Device);
        Assert.Contains("NEPTUNE", result.Explanation!);
    }

    [Fact]
    public void Does_not_confuse_similar_names()
    {
        // "Shark" and "Shark Lite" are different devices.
        var result = DeviceRecognition.Recognise(Card(("EMTOSHARK_1.0.0", Newer)), Catalog);

        Assert.Equal("Inspector Shark", result.Device!.Model.Name);
    }

    [Fact]
    public void Verified_model_is_marked_as_verified()
    {
        var result = DeviceRecognition.Recognise(
            Card(("EMTOAtlaS_1.0.3", Newer)), Catalog, verifiedModels: ["Inspector Atlas"]);

        Assert.Equal(RecognitionConfidence.Verified, result.Device!.Confidence);
    }

    [Fact]
    public void Verified_list_is_empty_until_a_real_device_is_updated()
    {
        Assert.Empty(DeviceRecognition.VerifiedModels);
    }

    private static DirectoryInfo PreparedCard(params string[] files)
    {
        var root = Directory.CreateTempSubdirectory();
        foreach (var file in files)
            File.WriteAllBytes(Path.Combine(root.FullName, file), [1, 2, 3]);
        return root;
    }

    [Fact]
    public void Recognises_a_card_prepared_for_an_update_by_its_database_file()
    {
        // A real AtlaS card after the app prepared it: erased, no recordings, only update files.
        var card = PreparedCard("firmware.bin", "firmware2.bin", "rdfw.bin", "AtlaSDB.bin");

        var result = DeviceRecognition.Recognise(card, Catalog);

        Assert.Equal("Inspector Atlas", result.Device!.Model.Name);
        Assert.Null(result.Device.Firmware);
        Assert.True(result.Device.FromUpdateFiles);
    }

    [Fact]
    public void Firmware_files_alone_do_not_name_the_model()
    {
        var result = DeviceRecognition.Recognise(PreparedCard("firmware.bin"), Catalog);

        Assert.Null(result.Device);
        Assert.Contains("записей", result.Explanation!);
    }

    [Fact]
    public void Database_file_does_not_confuse_similar_names()
    {
        var result = DeviceRecognition.Recognise(PreparedCard("SharkDB.bin"), Catalog);

        Assert.Equal("Inspector Shark", result.Device!.Model.Name);
    }

    [Fact]
    public void Recordings_outweigh_update_files()
    {
        var card = Card(("EMTOAtlaS_1.0.3", Newer));
        File.WriteAllBytes(Path.Combine(card.FullName, "SpartaDB.bin"), [1, 2, 3]);

        var result = DeviceRecognition.Recognise(card, Catalog);

        Assert.Equal("Inspector Atlas", result.Device!.Model.Name);
        Assert.False(result.Device.FromUpdateFiles);
    }
}
