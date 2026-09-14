using System.Globalization;

namespace Inspectrol.Core.Planning;

/// <remarks>
/// Everything selected goes into one card pass: the files have different names, and the dashcam asks about each
/// update separately and reboots between them (confirmed on Sparta and AtlaS). Only an intermediate firmware from
/// the archive gets a pass of its own. The site states requirements for the latest firmware only, so the chain is
/// limited to one intermediate pass; if a model ever needs more, the dashcam refuses the update.
/// </remarks>
public static class UpdatePlanner
{
    public static UpdatePlan Build(ModelUpdateInfo info, UpdateParts parts, Version current)
    {
        if (parts == UpdateParts.None)
            return Blocked(Strings.UpdatePlanner_NothingSelected);

        var withFirmware = parts.HasFlag(UpdateParts.Firmware);
        var withDatabase = parts.HasFlag(UpdateParts.Database);
        var withEMap = parts.HasFlag(UpdateParts.EMap);

        if (withDatabase && DatabaseProblem(info) is { } databaseProblem)
            return Blocked(databaseProblem);

        if (withFirmware && FirmwareProblem(info, current) is { } firmwareProblem)
            return Blocked(firmwareProblem);

        if (withEMap && info.EMapUrl is null)
            return Blocked(Strings.UpdatePlanner_NoEMap);

        // The database is installed after the firmware of the same pass, so it is checked against that firmware.
        var firmwareAfter = withFirmware ? info.LatestFirmware! : current;
        if (withDatabase && info.DatabaseRequiresFirmware is { } required && firmwareAfter < required)
        {
            return Blocked(withFirmware
                ? string.Format(Strings.UpdatePlanner_DatabaseNeedsFirmwareNewerThanLatest, required, info.LatestFirmware)
                : DatabaseNeedsFirmware(info, required, current));
        }

        var files = new List<string>();
        var names = new List<string>();

        if (withFirmware)
        {
            files.Add(info.FirmwareUrl!);
            names.Add(string.Format(Strings.UpdatePlanner_PartFirmware, info.LatestFirmware));
        }

        if (withDatabase)
        {
            files.Add(info.DatabaseUrl!);
            names.Add(info.DatabaseDate is { } date
                ? string.Format(Strings.UpdatePlanner_PartDatabase, date)
                : Strings.UpdatePlanner_PartUndatedDatabase);
        }

        if (withEMap)
        {
            files.Add(info.EMapUrl!);
            names.Add(Strings.UpdatePlanner_PartEMap);
        }

        var lastPass = new PlanStep(Title(names), files, withFirmware ? info.LatestFirmware : null, WithMaps: withEMap);

        if (!withFirmware || Bridge(info, current) is not { } bridge)
            return new UpdatePlan([lastPass], null);

        return new UpdatePlan(
            [new PlanStep(string.Format(Strings.UpdatePlanner_BridgeFirmwareStep, bridge.Version), [bridge.Url], bridge.Version), lastPass],
            null);
    }

    // Safest part first.
    public static IReadOnlyList<UpdatePartOption> Options(ModelUpdateInfo info, Version current)
    {
        var options = new List<UpdatePartOption>();

        if (info.DatabaseUrl is not null)
        {
            var problem = DatabaseProblem(info);
            var note = problem
                ?? (info.DatabaseRequiresFirmware is { } required && current < required
                    ? string.Format(Strings.UpdatePlanner_OptionDatabaseNeedsFirmware, required, current)
                    : null);
            options.Add(new UpdatePartOption(UpdateParts.Database, problem is null, note));
        }

        if (info.LatestFirmware is not null || info.FirmwareUrl is not null)
        {
            var problem = FirmwareProblem(info, current);
            var note = problem
                ?? (Bridge(info, current) is { } bridge
                    ? string.Format(Strings.UpdatePlanner_OptionFirmwareChain, bridge.Version, info.LatestFirmware)
                    : null);
            options.Add(new UpdatePartOption(UpdateParts.Firmware, problem is null, note));
        }

        if (info.EMapUrl is not null)
            options.Add(new UpdatePartOption(UpdateParts.EMap, true, null));

        return options;
    }

    private static string? DatabaseProblem(ModelUpdateInfo info)
    {
        if (info.DatabaseUrl is null)
            return Strings.UpdatePlanner_FirmwareOnlyModel;

        return info.DatabaseRequirementUnclear
            ? UnclearRequirement(Strings.UpdatePlanner_UnclearRequirementDatabase)
            : null;
    }

    private static string? FirmwareProblem(ModelUpdateInfo info, Version current)
    {
        if (info.LatestFirmware is null && info.FirmwareUrl is null)
            return Strings.UpdatePlanner_NoFirmware;

        if (info.FirmwareRequirementUnclear)
            return UnclearRequirement(Strings.UpdatePlanner_UnclearRequirementFirmware);

        // For some models Inspector does not put the version in the update title, and sister brands host the file on
        // another site. In both cases the firmware exists but cannot be used.
        if (info.LatestFirmware is null)
            return Strings.UpdatePlanner_FirmwareVersionUnknown;

        if (info.FirmwareUrl is null)
            return Strings.UpdatePlanner_FirmwareOnForeignSite;

        if (current >= info.LatestFirmware)
            return string.Format(Strings.UpdatePlanner_FirmwareAlreadyLatest, info.LatestFirmware);

        if (info.FirmwareRequires is { } requires && current < requires && Bridge(info, current) is null)
            return string.Format(Strings.UpdatePlanner_NoBridgeFirmware, requires, current);

        return null;
    }

    // Strictly between current and latest, or the same file would be written in two consecutive passes.
    private static ArchiveFirmware? Bridge(ModelUpdateInfo info, Version current)
    {
        if (info.LatestFirmware is null || info.FirmwareRequires is not { } requires || current >= requires)
            return null;

        return info.ArchiveFirmwares
            .Where(firmware => firmware.Version >= requires
                && firmware.Version > current
                && firmware.Version < info.LatestFirmware)
            .OrderBy(firmware => firmware.Version)
            .FirstOrDefault();
    }

    private static string DatabaseNeedsFirmware(ModelUpdateInfo info, Version required, Version current)
    {
        var text = string.Format(Strings.UpdatePlanner_DatabaseNeedsNewerFirmware, required, current);
        var firmwareHelps = FirmwareProblem(info, current) is null && info.LatestFirmware >= required;
        return firmwareHelps ? text + " " + Strings.UpdatePlanner_SelectFirmwareToo : text;
    }

    private static string Title(IReadOnlyList<string> names)
    {
        var text = names.Count switch
        {
            1 => names[0],
            2 => string.Format(Strings.UpdatePlanner_TwoParts, names[0], names[1]),
            _ => string.Format(Strings.UpdatePlanner_ThreeParts, names[0], names[1], names[2]),
        };

        return char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
    }

    private static UpdatePlan Blocked(string reason) => new([], reason);

    private static string UnclearRequirement(string what) =>
        string.Format(Strings.UpdatePlanner_UnclearRequirement, what);
}
