#if DEBUG
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Inspectrol.App.Pages;
using Inspectrol.Core.Cards;
using Inspectrol.Core.Catalog;
using Inspectrol.Core.Devices;
using Inspectrol.Core.Planning;

namespace Inspectrol.App.Preview;

/// <summary>
/// Opens a wizard step on made-up data, so a screen can be reviewed without a card or a dashcam.
/// </summary>
/// <remarks>
/// <code>dotnet run --project src/Inspectrol.App -- --preview=SCREEN[:OPTION] [--theme=light|dark] [--scroll=end]</code>
/// Screens: card, card-found, card-no-version, card-confirm, card-unknown, card-manual, models,
/// plan[:PART], download, recordings[:CHOICE], write[:admin|low-space|erased|done], car, device[:INDEX], finish, fail.
/// PART and CHOICE are 1-based positions of the option to select; INDEX is the 0-based question number.
/// </remarks>
internal static class ScreenPreview
{
    private const string AdministratorMessage = "Windows не дала очистить карту без прав администратора.";

    private static readonly CatalogModel AtlaS = new("Видеорегистраторы", "Inspector Atlas", []);
    private static readonly CatalogModel Barracuda = new("Комбо-устройства", "Inspector Barracuda", []);

    private static string Folder => Path.Combine(Path.GetTempPath(), "Inspectrol preview");

    public static async Task<bool> TryShowAsync(MainWindow window)
    {
        var arguments = Environment.GetCommandLineArgs();
        if (Option(arguments, "--preview=") is not { } preview)
            return false;

        Application.Current.ThemeMode = Option(arguments, "--theme=") switch
        {
            "light" => ThemeMode.Light,
            "dark" => ThemeMode.Dark,
            _ => Application.Current.ThemeMode,
        };

        var session = window.Session;
        session.Preview = true;
        session.Model = AtlaS;
        session.Firmware = new Version(1, 0, 3);
        session.Plan = new UpdatePlan(
            [new PlanStep("Прошивка 1.0.5 и база камер от 09.09.2026",
                ["https://example.invalid/AtlaS_1.0.5.zip", "https://example.invalid/AtlaSDB_37.zip"],
                new Version(1, 0, 5))],
            Blocker: null);

        var parts = preview.Split(':', 2);
        if (!await ShowAsync(window, parts[0], parts.Length > 1 ? parts[1] : null))
            return false;

        if (Option(arguments, "--scroll=") == "end")
        {
            await Task.Delay(TimeSpan.FromSeconds(1));
            foreach (var scroller in Descendants<ScrollViewer>(window.PageHost.Content))
                scroller.ScrollToEnd();
        }

        return true;
    }

    private static async Task<bool> ShowAsync(MainWindow window, string screen, string? option)
    {
        var session = window.Session;
        var number = int.TryParse(option, out var parsed) ? parsed : 0;

        switch (screen)
        {
            case "card":
                window.ShowCard(lookAgain: true);
                return true;

            case "card-found":
                window.ShowCard(lookAgain: false);
                window.CardPage.PreviewRecognition(Recognised(AtlaS, new Version(1, 0, 3)), Card());
                return true;

            case "card-no-version":
                window.ShowCard(lookAgain: false);
                window.CardPage.PreviewRecognition(Recognised(Barracuda, firmware: null), Card());
                return true;

            case "card-confirm":
                window.ShowCard(lookAgain: false);
                window.CardPage.PreviewRecognition(Recognised(AtlaS, new Version(1, 0, 3)), Card(), typedFirmware: "1.0.4");
                return true;

            case "card-unknown":
                window.ShowCard(lookAgain: false);
                var emptyCard = Directory.CreateDirectory(Path.Combine(Folder, "Empty card"));
                window.CardPage.PreviewRecognition(DeviceRecognition.Recognise(emptyCard, [AtlaS, Barracuda]), Card());
                return true;

            case "card-manual":
                window.ShowCard(lookAgain: false);
                window.CardPage.PreviewChosenByHand(AtlaS, Card());
                return true;

            case "models":
                session.Models = await new InspectorCatalog(session.Http).GetModelsAsync(CancellationToken.None);
                window.ShowModelPicker();
                return true;

            case "plan":
                session.Models = await new InspectorCatalog(session.Http).GetModelsAsync(CancellationToken.None);
                session.Model = session.Models.First(model => model.Name.Contains("AtlaS", StringComparison.OrdinalIgnoreCase));
                window.ShowPlan();
                await SelectAsync<CheckBox>(window, number);
                return true;

            case "download":
                window.ShowDownload();
                return true;

            case "recordings":
                session.CardRoot = CardWithRecordings();
                window.ShowRecordings();
                await SelectAsync<RadioButton>(window, number);
                return true;

            case "write":
                await ShowWriteAsync(window, option);
                return true;

            case "car":
                window.ShowCarInstruction();
                return true;

            case "device":
                window.ShowDeviceAction(number);
                return true;

            case "finish":
                session.ConfirmedFirmware = new Version(1, 0, 5);
                window.ShowFinish(success: true, problem: null, backToAction: null);
                return true;

            case "fail":
                var questions = CarInstructions.Questions(DeviceProcedure.For(session.CurrentStep!, 1, 1));
                window.ShowFinish(success: false, questions[^1].GiveUp, backToAction: questions.Count - 1);
                return true;

            default:
                return false;
        }
    }

