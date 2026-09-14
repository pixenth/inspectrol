using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Cards;
using Inspectrol.Core.Downloading;

namespace Inspectrol.App.Pages;

public partial class DownloadPage : UserControl
{
    private readonly IWizard _wizard;
    private bool _started;

    internal DownloadPage(IWizard wizard)
    {
        InitializeComponent();
        _wizard = wizard;

        Loaded += async (_, _) =>
        {
            if (_started)
                return;

            _started = true;
            await DownloadAsync();
        };
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private async void Retry() => await DownloadAsync();

    private async Task DownloadAsync()
    {
        var step = _wizard.Session.CurrentStep;
        if (step is null)
            return;

        HeaderText.Text = Strings.DownloadPage_Header;
        SubheaderText.Text = string.Format(Strings.DownloadPage_Subheader, step.Title);
        FilesPanel.Children.Clear();
        ResultNotice.Visibility = Visibility.Collapsed;
        PrimaryButton.Assign(null, null);

        var folder = new DirectoryInfo(Path.Combine(
            _wizard.Session.DownloadsFolder,
            DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss")));

        var downloads = _wizard.Session.Downloads;
        var deviceFiles = new List<CardFile>();
        var failed = false;

        for (var index = 0; index < step.FileUrls.Count; index++)
        {
            var url = step.FileUrls[index];

            // Archive names mean nothing to the user, so the files are numbered instead.
            var row = AddFileRow(string.Format(Strings.DownloadPage_FileNumber, index + 1, step.FileUrls.Count));
            var inside = await DownloadOneAsync(downloads, url, folder, row);

            if (inside is null)
                failed = true;
            else
                deviceFiles.AddRange(inside);
        }

        if (failed || deviceFiles.Count == 0)
        {
            HeaderText.Text = Strings.DownloadPage_FailedHeader;
            ShowResult(NoticeKind.Warning,
                Strings.DownloadPage_FailedText);
            PrimaryButton.Assign(Strings.Common_TryAgain, Retry);
            return;
        }

        _wizard.Session.Files = deviceFiles;
        HeaderText.Text = Strings.DownloadPage_DoneHeader;
        ShowResult(NoticeKind.Success, Strings.DownloadPage_DoneText);
        PrimaryButton.Assign(Strings.Common_Next, _wizard.ShowRecordings);
    }

    private async Task<IReadOnlyList<CardFile>?> DownloadOneAsync(
        IUpdateDownloads downloads, string url, DirectoryInfo folder, (ProgressBar Bar, TextBlock Status) row)
    {
        var progress = new Progress<double>(value =>
        {
            row.Bar.Value = value * 100;
            row.Status.Text = string.Format(Strings.DownloadPage_Progress, value);
        });

        try
        {
            var file = await downloads.DownloadAsync(url, folder, progress, CancellationToken.None);
            row.Status.Text = Strings.DownloadPage_Checking;

            // Extraction doubles as verification: it confirms the archive holds dashcam files.
            var unpacked = new DirectoryInfo(Path.Combine(folder.FullName, Path.GetFileNameWithoutExtension(file.Name)));
            var inside = await Task.Run(() => downloads.Extract(file, unpacked));

            row.Bar.Value = 100;
            row.Status.Text = Strings.DownloadPage_FileIntact;
            row.Status.Foreground = TryFindResource("SystemFillColorSuccessBrush") as Brush ?? row.Status.Foreground;
            return inside;
        }
        catch (Exception error) when (error is InvalidDataException or HttpRequestException or IOException
                                           or TaskCanceledException or UnauthorizedAccessException)
        {
            row.Status.Text = error is InvalidDataException
                ? error.Message
                : Strings.DownloadPage_FileFailed;
            row.Status.Foreground = TryFindResource("SystemFillColorCriticalBrush") as Brush ?? row.Status.Foreground;
            return null;
        }
    }

    private void ShowResult(NoticeKind kind, string text)
    {
        ResultNotice.Kind = kind;
        ResultText.Text = text;
        ResultNotice.Visibility = Visibility.Visible;
    }

    private (ProgressBar Bar, TextBlock Status) AddFileRow(string label)
    {
        var bar = new ProgressBar { Height = 10, Minimum = 0, Maximum = 100, Margin = new Thickness(0, 10, 0, 8) };
        var status = new TextBlock
        {
            Text = Strings.DownloadPage_Downloading,
            Foreground = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
        };

        FilesPanel.Children.Add(new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    bar,
                    status,
                },
            },
        });

        return (bar, status);
    }
}
