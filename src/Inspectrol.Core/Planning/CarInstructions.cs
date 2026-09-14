namespace Inspectrol.Core.Planning;

/// <remarks>
/// The computer stays indoors while the dashcam is in the car, so steps cannot be confirmed one by one. The whole pass
/// goes on one sheet with the hints printed under each step, and only questions whose answers can be checked back at
/// the computer are asked afterwards.
/// </remarks>
public static class CarInstructions
{
    public static IReadOnlyList<DeviceAction> Questions(IReadOnlyList<DeviceAction> actions) =>
    [
        .. actions.Where(action => action.Kind
            is DeviceActionKind.CheckVersion
            or DeviceActionKind.CheckDeviceWorks
            or DeviceActionKind.CardBackToComputer),
    ];

    public static string Text(IReadOnlyList<DeviceAction> actions, string modelName)
    {
        var lines = new List<string>
        {
            string.Format(Strings.CarInstructions_Heading, modelName),
            Strings.CarInstructions_EngineRule,
            "",
        };

        for (var number = 1; number <= actions.Count; number++)
        {
            var action = actions[number - 1];
            lines.Add($"{number}. {action.Title}");
            lines.Add($"   {action.Text}");
            lines.Add("   " + string.Format(Strings.CarInstructions_IfNot, action.IfNot));
            lines.Add("");
        }

        lines.Add(Strings.CarInstructions_Ending);
        return string.Join(Environment.NewLine, lines);
    }
}
