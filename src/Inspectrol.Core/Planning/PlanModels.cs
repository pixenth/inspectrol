namespace Inspectrol.Core.Planning;

[Flags]
public enum UpdateParts
{
    None = 0,
    Firmware = 1,
    Database = 2,
    EMap = 4,
}

public sealed record UpdatePartOption(UpdateParts Part, bool Available, string? Note);

// An older firmware version from the archive folder on the file server.
public sealed record ArchiveFirmware(Version Version, string Url);

public sealed record ModelUpdateInfo(
    Version? LatestFirmware,
    string? FirmwareUrl,
    Version? FirmwareRequires,
    DateOnly? DatabaseDate,
    string? DatabaseUrl,
    Version? DatabaseRequiresFirmware,
    IReadOnlyList<ArchiveFirmware> ArchiveFirmwares,
    IReadOnlyList<string> FirmwareWarnings,
    IReadOnlyList<string> DatabaseWarnings,
    bool FirmwareRequirementUnclear = false,
    bool DatabaseRequirementUnclear = false,
    string? EMapUrl = null);

/// <summary>One card pass: everything written to the card at once.</summary>
/// <param name="FirmwareAfter">Null when the pass does not change the firmware.</param>
/// <param name="WithMaps">eMap maps take much longer to install than other updates.</param>
public sealed record PlanStep(string Title, IReadOnlyList<string> FileUrls, Version? FirmwareAfter = null, bool WithMaps = false);

public sealed record UpdatePlan(IReadOnlyList<PlanStep> Steps, string? Blocker)
{
    public bool IsPossible => Blocker is null && Steps.Count > 0;
}
