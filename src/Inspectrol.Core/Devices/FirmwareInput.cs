namespace Inspectrol.Core.Devices;

// With the Russian layout the numeric keypad types a comma, so "1,2,3" is accepted the same as "1.2.3".
public static class FirmwareInput
{
    public static Version? Parse(string? typed)
    {
        if (string.IsNullOrWhiteSpace(typed))
            return null;

        var text = typed.Trim().Replace(',', '.');
        return Version.TryParse(text, out var version) ? version : null;
    }
}
