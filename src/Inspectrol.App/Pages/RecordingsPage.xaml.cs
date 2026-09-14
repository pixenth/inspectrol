using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Devices;
using Inspectrol.Core.Formatting;
using Microsoft.Win32;

namespace Inspectrol.App.Pages;

public partial class RecordingsPage : UserControl
{
    private enum Choice { SaveAll, LastDay, Range, SaveNothing }

    // Rough card reader speed. The estimate only has to tell minutes from hours.
    private const double MegabytesPerSecond = 25;

    private static string CopyingBusy => Strings.RecordingsPage_CopyingBusy;

    private readonly IWizard _wizard;
    private readonly DirectoryInfo _cardRoot;
    private readonly List<RadioButton> _options = [];

    private IReadOnlyList<FileInfo> _recordings = [];
    private Choice? _choice;
    private string _folder;
    private bool _read;

    internal RecordingsPage(IWizard wizard)
    {
        InitializeComponent();
        _wizard = wizard;
        _cardRoot = wizard.Session.CardRoot!;
        _folder = FolderIn(wizard.Session.RecordingsFolder);

        Loaded += (_, _) =>
        {
            if (_read)
                return;

            _read = true;
            ReadCard();
        };
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private void ReadCard()
    {
        SecondaryButton.Assign(Strings.Common_Back, _wizard.ShowPlan);

        // A removed card would look empty, and the backup would be skipped silently before the card is erased.
        if (!CardIsPresent())
        {
            ShowCardMissing();
            return;
        }

        _recordings = CardRecordings.Find(_cardRoot);
        var summary = RecordingsBackup.Summarise(_recordings);

        if (summary.Count == 0)
        {
            HeaderText.Text = Strings.RecordingsPage_NoRecordingsHeader;
            SummaryText.Text = Strings.RecordingsPage_NothingToSave;
            ChoicePanel.Visibility = Visibility.Collapsed;
            PrimaryButton.Assign(Strings.Common_Next, _wizard.ShowWrite);
            return;
        }

        HeaderText.Text = Strings.RecordingsPage_Header;
        SummaryText.Text = string.Format(
            Strings.RecordingsPage_Summary, Recordings(summary.Count), Size(summary.TotalBytes), summary.Oldest, summary.Newest);

        if (_options.Count == 0)
        {
            BuildOptions();
            FromPicker.SelectedDate = summary.Newest?.Date;
            ToPicker.SelectedDate = summary.Newest?.Date;
        }

        FolderText.Text = _folder;
        ChoicePanel.Visibility = Visibility.Visible;
        PrimaryButton.Assign(Strings.RecordingsPage_Continue, Continue, enabled: false);
        Refresh();
    }

    private bool CardIsPresent()
    {
        try
        {
            return new DriveInfo(_cardRoot.FullName).IsReady && Directory.Exists(_cardRoot.FullName);
        }
        catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void ShowCardMissing()
    {
        HeaderText.Text = Strings.RecordingsPage_CardMissingHeader;
        SummaryText.Text = Strings.RecordingsPage_CardMissingText;
        ChoicePanel.Visibility = Visibility.Collapsed;
        HintText.Text = "";
        PrimaryButton.Assign(Strings.RecordingsPage_CardIsBack, ReadCard);
    }

    private void BuildOptions()
    {
        var lastDay = RecordingsBackup.LastDay(_recordings);

        Add(Choice.SaveAll, Strings.RecordingsPage_SaveAllTitle, Describe(_recordings));
        Add(Choice.LastDay, Strings.RecordingsPage_LastDayTitle, Describe(lastDay));
        Add(Choice.Range, Strings.RecordingsPage_RangeOptionTitle, Strings.RecordingsPage_RangeOptionNote);
        Add(Choice.SaveNothing, Strings.RecordingsPage_SaveNothingTitle, Strings.RecordingsPage_SaveNothingNote);

        void Add(Choice choice, string title, string note)
        {
            var button = new RadioButton
            {
                GroupName = "Recordings",
                Padding = new Thickness(10, 0, 0, 0),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                        new TextBlock
                        {
                            Text = note,
                            Margin = new Thickness(0, 4, 0, 0),
                            Style = (Style)FindResource("SecondaryText"),
                        },
                    },
                },
            };
            button.Checked += (_, _) => OnChoice(choice);

            _options.Add(button);
            OptionsPanel.Children.Add(new Border
            {
                Style = (Style)FindResource("Card"),
                Margin = new Thickness(0, 0, 0, 12),
                Child = button,
            });
        }
    }

