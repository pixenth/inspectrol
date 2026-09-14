using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Inspectrol.Core;
using Inspectrol.Core.Devices;
using Inspectrol.Core.Simulation;

namespace Inspectrol.App.Simulator;

// Control panel for --simulator: the user plays the card and the dashcam while the wizard runs unchanged against
// SimulatedCard instead of real drives.
public partial class SimulatorWindow : Window
{
    private static readonly int[] SizesInGigabytes = [8, 16, 32, 64, 128, 256, 512];

    private readonly MainWindow _wizard;
    private readonly SimulatedCard _card;
    private bool _ready;
    private bool _contentChanged;

    private SimulatorWindow(MainWindow wizard, SimulatedCard card)
    {
        InitializeComponent();
        _wizard = wizard;
        _card = card;

        foreach (var dashcam in Enum.GetValues<SimulatedDashcam>())
            DashcamBox.Items.Add(new ComboBoxItem { Content = SimulatedCard.ModelName(dashcam), Tag = dashcam });

        foreach (var size in SizesInGigabytes)
            SizeBox.Items.Add(new ComboBoxItem { Content = string.Format(Strings.Simulator_SizeItem, size), Tag = size });

        DashcamBox.SelectedIndex = 0;
        SizeBox.SelectedIndex = Array.IndexOf(SizesInGigabytes, 128);
        FirmwareBox.Text = card.Firmware.ToString();
        RecordingsOption.IsChecked = true;
        FolderText.Text = string.Format(Strings.Simulator_Folder, card.Home.FullName);

        card.Changed += (_, _) => Dispatcher.InvokeAsync(Refresh);
        _ready = true;
        Refresh();
    }

    public static bool IsRequested() =>
        Environment.GetCommandLineArgs().Contains("--simulator", StringComparer.OrdinalIgnoreCase);

    public static void Attach(MainWindow wizard)
    {
        var card = SimulatedCard.Create(new DirectoryInfo(Path.Combine(Path.GetTempPath(), "Inspectrol simulator")));

        var session = wizard.Session;
        session.Simulated = true;
        session.FindCards = card.FindCards;
        session.CardStorage = new SimulatedCardStorage(card);
        session.Downloads = new SimulatedDownloads(session.Http, card);
        session.DownloadsFolder = Path.Combine(card.Home.FullName, "Downloads");
        session.RecordingsFolder = Path.Combine(card.Home.FullName, "Computer");
        wizard.Title += Strings.Simulator_TitleSuffix;

        var panel = new SimulatorWindow(wizard, card) { Owner = wizard };
        panel.PlaceNextTo(wizard);
        panel.Show();
    }

    private void PlaceNextTo(Window wizard)
    {
        const double gap = 8;
        var area = SystemParameters.WorkArea;

        if (wizard.Left + wizard.ActualWidth + gap + Width > area.Right)
            wizard.Left = Math.Max(area.Left, area.Right - wizard.ActualWidth - gap - Width);

        Left = Math.Min(wizard.Left + wizard.ActualWidth + gap, area.Right - Width);
        Top = wizard.Top;
        Height = Math.Min(wizard.ActualHeight, area.Height);
    }

    private void OnSettingChanged(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;

        _contentChanged = true;
        FirmwareHint.Visibility = FirmwareInput.Parse(FirmwareBox.Text) is null ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void OnInsertClick(object sender, RoutedEventArgs e)
    {
        if (FirmwareInput.Parse(FirmwareBox.Text) is not { } firmware)
            return;

        if (_contentChanged)
        {
            _card.Dashcam = (SimulatedDashcam)((ComboBoxItem)DashcamBox.SelectedItem).Tag;
            _card.Firmware = firmware;
            _card.TotalBytes = (int)((ComboBoxItem)SizeBox.SelectedItem).Tag * 1_000_000_000L;
            _card.Prepare(SelectedContent());
            _contentChanged = false;
        }

        await _card.InsertAsync();
    }

    private async void OnRemoveClick(object sender, RoutedEventArgs e) => await _card.RemoveAsync();

    private void OnDashcamClick(object sender, RoutedEventArgs e)
    {
        _card.RunInDashcam(InstallsUpdateBox.IsChecked == true, _wizard.Session.CurrentStep?.FirmwareAfter);

        _ready = false;
        FirmwareBox.Text = _card.Firmware.ToString();
        _ready = true;

        DashcamResultText.Text = string.Format(Strings.Simulator_CarDone, _card.Firmware);
    }

    private void OnConditionClick(object sender, RoutedEventArgs e)
    {
        _card.NeedsAdministrator = AdministratorBox.IsChecked == true;
        _card.AlmostFull = AlmostFullBox.IsChecked == true;
        _card.SecondDriveConnected = SecondDriveBox.IsChecked == true;
        _card.RealDownloads = RealDownloadsBox.IsChecked == true;
    }

    private void OnFormatClick(object sender, RoutedEventArgs e) => _card.FormatByHand();

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(_card.Home.FullName) { UseShellExecute = true });

    private SimulatedContent SelectedContent() =>
        EmptyOption.IsChecked == true ? SimulatedContent.Empty
        : UpdateFilesOption.IsChecked == true ? SimulatedContent.UpdateFiles
        : PhotosOption.IsChecked == true ? SimulatedContent.Photos
        : SimulatedContent.Recordings;

    private void Refresh()
    {
        CardStateText.Text = _card.Inserted ? Strings.Simulator_CardInComputer : Strings.Simulator_CardOut;
        CardContentsText.Text = _card.Summary();

        InsertButton.IsEnabled = !_card.Inserted;
        RemoveButton.IsEnabled = _card.Inserted;
        ContentPanel.IsEnabled = !_card.Inserted;
        DashcamButton.IsEnabled = !_card.Inserted;
        FormatButton.IsEnabled = _card.Inserted;
    }
}