    // Runs the real CardWriter against PreviewCardStorage to get the exact result the page would receive.
    private static async Task ShowWriteAsync(MainWindow window, string? outcome)
    {
        var session = window.Session;
        var storage = new PreviewCardStorage(
            freeBytes: outcome == "low-space" ? 10_000_000 : 60_000_000_000,
            needsAdministrator: outcome is "admin" or "low-space",
            copyFails: outcome == "erased");

        session.CardStorage = storage;
        session.CardRoot = CardWithRecordings();
        session.Files = UpdateFiles();
        window.ShowWrite();

        if (outcome is null || window.PageHost.Content is not WritePage page)
            return;

        var result = await new CardWriter(storage).WriteAsync(
            session.Files,
            session.CardRoot,
            storage.Describe(session.CardRoot),
            session.Model!.Name,
            progress: null,
            CancellationToken.None);

        page.PreviewResult(result);
    }

    private static CardRecognition Recognised(CatalogModel model, Version? firmware) =>
        CardRecognition.Recognised(new CardDevice(model, firmware, RecognitionConfidence.Guessed));

    private static RemovableCard Card() =>
        new(new DirectoryInfo(@"F:\"), Label: "", TotalBytes: 128_000_000_000, FreeBytes: 60_000_000_000, LooksLikeDashcam: true);

    // Laid out like an AtlaS card.
    private static DirectoryInfo CardWithRecordings()
    {
        var root = new DirectoryInfo(Path.Combine(Folder, "F"));
        var media = root.CreateSubdirectory(Path.Combine("DCIM", "100MEDIA"));
        var start = new DateTime(2026, 9, 10, 8, 0, 0);

        for (var index = 0; index < 36; index++)
        {
            var time = start.AddDays(index / 12).AddMinutes(index % 12 * 3);
            var file = new FileInfo(Path.Combine(media.FullName, $"{time:MMddHHmm}_{index + 1:0000}.MP4"));

            if (!file.Exists)
            {
                using var stream = file.Create();
                stream.SetLength(256 * 1024);
            }

            file.LastWriteTime = time;
        }

        return root;
    }

    // Names and sizes of the files in the real AtlaS 1.0.5 update.
    private static IReadOnlyList<CardFile> UpdateFiles()
    {
        var folder = Directory.CreateDirectory(Path.Combine(Folder, "Files"));
        (string Name, long Bytes)[] entries =
        [
            ("firmware.bin", 70_863_276),
            ("firmware2.bin", 70_863_276),
            ("rdfw.bin", 67_697),
            ("AtlaSDB.bin", 3_075_579),
        ];

        return [.. entries.Select(entry =>
        {
            var file = new FileInfo(Path.Combine(folder.FullName, entry.Name));
            if (!file.Exists)
            {
                using var stream = file.Create();
                stream.SetLength(entry.Bytes);
            }

            file.Refresh();
            return CardFile.InRoot(file);
        })];
    }

    // Pages build their choices after loading data, so wait for them to appear.
    private static async Task SelectAsync<T>(MainWindow window, int position) where T : ToggleButton
    {
        if (position < 1)
            return;

        for (var attempt = 0; attempt < 120; attempt++)
        {
            var choices = Descendants<T>(window.PageHost.Content).ToList();
            if (choices.Count >= position)
            {
                choices[position - 1].IsChecked = true;
                return;
            }

            await Task.Delay(250);
        }
    }

    private static IEnumerable<T> Descendants<T>(object? node) where T : DependencyObject
    {
        if (node is not DependencyObject parent)
            yield break;

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is T match)
                yield return match;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static string? Option(string[] arguments, string prefix) =>
        arguments.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];

    private sealed class PreviewCardStorage(long freeBytes, bool needsAdministrator, bool copyFails) : ICardStorage
    {
        public CardSnapshot Describe(DirectoryInfo root) => new(128_000_000_000, "", "exFAT", 36, "09121833_0036.MP4");

        public bool IsRemovable(DirectoryInfo root) => true;

        public bool IsEmpty(DirectoryInfo root) => false;

        public long FreeBytes(DirectoryInfo root) => freeBytes;

        public Task FormatAsync(DirectoryInfo root, CardFileSystem fileSystem, CancellationToken cancellationToken) =>
            needsAdministrator
                ? Task.FromException(new CardNeedsAdministratorException(AdministratorMessage))
                : Task.CompletedTask;

        public Task CopyToCardAsync(
            IReadOnlyList<CardFile> files,
            DirectoryInfo root,
            IProgress<double>? progress,
            CancellationToken cancellationToken) =>
            copyFails ? Task.FromException(new IOException("Устройство не готово.")) : Task.CompletedTask;

        public Task<bool> VerifyAsync(IReadOnlyList<CardFile> files, DirectoryInfo root, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<bool> TryEjectAsync(DirectoryInfo root, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
#endif
