using Inspectrol.Core.Cards;
using Inspectrol.Core.Catalog;
using Inspectrol.Core.Devices;
using Inspectrol.Core.Simulation;

namespace Inspectrol.Tests;

public class SimulationTests
{
    private static readonly CatalogModel[] Catalog =
    [
        new("Комбо", "Inspector Atlas", []),
        new("Видеорегистраторы", "Inspector Sparta", []),
        new("Комбо", "Inspector Barracuda", []),
    ];

    private static SimulatedCard NewCard() => SimulatedCard.Create(Directory.CreateTempSubdirectory());

    private static SimulatedCardStorage Storage(SimulatedCard card) =>
        new(card) { FormatDuration = TimeSpan.Zero, MinimumCopyDuration = TimeSpan.Zero };

    private static IReadOnlyList<CardFile> AtlaSUpdate(SimulatedCard card)
    {
        var downloads = new SimulatedDownloads(new HttpClient(), card);
        var folder = Directory.CreateTempSubdirectory();
        return
        [
            .. downloads.Extract(new FileInfo("AtlaS_1.0.5+E3.0.zip"), folder),
            .. downloads.Extract(new FileInfo("AtlaSDB_37.zip"), folder),
        ];
    }

    [Fact]
    public void Fake_mp4_carries_the_model_and_firmware_tag()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "09131200_1001.MP4");

        FakeRecordings.WriteMp4(path, "AtlaS", new Version(1, 0, 3), 4096);
        var tag = RecordingTagReader.Read(new FileInfo(path));

        Assert.Equal("AtlaS", tag?.ModelTag);
        Assert.Equal(new Version(1, 0, 3), tag?.Firmware);
    }

    [Fact]
    public void Fake_avi_carries_the_model_without_a_version()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "09131200_1001.AVI");

        FakeRecordings.WriteAvi(path, "BARRACUDA", 4096);
        var tag = RecordingTagReader.Read(new FileInfo(path));

        Assert.Equal("BARRACUDA", tag?.ModelTag);
        Assert.Null(tag?.Firmware);
    }

    [Fact]
    public async Task Card_with_recordings_is_recognised_like_a_real_one()
    {
        var card = NewCard();
        await card.InsertAsync();

        var recognition = DeviceRecognition.Recognise(card.Root, Catalog);

        Assert.True(Assert.Single(card.FindCards()).LooksLikeDashcam);
        Assert.Equal("Inspector Atlas", recognition.Device?.Model.Name);
        Assert.Equal(new Version(1, 0, 3), recognition.Device?.Firmware);
    }

    [Theory]
    [InlineData(SimulatedContent.Empty, CardContentKind.Empty)]
    [InlineData(SimulatedContent.UpdateFiles, CardContentKind.UpdateFiles)]
    [InlineData(SimulatedContent.Photos, CardContentKind.OtherFiles)]
    public async Task Card_contents_are_classified_like_a_real_card(SimulatedContent content, CardContentKind expected)
    {
        var card = NewCard();
        card.Prepare(content);
        await card.InsertAsync();

        Assert.Equal(expected, CardContents.Classify(card.Root));
    }

    [Fact]
    public async Task Removed_card_disappears_from_the_computer()
    {
        var card = NewCard();
        await card.InsertAsync();

        await card.RemoveAsync();

        Assert.Empty(card.FindCards());
        Assert.False(Directory.Exists(card.Root.FullName));
        Assert.False(Storage(card).IsRemovable(card.Root));
    }

    [Fact]
    public async Task Writing_an_update_erases_the_card_and_ejects_it()
    {
        var card = NewCard();
        await card.InsertAsync();
        var storage = Storage(card);

        var result = await new CardWriter(storage).WriteAsync(
            AtlaSUpdate(card), card.Root, storage.Describe(card.Root), "Inspector Atlas", progress: null, CancellationToken.None);

        Assert.True(result.Success, result.Problem);
        Assert.False(card.Inserted);
        Assert.Equal(CardContentKind.UpdateFiles, CardContents.Classify(card.Contents));
    }

    [Fact]
    public async Task Refusal_to_erase_without_administrator_rights_leaves_the_recordings()
    {
        var card = NewCard();
        card.NeedsAdministrator = true;
        await card.InsertAsync();
        var storage = Storage(card);

        var result = await new CardWriter(storage).WriteAsync(
            AtlaSUpdate(card), card.Root, storage.Describe(card.Root), "Inspector Atlas", progress: null, CancellationToken.None);

        Assert.True(result.NeedsAdministrator);
        Assert.NotEmpty(CardRecordings.Find(card.Root));
    }

    [Fact]
    public async Task Dashcam_installs_firmware_from_the_card_and_records_with_the_new_version()
    {
        var card = NewCard();
        card.Prepare(SimulatedContent.UpdateFiles);

        card.RunInDashcam(installsUpdate: true, firmwareOnCard: new Version(1, 0, 5));
        await card.InsertAsync();

        Assert.Equal(new Version(1, 0, 5), DeviceRecognition.Recognise(card.Root, Catalog).Device?.Firmware);
    }

    [Fact]
    public void Simulated_emap_download_keeps_the_maps_folder()
    {
        var downloads = new SimulatedDownloads(new HttpClient(), NewCard());

        var files = downloads.Extract(
            new FileInfo("Scat SE_SEQ_MapS_AtlaS_eMap_Q2'2021.rar"), Directory.CreateTempSubdirectory());

        Assert.All(files, file => Assert.StartsWith("eMap/", file.CardPath));
        Assert.Contains(files, file => file.CardPath == "eMap/reinstall.dat" && file.Length == 0);
    }
}
