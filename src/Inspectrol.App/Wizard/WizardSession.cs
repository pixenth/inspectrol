using System.IO;
using System.Net.Http;
using Inspectrol.Core;
using Inspectrol.Core.Cards;
using Inspectrol.Core.Catalog;
using Inspectrol.Core.Devices;
using Inspectrol.Core.Downloading;
using Inspectrol.Core.Planning;

namespace Inspectrol.App.Wizard;

internal sealed class WizardSession(HttpClient http)
{
    public HttpClient Http { get; } = http;

    public IReadOnlyList<CatalogModel>? Models { get; set; }

    public CatalogModel? Model { get; set; }

    // Currently installed on the dashcam; the plan is built from it.
    public Version? Firmware { get; set; }

    public DirectoryInfo? CardRoot { get; set; }

    public ICardStorage CardStorage { get; set; } = new WindowsCardStorage();

    public Func<IReadOnlyList<RemovableCard>> FindCards { get; set; } = RemovableCards.Find;

    public IUpdateDownloads Downloads { get; set; } = new WebUpdateDownloads(http);

    public string DownloadsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Inspectrol",
        Strings.DownloadPage_DownloadsFolderName);

    // Parent of the folder the recordings are saved into.
    public string RecordingsFolder { get; set; } = VideosFolder();

    // Card size from the first pass, used to detect a different card in a later pass.
    public long? CardBytes { get; set; }

    public UpdatePlan? Plan { get; set; }

    public IReadOnlyList<CardFile> Files { get; set; } = [];

    public string? RecordingsSavedTo { get; set; }

    // Read by the user in the dashcam menu after a pass.
    public Version? ConfirmedFirmware { get; set; }

    public int PassesDone { get; set; }

    // The plan is rebuilt from the new firmware after every pass, so the current pass is always its first step.
    public PlanStep? CurrentStep => Plan is { IsPossible: true } plan ? plan.Steps[0] : null;

    public int PassNumber => PassesDone + 1;

    public int PassCount => PassesDone + Math.Max(1, Plan?.Steps.Count ?? 1);

    // Screen preview on made-up data: writing to a card is disabled.
    public bool Preview { get; set; }

    // The simulator stands in for the card, the dashcam and downloads, and nothing is remembered.
    public bool Simulated { get; set; }

    public void StartNextPass()
    {
        PassesDone++;
        Plan = null;
        Files = [];
        RecordingsSavedTo = null;
    }

    private static string VideosFolder()
    {
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        return string.IsNullOrEmpty(videos) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : videos;
    }
}