    private void OnChoice(Choice choice)
    {
        _choice = choice;
        RangeCard.Visibility = choice == Choice.Range ? Visibility.Visible : Visibility.Collapsed;
        SkipCard.Visibility = choice == Choice.SaveNothing ? Visibility.Visible : Visibility.Collapsed;
        FolderCard.Visibility = choice == Choice.SaveNothing ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.Content = choice == Choice.SaveNothing ? Strings.RecordingsPage_ContinueWithoutSaving : Strings.RecordingsPage_SaveRecordings;
        UpdateRangeText();
        Refresh();

        // The details of the choice appear below the fold.
        FrameworkElement details = choice switch
        {
            Choice.Range => RangeCard,
            Choice.SaveNothing => SkipCard,
            _ => FolderCard,
        };

        if (IsLoaded)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => details.BringIntoView());
    }

    private void OnRangeChanged(object? sender, SelectionChangedEventArgs e)
    {
        UpdateRangeText();
        Refresh();
    }

    private void OnSkipConsentChanged(object sender, RoutedEventArgs e) => Refresh();

    private void UpdateRangeText()
    {
        if (_choice != Choice.Range)
            return;

        RangeText.Text = ChosenFiles() is { Count: > 0 } files
            ? Describe(files)
            : Strings.RecordingsPage_NoRecordingsInRange;
    }

    private IReadOnlyList<FileInfo> ChosenFiles() => _choice switch
    {
        Choice.SaveAll => _recordings,
        Choice.LastDay => RecordingsBackup.LastDay(_recordings),
        Choice.Range when FromPicker.SelectedDate is { } from && ToPicker.SelectedDate is { } to && from <= to =>
            RecordingsBackup.Between(_recordings, from, to),
        _ => [],
    };

    private void Refresh()
    {
        PrimaryButton.IsEnabled = _choice switch
        {
            Choice.SaveNothing => SkipConsent.IsChecked == true,
            Choice.Range => ChosenFiles().Count > 0,
            null => false,
            _ => true,
        };

        HintText.Text = _choice switch
        {
            null => Strings.RecordingsPage_ChooseHint,
            Choice.SaveNothing when SkipConsent.IsChecked != true => Strings.RecordingsPage_SkipHint,
            Choice.Range when ChosenFiles().Count == 0 => Strings.RecordingsPage_RangeHint,
            _ => "",
        };
    }

    private void OnChooseFolderClick(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFolderDialog
        {
            Title = Strings.RecordingsPage_FolderDialogTitle,
            InitialDirectory = _wizard.Session.RecordingsFolder,
        };

        if (picker.ShowDialog(Window.GetWindow(this)) != true)
            return;

        _folder = FolderIn(picker.FolderName);
        FolderText.Text = _folder;
    }

    private async void Continue()
    {
        if (_choice is null)
            return;

        if (_choice == Choice.SaveNothing)
        {
            if (SkipConsent.IsChecked == true)
                _wizard.ShowWrite();
            return;
        }

        if (!CardIsPresent())
        {
            ShowCardMissing();
            return;
        }

        var files = ChosenFiles();
        var target = new DirectoryInfo(_folder);

        _wizard.SetBusy(CopyingBusy);
        ChoicePanel.Visibility = Visibility.Collapsed;
        ResultNotice.Visibility = Visibility.Collapsed;
        CopyPanel.Visibility = Visibility.Visible;
        CopyBar.Value = 0;
        CopyPercent.Text = "";
        HintText.Text = "";
        CopyText.Text = string.Format(Strings.RecordingsPage_Copying, Recordings(files.Count), target.FullName);
        PrimaryButton.Assign(null, null);
        SecondaryButton.Assign(null, null);

        var progress = new Progress<double>(value =>
        {
            CopyBar.Value = value * 100;
            CopyPercent.Text = string.Format(Strings.RecordingsPage_CopyProgress, value);
        });

        try
        {
            await RecordingsBackup.CopyAsync(files, target, progress, CancellationToken.None);

            _wizard.SetBusy(null);
            _wizard.Session.RecordingsSavedTo = target.FullName;

            HeaderText.Text = Strings.RecordingsPage_SavedHeader;
            SummaryText.Text = Strings.RecordingsPage_CardNotChangedYet;
            CopyPanel.Visibility = Visibility.Collapsed;
            ResultNotice.Kind = NoticeKind.Success;
            ResultText.Text = string.Format(Strings.RecordingsPage_Saved, Recordings(files.Count), target.FullName);
            OpenFolderButton.Visibility = Visibility.Visible;
            ResultNotice.Visibility = Visibility.Visible;
            PrimaryButton.Assign(Strings.Common_Next, _wizard.ShowWrite);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _wizard.SetBusy(null);

            HeaderText.Text = Strings.RecordingsPage_SaveFailedHeader;
            CopyPanel.Visibility = Visibility.Collapsed;
            ChoicePanel.Visibility = Visibility.Visible;
            ResultNotice.Kind = NoticeKind.Warning;
            ResultText.Text = string.Format(Strings.RecordingsPage_SaveFailed, error.Message);
            OpenFolderButton.Visibility = Visibility.Collapsed;
            ResultNotice.Visibility = Visibility.Visible;
            PrimaryButton.Assign(Strings.Common_TryAgain, Continue);
            SecondaryButton.Assign(Strings.Common_Back, _wizard.ShowPlan);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_folder) { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        {
            ResultText.Text += Strings.RecordingsPage_OpenFolderFailed;
        }
    }

    private static string FolderIn(string parent) =>
        Path.Combine(parent, string.Format(Strings.RecordingsPage_FolderName, DateTime.Now));

    private static string Describe(IReadOnlyList<FileInfo> files)
    {
        var bytes = files.Sum(file => file.Length);
        var minutes = bytes / 1024d / 1024d / MegabytesPerSecond / 60;

        var time = minutes switch
        {
            < 1 => Strings.RecordingsPage_TimeUnderMinute,
            < 60 => string.Format(Strings.RecordingsPage_TimeMinutes, Math.Round(minutes)),
            _ => string.Format(Strings.RecordingsPage_TimeHours, Math.Round(minutes / 60, 1)),
        };

        return string.Format(Strings.RecordingsPage_Estimate, Recordings(files.Count), Size(bytes), time);
    }

    private static string Recordings(long count) => RussianPlural.Format(count, Strings.Common_RecordingForms);

    private static string Size(long bytes) => bytes >= 1024L * 1024 * 1024
        ? string.Format(Strings.RecordingsPage_SizeGigabytes, bytes / 1024d / 1024d / 1024d)
        : string.Format(Strings.RecordingsPage_SizeMegabytes, bytes / 1024d / 1024d);
}
