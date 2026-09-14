namespace Inspectrol.Core.Planning;

public enum DeviceActionKind
{
    CardOutOfComputer,
    CardIntoDevice,
    StartEngine,
    AcceptUpdate,
    WaitForReboot,
    ResetSettings,
    CheckVersion,
    CheckDeviceWorks,
    CardBackToComputer,
}

/// <param name="GiveUp">
/// Null when the step is optional. Otherwise the update did not happen and the completion screen must not be shown.
/// </param>
/// <param name="Critical">The update is in progress: the card and the power must not be touched.</param>
public sealed record DeviceAction(
    DeviceActionKind Kind,
    string Title,
    string Text,
    string Question,
    string IfNot,
    string? GiveUp,
    bool Critical = false);

/// <remarks>
/// The actions go on a sheet the user takes to the car (<see cref="CarInstructions"/>), so the texts must work
/// without the app. In many cars the dashcam is powered only while the engine runs, so the engine is started before
/// the update and kept running. Model-specific menu names are deliberately absent until checked on a real device.
/// </remarks>
public static class DeviceProcedure
{
    private static string DontTouch => Strings.DeviceProcedure_DontTouch;

    private static string SupportPage => Strings.DeviceProcedure_SupportPage;

    // passNumber is one-based.
    public static IReadOnlyList<DeviceAction> For(PlanStep step, int passNumber, int passCount)
    {
        var updates = Math.Max(1, step.FileUrls.Count);
        var actions = new List<DeviceAction>
        {
            new(DeviceActionKind.CardOutOfComputer,
                Strings.DeviceProcedure_CardOutOfComputerTitle,
                Strings.DeviceProcedure_CardOutOfComputerText,
                Strings.DeviceProcedure_CardOutOfComputerQuestion,
                Strings.DeviceProcedure_CardOutOfComputerIfNot,
                GiveUp: null),

            new(DeviceActionKind.CardIntoDevice,
                Strings.DeviceProcedure_CardIntoDeviceTitle,
                Strings.DeviceProcedure_CardIntoDeviceText,
                Strings.DeviceProcedure_CardIntoDeviceQuestion,
                Strings.DeviceProcedure_CardIntoDeviceIfNot,
                GiveUp: null),

            new(DeviceActionKind.StartEngine,
                Strings.DeviceProcedure_StartEngineTitle,
                Strings.DeviceProcedure_StartEngineText,
                Strings.DeviceProcedure_StartEngineQuestion,
                Strings.DeviceProcedure_StartEngineIfNot,
                GiveUp:
                Strings.DeviceProcedure_StartEngineGiveUp),
        };

        for (var number = 1; number <= updates; number++)
        {
            actions.Add(new DeviceAction(
                DeviceActionKind.AcceptUpdate,
                updates > 1
                    ? string.Format(Strings.DeviceProcedure_AcceptUpdateNumberedTitle, number, updates)
                    : Strings.DeviceProcedure_AcceptUpdateTitle,
                // "Кружок" and "крестик" are quoted from the update procedure on the AtlaS and Sparta support pages.
                number == 1
                    ? Strings.DeviceProcedure_AcceptFirstUpdateText
                      + (updates > 1
                          ? " " + string.Format(Strings.DeviceProcedure_AcceptUpdateCountNote, updates)
                          : "")
                    : Strings.DeviceProcedure_AcceptNextUpdateText,
                Strings.DeviceProcedure_AcceptUpdateQuestion,
                number == 1
                    ? Strings.DeviceProcedure_AcceptFirstUpdateIfNot
                    : Strings.DeviceProcedure_AcceptNextUpdateIfNot,
                GiveUp: number == 1
                    ? Strings.DeviceProcedure_AcceptFirstUpdateGiveUp + " " + SupportPage
                    : Strings.DeviceProcedure_AcceptNextUpdateGiveUp + " " + SupportPage));

            actions.Add(new DeviceAction(
                DeviceActionKind.WaitForReboot,
                Strings.DeviceProcedure_WaitForRebootTitle,
                Strings.DeviceProcedure_WaitForRebootText + " " + DontTouch
                + " " + (number == updates && step.WithMaps
                    ? Strings.DeviceProcedure_WaitForMapsDuration
                    : Strings.DeviceProcedure_WaitForRebootDuration),
                Strings.DeviceProcedure_WaitForRebootQuestion,
                Strings.DeviceProcedure_WaitForRebootIfNot + " " + DontTouch,
                GiveUp:
                Strings.DeviceProcedure_WaitForRebootGiveUp + " " + SupportPage,
                Critical: true));
        }

        if (step.FirmwareAfter is { } version)
        {
            actions.Add(new DeviceAction(
                DeviceActionKind.ResetSettings,
                Strings.DeviceProcedure_ResetSettingsTitle,
                Strings.DeviceProcedure_ResetSettingsText,
                Strings.DeviceProcedure_ResetSettingsQuestion,
                Strings.DeviceProcedure_ResetSettingsIfNot,
                GiveUp: null));

            actions.Add(new DeviceAction(
                DeviceActionKind.CheckVersion,
                Strings.DeviceProcedure_CheckVersionTitle,
                string.Format(Strings.DeviceProcedure_CheckVersionText, version),
                string.Format(Strings.DeviceProcedure_CheckVersionQuestion, version),
                Strings.DeviceProcedure_CheckVersionIfNot,
                GiveUp:
                string.Format(Strings.DeviceProcedure_CheckVersionGiveUp, version) + " " + SupportPage));
        }
        else
        {
            actions.Add(new DeviceAction(
                DeviceActionKind.CheckDeviceWorks,
                Strings.DeviceProcedure_CheckDeviceWorksTitle,
                Strings.DeviceProcedure_CheckDeviceWorksText,
                Strings.DeviceProcedure_CheckDeviceWorksQuestion,
                Strings.DeviceProcedure_CheckDeviceWorksIfNot,
                GiveUp:
                Strings.DeviceProcedure_CheckDeviceWorksGiveUp + " " + SupportPage));
        }

        if (passNumber < passCount)
        {
            actions.Add(new DeviceAction(
                DeviceActionKind.CardBackToComputer,
                Strings.DeviceProcedure_CardBackToComputerTitle,
                string.Format(Strings.DeviceProcedure_CardBackToComputerText, passNumber, passCount),
                Strings.DeviceProcedure_CardBackToComputerQuestion,
                Strings.DeviceProcedure_CardBackToComputerIfNot,
                GiveUp: null));
        }

        return actions;
    }
}
