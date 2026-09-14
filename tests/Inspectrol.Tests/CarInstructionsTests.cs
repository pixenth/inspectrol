using Inspectrol.Core.Planning;

namespace Inspectrol.Tests;

public class CarInstructionsTests
{
    private static readonly PlanStep FirmwareAndDatabase = new(
        "Прошивка 1.0.5 и база камер",
        ["https://host/PO/AtlaS_1.0.5.zip", "https://host/DB/AtlaSDB_37.zip"],
        FirmwareAfter: new Version(1, 0, 5));

    private static readonly PlanStep DatabaseOnly = new(
        "Записать базу камер",
        ["https://host/DB/AtlaSDB_37.zip"]);

    private static IReadOnlyList<DeviceAction> Actions(PlanStep step, int passNumber = 1, int passCount = 1) =>
        DeviceProcedure.For(step, passNumber, passCount);

    [Fact]
    public void Every_step_is_numbered_and_keeps_its_order()
    {
        var actions = Actions(FirmwareAndDatabase);
        var text = CarInstructions.Text(actions, "Inspector AtlaS");

        var previous = -1;
        for (var number = 1; number <= actions.Count; number++)
        {
            var place = text.IndexOf($"{number}. {actions[number - 1].Title}", StringComparison.Ordinal);
            Assert.True(place > previous, $"шаг {number} «{actions[number - 1].Title}» не на своём месте");
            previous = place;
        }
    }

    [Fact]
    public void Carries_the_hints_along_because_nobody_can_press_no_at_the_car()
    {
        var text = CarInstructions.Text(Actions(FirmwareAndDatabase), "Inspector AtlaS");

        foreach (var action in Actions(FirmwareAndDatabase))
            Assert.Contains(action.IfNot, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_asks_a_question_the_person_cannot_answer_at_the_car()
    {
        var text = CarInstructions.Text(Actions(FirmwareAndDatabase), "Inspector AtlaS");

        foreach (var action in Actions(FirmwareAndDatabase))
            Assert.DoesNotContain(action.Question, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Says_at_the_end_to_come_back_to_the_computer()
    {
        var text = CarInstructions.Text(Actions(FirmwareAndDatabase), "Inspector AtlaS");

        Assert.Contains("вернитесь к компьютеру", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Names_the_device_so_the_photo_is_not_mixed_up_with_another_one()
    {
        var text = CarInstructions.Text(Actions(FirmwareAndDatabase), "Inspector AtlaS");

        Assert.Contains("Inspector AtlaS", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Keeps_the_engine_running_until_the_card_comes_back()
    {
        var text = CarInstructions.Text(Actions(FirmwareAndDatabase), "Inspector AtlaS");

        Assert.Contains("не глушите", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Asks_only_what_can_be_checked_after_coming_back()
    {
        var questions = CarInstructions.Questions(Actions(FirmwareAndDatabase));

        Assert.Equal([DeviceActionKind.CheckVersion], questions.Select(a => a.Kind));
    }

    [Fact]
    public void Without_a_new_firmware_asks_whether_the_device_still_works()
    {
        var questions = CarInstructions.Questions(Actions(DatabaseOnly));

        Assert.Equal([DeviceActionKind.CheckDeviceWorks], questions.Select(a => a.Kind));
    }

    [Fact]
    public void In_a_chain_the_last_question_is_about_the_card_being_back()
    {
        var questions = CarInstructions.Questions(Actions(FirmwareAndDatabase, passNumber: 1, passCount: 2));

        Assert.Equal(
            [DeviceActionKind.CheckVersion, DeviceActionKind.CardBackToComputer],
            questions.Select(a => a.Kind));
    }

    [Fact]
    public void What_is_asked_afterwards_is_still_written_on_the_sheet()
    {
        var actions = Actions(FirmwareAndDatabase);
        var text = CarInstructions.Text(actions, "Inspector AtlaS");

        foreach (var question in CarInstructions.Questions(actions))
            Assert.Contains(question.Title, text, StringComparison.Ordinal);
    }
}
