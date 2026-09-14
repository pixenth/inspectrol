using Inspectrol.Core.Memory;

namespace Inspectrol.Tests;

public class AppMemoryTests
{
    private static string TempFile() =>
        Path.Combine(Directory.CreateTempSubdirectory().FullName, "настройки.json");

    [Fact]
    public void Remembers_what_was_updated()
    {
        var path = TempFile();
        var store = new AppMemoryStore(path);

        store.TrySave(new AppMemory
        {
            ModelName = "Inspector Barracuda",
            Firmware = "1.2.5",
            UpdatedWhat = "база камер",
            UpdatedAt = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Local),
        });

        var loaded = new AppMemoryStore(path).Load();

        Assert.Equal("Inspector Barracuda", loaded.ModelName);
        Assert.Equal("1.2.5", loaded.Firmware);
        Assert.Equal("база камер", loaded.UpdatedWhat);
        Assert.Equal(2026, loaded.UpdatedAt!.Value.Year);
    }

    [Fact]
    public void Firmware_is_kept_as_written()
    {
        // The site has versions that do not parse as numbers, such as "1.0R221E".
        var path = TempFile();
        new AppMemoryStore(path).TrySave(new AppMemory { Firmware = "1.0R221E" });

        Assert.Equal("1.0R221E", new AppMemoryStore(path).Load().Firmware);
    }

    [Fact]
    public void First_run_when_there_is_no_file()
    {
        var memory = new AppMemoryStore(TempFile()).Load();

        Assert.True(memory.IsFirstRun);
        Assert.False(memory.SimpleMode);
    }

    [Fact]
    public void Broken_file_does_not_stop_the_program()
    {
        var path = TempFile();
        File.WriteAllText(path, "это не настройки, а какой-то мусор {{{");

        var memory = new AppMemoryStore(path).Load();

        Assert.True(memory.IsFirstRun);
    }

    [Fact]
    public void Remembers_the_chosen_mode()
    {
        var path = TempFile();
        new AppMemoryStore(path).TrySave(new AppMemory { SimpleMode = true, UnofficialWarningSeen = true });

        var loaded = new AppMemoryStore(path).Load();

        Assert.True(loaded.SimpleMode);
        Assert.False(loaded.IsFirstRun);
    }

    [Fact]
    public void Saving_into_a_folder_that_does_not_exist_yet_works()
    {
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "нет", "такой", "папки.json");

        Assert.True(new AppMemoryStore(path).TrySave(new AppMemory { ModelName = "Inspector Sparta" }));
        Assert.Equal("Inspector Sparta", new AppMemoryStore(path).Load().ModelName);
    }
}
