using System.IO.Compression;
using Inspectrol.Core.Downloading;

namespace Inspectrol.Tests;

public class ArchiveExtractorTests
{
    private static FileInfo MakeZip(string[] names, string[]? empty = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"inspectrol-{Guid.NewGuid():N}.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var name in names)
            {
                var entry = zip.CreateEntry(name);
                if (empty?.Contains(name) == true)
                    continue;

                using var stream = entry.Open();
                stream.WriteByte(42);
            }
        }
        return new FileInfo(path);
    }

    private static FileInfo MakeZip(params string[] names) => MakeZip(names, empty: null);

    private static FileInfo MakeFile(string extension, params byte[] firstBytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"inspectrol-{Guid.NewGuid():N}{extension}");
        File.WriteAllBytes(path, [.. firstBytes, 0, 0, 0, 0]);
        return new FileInfo(path);
    }

    [Fact]
    public void Extracts_update_files_into_the_card_root()
    {
        var folder = Directory.CreateTempSubdirectory();

        var files = ArchiveExtractor.Extract(MakeZip("BarracudaFW.BRN", "BarracudaDB.bin", "instruction.pdf"), folder);

        Assert.Equal(["BarracudaFW.BRN", "BarracudaDB.bin"], files.Select(file => file.CardPath));
        Assert.True(File.Exists(Path.Combine(folder.FullName, "BarracudaDB.bin")));
    }

    [Fact]
    public void Extracts_every_firmware_file_of_a_signature_model()
    {
        // The real archive AtlaS_1.0.5+E3.0.zip holds firmware.bin, firmware2.bin and rdfw.bin (the radar part).
        var folder = Directory.CreateTempSubdirectory();

        var files = ArchiveExtractor.Extract(MakeZip("firmware.bin", "firmware2.bin", "rdfw.bin"), folder);

        Assert.Equal(3, files.Count);
        Assert.All(new[] { "firmware.bin", "firmware2.bin", "rdfw.bin" },
            name => Assert.True(File.Exists(Path.Combine(folder.FullName, name)), name));
    }

    [Fact]
    public void Keeps_the_emap_folder_with_all_its_files()
    {
        // The eMap archive holds an "eMap" folder with maps, fonts, skins and an empty "reinstall.dat" marker.
        var folder = Directory.CreateTempSubdirectory();
        var archive = MakeZip(
            ["eMap/map/cis/map.kdb", "eMap/media/cis/cis_16c.fnt", "eMap/system.ini", "eMap/reinstall.dat"],
            empty: ["eMap/reinstall.dat"]);

        var files = ArchiveExtractor.Extract(archive, folder);

        Assert.Equal(
            ["eMap/map/cis/map.kdb", "eMap/media/cis/cis_16c.fnt", "eMap/system.ini", "eMap/reinstall.dat"],
            files.Select(file => file.CardPath));
        Assert.True(File.Exists(Path.Combine(folder.FullName, "eMap", "map", "cis", "map.kdb")));
    }

    [Fact]
    public void Refuses_an_emap_archive_without_map_data()
    {
        var folder = Directory.CreateTempSubdirectory();

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveExtractor.Extract(MakeZip(["eMap/system.ini", "eMap/reinstall.dat"], empty: ["eMap/reinstall.dat"]), folder));

        Assert.Contains("не похоже на обновление", error.Message);
    }

    [Fact]
    public void Refuses_paths_outside_the_folder()
    {
        var folder = Directory.CreateTempSubdirectory();

        var error = Assert.Throws<InvalidDataException>(() => ArchiveExtractor.Extract(MakeZip("../escape.bin"), folder));

        Assert.Contains("подозрительн", error.Message);
    }

    [Fact]
    public void Refuses_archive_without_device_files()
    {
        var folder = Directory.CreateTempSubdirectory();

        var error = Assert.Throws<InvalidDataException>(() => ArchiveExtractor.Extract(MakeZip("readme.txt"), folder));

        Assert.Contains("не похоже на обновление", error.Message);
    }

    [Fact]
    public void Explains_a_damaged_rar_archive()
    {
        // Inspector publishes eMap maps as RAR archives.
        var folder = Directory.CreateTempSubdirectory();
        var rar = MakeFile(".rar", 0x52, 0x61, 0x72, 0x21, 0x1a, 0x07, 0x00);

        var error = Assert.Throws<InvalidDataException>(() => ArchiveExtractor.Extract(rar, folder));

        Assert.Contains("не похож на архив", error.Message);
    }

    [Fact]
    public void Refuses_a_file_that_is_not_an_archive()
    {
        var folder = Directory.CreateTempSubdirectory();
        var html = MakeFile(".zip", (byte)'<', (byte)'h', (byte)'t', (byte)'m', (byte)'l');

        var error = Assert.Throws<InvalidDataException>(() => ArchiveExtractor.Extract(html, folder));

        Assert.Contains("не похож на архив", error.Message);
    }

    [Fact]
    public void Ignores_junk_that_macos_puts_into_archives()
    {
        var folder = Directory.CreateTempSubdirectory();

        var files = ArchiveExtractor.Extract(MakeZip("firmware.bin", "__MACOSX/._firmware.bin"), folder);

        Assert.Equal("firmware.bin", Assert.Single(files).CardPath);
    }

    [Fact]
    public void Refuses_an_archive_holding_only_macos_junk()
    {
        var folder = Directory.CreateTempSubdirectory();

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveExtractor.Extract(MakeZip("__MACOSX/._firmware.bin"), folder));

        Assert.Contains("не похоже на обновление", error.Message);
    }

    [Fact]
    public void Refuses_an_empty_device_file()
    {
        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveExtractor.Extract(MakeZip(["firmware.bin"], empty: ["firmware.bin"]), Directory.CreateTempSubdirectory()));

        Assert.Contains("не похоже на обновление", error.Message);
    }
}
