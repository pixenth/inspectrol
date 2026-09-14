using Inspectrol.Core.Planning;

namespace Inspectrol.Tests;

public class UpdatePlannerTests
{
    private const UpdateParts FirmwareAndDatabase = UpdateParts.Firmware | UpdateParts.Database;

    private static ModelUpdateInfo Barracuda() => new(
        LatestFirmware: new Version(1, 2, 5),
        FirmwareUrl: "https://host/PO/BARRACUDA_FW_1.2.5.zip",
        FirmwareRequires: new Version(1, 2, 0),
        DatabaseDate: new DateOnly(2026, 9, 9),
        DatabaseUrl: "https://host/DB/BarracudaDB_37.zip",
        DatabaseRequiresFirmware: new Version(1, 2, 3),
        ArchiveFirmwares: [new ArchiveFirmware(new Version(1, 2, 0), "https://host/ARCHIVE/BARRACUDA_FW_1.2.0.zip")],
        FirmwareWarnings: [],
        DatabaseWarnings: []);

    private static ModelUpdateInfo AtlaS() => Barracuda() with { EMapUrl = "https://host/PO/eMap_Q2'2021.rar" };

    [Fact]
    public void Names_the_database_step_by_its_date()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.Database, new Version(1, 2, 4));

        Assert.Equal("База камер от 09.09.2026", Assert.Single(plan.Steps).Title);
    }

    [Fact]
    public void Names_the_combined_step_by_firmware_version_and_database_date()
    {
        var plan = UpdatePlanner.Build(Barracuda(), FirmwareAndDatabase, new Version(1, 2, 1));

        Assert.Equal("Прошивка 1.2.5 и база камер от 09.09.2026", Assert.Single(plan.Steps).Title);
    }

    [Fact]
    public void Names_steps_without_a_date_when_the_site_gives_none()
    {
        var info = Barracuda() with { DatabaseDate = null };

        Assert.Equal("База камер", Assert.Single(UpdatePlanner.Build(info, UpdateParts.Database, new Version(1, 2, 4)).Steps).Title);
        Assert.Equal(
            "Прошивка 1.2.5 и база камер",
            Assert.Single(UpdatePlanner.Build(info, FirmwareAndDatabase, new Version(1, 2, 1)).Steps).Title);
    }

    [Fact]
    public void Puts_all_three_parts_into_one_pass()
    {
        var plan = UpdatePlanner.Build(AtlaS(), FirmwareAndDatabase | UpdateParts.EMap, new Version(1, 2, 1));

        var step = Assert.Single(plan.Steps);
        Assert.Equal("Прошивка 1.2.5, база камер от 09.09.2026 и карты eMap", step.Title);
        Assert.Equal(
            ["https://host/PO/BARRACUDA_FW_1.2.5.zip", "https://host/DB/BarracudaDB_37.zip", "https://host/PO/eMap_Q2'2021.rar"],
            step.FileUrls);
    }

    [Fact]
    public void Writes_emap_alone_without_touching_firmware()
    {
        var plan = UpdatePlanner.Build(AtlaS(), UpdateParts.EMap, new Version(1, 2, 5));

        var step = Assert.Single(plan.Steps);
        Assert.Equal("Карты eMap", step.Title);
        Assert.Equal(["https://host/PO/eMap_Q2'2021.rar"], step.FileUrls);
        Assert.Null(step.FirmwareAfter);
        Assert.True(step.WithMaps);
    }

    [Fact]
    public void Refuses_emap_for_a_model_without_it()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.EMap, new Version(1, 2, 5));

        Assert.False(plan.IsPossible);
        Assert.Contains("eMap", plan.Blocker!);
    }

    [Fact]
    public void Refuses_an_empty_selection()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.None, new Version(1, 2, 5));

        Assert.False(plan.IsPossible);
        Assert.NotNull(plan.Blocker);
    }

    [Fact]
    public void Database_alone_needs_matching_firmware()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.Database, new Version(1, 2, 4));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(["https://host/DB/BarracudaDB_37.zip"], step.FileUrls);
        Assert.True(plan.IsPossible);
    }

    [Fact]
    public void Database_alone_on_old_firmware_asks_to_select_firmware_too()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.Database, new Version(1, 2, 1));

        Assert.False(plan.IsPossible);
        Assert.Contains("1.2.3", plan.Blocker!);
        Assert.Contains("Отметьте и прошивку", plan.Blocker!);
    }

    [Fact]
    public void Database_with_firmware_is_possible_on_old_firmware()
    {
        var plan = UpdatePlanner.Build(Barracuda(), FirmwareAndDatabase, new Version(1, 2, 1));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(
            ["https://host/PO/BARRACUDA_FW_1.2.5.zip", "https://host/DB/BarracudaDB_37.zip"],
            step.FileUrls);
    }

    [Fact]
    public void Firmware_alone_is_a_single_pass()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.Firmware, new Version(1, 2, 1));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(["https://host/PO/BARRACUDA_FW_1.2.5.zip"], step.FileUrls);
        Assert.Equal("Прошивка 1.2.5", step.Title);
    }

    [Fact]
    public void Firmware_on_a_very_old_device_goes_through_the_archive()
    {
        var plan = UpdatePlanner.Build(Barracuda(), FirmwareAndDatabase, new Version(1, 1, 0));

        Assert.Equal(2, plan.Steps.Count);
        Assert.Equal(["https://host/ARCHIVE/BARRACUDA_FW_1.2.0.zip"], plan.Steps[0].FileUrls);
        Assert.Equal(
            ["https://host/PO/BARRACUDA_FW_1.2.5.zip", "https://host/DB/BarracudaDB_37.zip"],
            plan.Steps[1].FileUrls);
    }

    [Fact]
    public void Firmware_chain_without_a_suitable_archive_version_is_blocked()
    {
        var info = Barracuda() with { ArchiveFirmwares = [] };

        var plan = UpdatePlanner.Build(info, UpdateParts.Firmware, new Version(1, 1, 0));

        Assert.False(plan.IsPossible);
        Assert.Contains("1.2.0", plan.Blocker!);
    }

    [Fact]
    public void Refuses_firmware_that_is_already_on_the_device()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.Firmware, new Version(1, 2, 5));

        Assert.False(plan.IsPossible);
        Assert.Contains("1.2.5", plan.Blocker!);
    }

    [Fact]
    public void Reports_when_database_needs_firmware_newer_than_the_latest()
    {
        // The site can contradict itself: the database requires firmware that is not published yet.
        var info = Barracuda() with { DatabaseRequiresFirmware = new Version(1, 3, 0) };

        var plan = UpdatePlanner.Build(info, FirmwareAndDatabase, new Version(1, 2, 1));

        Assert.False(plan.IsPossible);
        Assert.Contains("1.3.0", plan.Blocker!);
    }

    [Fact]
    public void Model_without_firmware_updates_still_gets_its_database()
    {
        // Some models on the site publish only a database and have no firmware page at all.
        var info = Barracuda() with
        {
            LatestFirmware = null,
            FirmwareUrl = null,
            FirmwareRequires = null,
            DatabaseRequiresFirmware = null,
            ArchiveFirmwares = [],
        };

        var plan = UpdatePlanner.Build(info, UpdateParts.Database, new Version(1, 0, 0));

        Assert.Equal(["https://host/DB/BarracudaDB_37.zip"], Assert.Single(plan.Steps).FileUrls);
    }

    [Fact]
    public void Refuses_firmware_for_a_model_without_firmware_updates()
    {
        var info = Barracuda() with { LatestFirmware = null, FirmwareUrl = null, FirmwareRequires = null };

        var plan = UpdatePlanner.Build(info, UpdateParts.Firmware, new Version(1, 0, 0));

        Assert.False(plan.IsPossible);
    }

    [Fact]
    public void Picks_the_lowest_archive_version_that_satisfies_the_requirement()
    {
        var info = Barracuda() with
        {
            ArchiveFirmwares =
            [
                new ArchiveFirmware(new Version(1, 2, 3), "https://host/ARCHIVE/FW_1.2.3.zip"),
                new ArchiveFirmware(new Version(1, 2, 0), "https://host/ARCHIVE/FW_1.2.0.zip"),
                new ArchiveFirmware(new Version(1, 0, 1), "https://host/ARCHIVE/FW_1.0.1.zip"),
            ],
        };

        var plan = UpdatePlanner.Build(info, UpdateParts.Firmware, new Version(1, 1, 0));

        Assert.Equal(["https://host/ARCHIVE/FW_1.2.0.zip"], plan.Steps[0].FileUrls);
    }

    [Fact]
    public void Refuses_when_the_firmware_requirement_was_not_understood()
    {
        var info = Barracuda() with { FirmwareRequires = null, FirmwareRequirementUnclear = true };

        var plan = UpdatePlanner.Build(info, FirmwareAndDatabase, new Version(1, 0, 0));

        Assert.False(plan.IsPossible);
        Assert.Contains("не поняла", plan.Blocker!);
    }

    [Fact]
    public void Refuses_when_the_database_requirement_was_not_understood()
    {
        var info = Barracuda() with { DatabaseRequiresFirmware = null, DatabaseRequirementUnclear = true };

        var plan = UpdatePlanner.Build(info, UpdateParts.Database, new Version(1, 2, 5));

        Assert.False(plan.IsPossible);
        Assert.Contains("не поняла", plan.Blocker!);
    }

    [Fact]
    public void Never_offers_the_same_firmware_twice()
    {
        var info = Barracuda() with
        {
            FirmwareRequires = new Version(1, 2, 5),
            ArchiveFirmwares = [new ArchiveFirmware(new Version(1, 2, 5), "https://host/ARCHIVE/BARRACUDA_FW_1.2.5.zip")],
        };

        var plan = UpdatePlanner.Build(info, UpdateParts.Firmware, new Version(1, 1, 0));

        Assert.False(plan.IsPossible);
    }

    [Fact]
    public void Refuses_when_the_title_has_no_firmware_version()
    {
        // For some models Inspector puts no version in the firmware update title.
        var info = Barracuda() with { LatestFirmware = null, FirmwareRequires = null };

        var plan = UpdatePlanner.Build(info, FirmwareAndDatabase, new Version(1, 2, 5));

        Assert.False(plan.IsPossible);
        Assert.Contains("версию", plan.Blocker!);
    }

    [Fact]
    public void Refuses_when_the_firmware_file_is_on_another_site()
    {
        // For sister brands the download button leads to the brand's own site.
        var info = Barracuda() with { FirmwareUrl = null, FirmwareRequires = null };

        var plan = UpdatePlanner.Build(info, FirmwareAndDatabase, new Version(1, 2, 1));

        Assert.False(plan.IsPossible);
        Assert.Contains("другого бренда", plan.Blocker!);
    }

    [Fact]
    public void Database_alone_still_works_when_firmware_is_unusable()
    {
        var info = Barracuda() with { LatestFirmware = null, FirmwareRequires = null };

        var plan = UpdatePlanner.Build(info, UpdateParts.Database, new Version(1, 2, 5));

        Assert.True(plan.IsPossible);
    }

    [Fact]
    public void Model_without_a_database_gets_its_firmware_alone()
    {
        // Some models on the site publish only firmware, which is a valid layout.
        var info = Barracuda() with { DatabaseDate = null, DatabaseUrl = null, DatabaseRequiresFirmware = null };

        var plan = UpdatePlanner.Build(info, UpdateParts.Firmware, new Version(1, 2, 1));

        Assert.Equal(["https://host/PO/BARRACUDA_FW_1.2.5.zip"], Assert.Single(plan.Steps).FileUrls);
    }

    [Fact]
    public void Explains_that_the_model_has_no_database()
    {
        var info = Barracuda() with { DatabaseDate = null, DatabaseUrl = null, DatabaseRequiresFirmware = null };

        var plan = UpdatePlanner.Build(info, UpdateParts.Database, new Version(1, 2, 1));

        Assert.False(plan.IsPossible);
        Assert.Contains("только прошивку", plan.Blocker!);
    }

    [Fact]
    public void Firmware_step_says_which_version_the_device_will_have()
    {
        var plan = UpdatePlanner.Build(Barracuda(), FirmwareAndDatabase, new Version(1, 2, 1));

        Assert.Equal(new Version(1, 2, 5), Assert.Single(plan.Steps).FirmwareAfter);
    }

    [Fact]
    public void Chain_steps_name_the_intermediate_and_the_final_version()
    {
        var plan = UpdatePlanner.Build(Barracuda(), FirmwareAndDatabase, new Version(1, 1, 0));

        Assert.Equal([new Version(1, 2, 0), new Version(1, 2, 5)], plan.Steps.Select(step => step.FirmwareAfter));
    }

    [Fact]
    public void Database_step_does_not_change_firmware()
    {
        var plan = UpdatePlanner.Build(Barracuda(), UpdateParts.Database, new Version(1, 2, 4));

        Assert.Null(Assert.Single(plan.Steps).FirmwareAfter);
    }

    [Fact]
    public void Lists_only_the_parts_the_model_has()
    {
        var options = UpdatePlanner.Options(Barracuda(), new Version(1, 2, 4));

        Assert.Equal([UpdateParts.Database, UpdateParts.Firmware], options.Select(option => option.Part));
        Assert.Equal(
            [UpdateParts.Database, UpdateParts.Firmware, UpdateParts.EMap],
            UpdatePlanner.Options(AtlaS(), new Version(1, 2, 4)).Select(option => option.Part));
    }

    [Fact]
    public void Does_not_let_the_user_select_firmware_that_is_already_installed()
    {
        var firmware = UpdatePlanner.Options(Barracuda(), new Version(1, 2, 5)).Single(o => o.Part == UpdateParts.Firmware);

        Assert.False(firmware.Available);
        Assert.Contains("1.2.5", firmware.Note);
    }

    [Fact]
    public void Tells_the_user_that_the_database_needs_newer_firmware()
    {
        var database = UpdatePlanner.Options(Barracuda(), new Version(1, 2, 1)).Single(o => o.Part == UpdateParts.Database);

        Assert.True(database.Available);
        Assert.Contains("1.2.3", database.Note);
    }

    [Fact]
    public void Tells_the_user_that_firmware_takes_two_passes()
    {
        var firmware = UpdatePlanner.Options(Barracuda(), new Version(1, 1, 0)).Single(o => o.Part == UpdateParts.Firmware);

        Assert.True(firmware.Available);
        Assert.Contains("1.2.0", firmware.Note);
    }

    [Fact]
    public void Does_not_let_the_user_select_firmware_with_a_missing_intermediate_version()
    {
        var info = Barracuda() with { ArchiveFirmwares = [] };

        var firmware = UpdatePlanner.Options(info, new Version(1, 1, 0)).Single(o => o.Part == UpdateParts.Firmware);

        Assert.False(firmware.Available);
    }
}
