using System.Text.Json;

namespace Inspectrol.Core.Memory;

public sealed record AppMemory
{
    public string? ModelName { get; init; }

    public string? Region { get; init; }

    // A string, not a Version: the site has versions such as "1.0R221E".
    public string? Firmware { get; init; }

    public DateTime? UpdatedAt { get; init; }

    public string? UpdatedWhat { get; init; }

    // Reserved for a simplified mode with larger text; the wizard does not offer it yet.
    public bool SimpleMode { get; init; }

    public bool UnofficialWarningSeen { get; init; }

    public bool IsFirstRun => ModelName is null && !UnofficialWarningSeen;
}

// A corrupt or unavailable settings file must never keep the user from updating the dashcam.
public sealed class AppMemoryStore(string? filePath = null)
{
    private static readonly JsonSerializerOptions Format = new() { WriteIndented = true };

    private readonly string _path = filePath ?? DefaultPath();

    public static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Inspectrol",
        "настройки.json");

    public AppMemory Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppMemory();

            return JsonSerializer.Deserialize<AppMemory>(File.ReadAllText(_path)) ?? new AppMemory();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppMemory();
        }
    }

    public bool TrySave(AppMemory memory)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(memory, Format));
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
