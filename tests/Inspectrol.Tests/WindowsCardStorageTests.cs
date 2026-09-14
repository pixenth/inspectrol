using Inspectrol.Core.Cards;

namespace Inspectrol.Tests;

public class WindowsCardStorageTests
{
    private static FileInfo MakeFile(DirectoryInfo folder, string name, params byte[] content)
    {
        var file = new FileInfo(Path.Combine(folder.FullName, name));
        File.WriteAllBytes(file.FullName, content);
        return file;
    }

    private static CardFile[] UpdateWithMaps(DirectoryInfo source) =>
    [
        CardFile.InRoot(MakeFile(source, "firmware.bin", 1, 2, 3)),
        new CardFile(MakeFile(source, "map.kdb", 4, 5), "eMap/map/cis/map.kdb"),
        new CardFile(MakeFile(source, "reinstall.dat"), "eMap/reinstall.dat"),
    ];

    [Fact]
    public async Task Copies_files_into_their_folders_on_the_card()
    {
        var card = Directory.CreateTempSubdirectory();
        var files = UpdateWithMaps(Directory.CreateTempSubdirectory());
        var storage = new WindowsCardStorage();

        await storage.CopyToCardAsync(files, card, progress: null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(card.FullName, "firmware.bin")));
        Assert.True(File.Exists(Path.Combine(card.FullName, "eMap", "map", "cis", "map.kdb")));
        Assert.True(File.Exists(Path.Combine(card.FullName, "eMap", "reinstall.dat")));
        Assert.True(await storage.VerifyAsync(files, card, CancellationToken.None));
    }

    [Fact]
    public async Task Notices_a_file_that_differs_on_the_card()
    {
        var card = Directory.CreateTempSubdirectory();
        var files = UpdateWithMaps(Directory.CreateTempSubdirectory());
        var storage = new WindowsCardStorage();

        await storage.CopyToCardAsync(files, card, progress: null, CancellationToken.None);
        File.WriteAllBytes(Path.Combine(card.FullName, "eMap", "map", "cis", "map.kdb"), [9, 9]);

        Assert.False(await storage.VerifyAsync(files, card, CancellationToken.None));
    }
}
