using Inspectrol.Core.Planning;

namespace Inspectrol.Tests;

public class DeviceProcedureTests
{
    private static readonly PlanStep FirmwareAndDatabase = new(
        "Прошивка 1.0.5 и база камер",
        ["https://host/PO/AtlaS_1.0.5.zip", "https://host/DB/AtlaSDB_37.zip"],
        FirmwareAfter: new Version(1, 0, 5));

    private static readonly PlanStep DatabaseOnly = new(
        "Записать базу камер",
        ["https://host/DB/AtlaSDB_37.zip"]);

    [Fact]
    public void Engine_is_started_before_the_device_asks_anything()
    {
        // In many cars the dashcam has no power while the engine is off.
        var actions = DeviceProcedure.For(FirmwareAndDatabase, passNumber: 1, passCount: 1);

        var engine = actions.ToList().FindIndex(a => a.Title.Contains("Заведите двигатель"));
        var firstQuestion = actions.ToList().FindIndex(a => a.Kind == DeviceActionKind.AcceptUpdate);

        Assert.True(engine >= 0);
        Assert.True(engine < firstQuestion);
    }

    [Fact]
    public void Never_tells_to_stop_the_engine_while_updating()
    {
        var actions = DeviceProcedure.For(FirmwareAndDatabase, passNumber: 1, passCount: 2);

        foreach (var action in actions)
        {
            var text = $"{action.Title} {action.Text} {action.IfNot} {action.GiveUp}";
            Assert.DoesNotContain("не заводите", text, StringComparison.OrdinalIgnoreCase);
            if (action.Kind != DeviceActionKind.CardBackToComputer)
                Assert.DoesNotContain("заглушите", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Asks_about_each_update_and_waits_for_each_reboot()
    {
        // Sparta and AtlaS ask about each update separately and reboot in between.
        var actions = DeviceProcedure.For(FirmwareAndDatabase, passNumber: 1, passCount: 1);

        var sequence = actions
            .Where(a => a.Kind is DeviceActionKind.AcceptUpdate or DeviceActionKind.WaitForReboot)
            .Select(a => a.Kind);

        Assert.Equal(
            [DeviceActionKind.AcceptUpdate, DeviceActionKind.WaitForReboot,
             DeviceActionKind.AcceptUpdate, DeviceActionKind.WaitForReboot],
            sequence);
    }

    [Fact]
    public void Firmware_pass_ends_with_reset_and_the_expected_version()
    {
        var actions = DeviceProcedure.For(FirmwareAndDatabase, passNumber: 1, passCount: 1);

        Assert.Equal(DeviceActionKind.ResetSettings, actions[^2].Kind);
        Assert.Equal(DeviceActionKind.CheckVersion, actions[^1].Kind);
        Assert.Contains("1.0.5", actions[^1].Question);
    }

    [Fact]
    public void Database_pass_does_not_ask_about_firmware_version()
    {
        var actions = DeviceProcedure.For(DatabaseOnly, passNumber: 1, passCount: 1);

        Assert.DoesNotContain(actions, a => a.Kind is DeviceActionKind.CheckVersion or DeviceActionKind.ResetSettings);
        Assert.Single(actions, a => a.Kind == DeviceActionKind.AcceptUpdate);
    }

    [Fact]
    public void Middle_of_a_chain_brings_the_card_back_to_the_computer()
    {
        var actions = DeviceProcedure.For(FirmwareAndDatabase, passNumber: 1, passCount: 2);

        Assert.Equal(DeviceActionKind.CardBackToComputer, actions[^1].Kind);
        Assert.DoesNotContain(
            DeviceProcedure.For(FirmwareAndDatabase, passNumber: 2, passCount: 2),
            a => a.Kind == DeviceActionKind.CardBackToComputer);
    }

    [Fact]
    public void Every_action_asks_whether_it_worked_and_says_what_to_do_if_not()
    {
        foreach (var step in new[] { FirmwareAndDatabase, DatabaseOnly })
        foreach (var action in DeviceProcedure.For(step, passNumber: 1, passCount: 2))
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Title));
            Assert.False(string.IsNullOrWhiteSpace(action.Text));
            Assert.EndsWith("?", action.Question);
            Assert.False(string.IsNullOrWhiteSpace(action.IfNot));
        }
    }

    [Fact]
    public void An_update_that_did_not_happen_is_never_reported_as_done()
    {
        var actions = DeviceProcedure.For(FirmwareAndDatabase, passNumber: 1, passCount: 1);

        foreach (var action in actions.Where(a => a.Kind is DeviceActionKind.AcceptUpdate
                     or DeviceActionKind.WaitForReboot or DeviceActionKind.CheckVersion))
        {
            Assert.False(string.IsNullOrWhiteSpace(action.GiveUp));
        }

        Assert.Null(actions.Single(a => a.Kind == DeviceActionKind.ResetSettings).GiveUp);
    }

    [Fact]
    public void Warns_that_maps_take_longer_to_install()
    {
        var step = new PlanStep("Карты eMap", ["https://host/eMap.rar"], WithMaps: true);

        var actions = DeviceProcedure.For(step, 1, 1);

        Assert.Contains(actions, action => action.Kind == DeviceActionKind.WaitForReboot && action.Text.Contains("eMap"));
    }
}
