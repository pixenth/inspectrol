using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Updates;

namespace Inspectrol.App.Pages;

// Offered only from the first screen, before anything has been done to the card or the dashcam.
public partial class AppUpdatePage : UserControl
{
    private readonly IWizard _wizard;
    private readonly AppUpdate _update;

    internal AppUpdatePage(IWizard wizard, AppUpdate update)
    {
        InitializeComponent();
        _wizard = wizard;
        _update = update;

        HeaderText.Text = string.Format(Strings.UpdatePage_Header, update.Version);
        SubheaderText.Text = string.Format(Strings.UpdatePage_Subheader, AboutInfo.Version);
        NotesText.Text = update.Notes ?? "";
        NotesCard.Visibility = update.Notes is null ? Visibility.Collapsed : Visibility.Visible;

        PrimaryButton.Assign(Strings.UpdatePage_Install, Install);
        SecondaryButton.Assign(Strings.UpdatePage_NotNow, () => _wizard.ShowCard(lookAgain: true));
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private async void Install()
    {
        _wizard.SetBusy(Strings.UpdatePage_Busy);
        PrimaryButton.Assign(null, null);
        SecondaryButton.Assign(null, null);
        ResultNotice.Visibility = Visibility.Collapsed;
        ProgressCard.Visibility = Visibility.Visible;
        StatusText.Text = Strings.UpdatePage_Downloading;
        DownloadBar.Value = 0;

        try
        {
            var folder = new DirectoryInfo(Path.Combine(_wizard.Session.DownloadsFolder, $"Inspectrol {_update.Version}"));
            var progress = new Progress<double>(value => DownloadBar.Value = value * 100);
            var setup = await UpdateDownloader.DownloadAsync(_wizard.Session.Http, _update, folder, progress, CancellationToken.None);

            StatusText.Text = Strings.UpdatePage_Installing;

            // /S installs silently, /RUN makes the installer start the new version when it is done.
            Process.Start(new ProcessStartInfo(setup.FullName, "/S /RUN") { UseShellExecute = true });

            _wizard.SetBusy(null);
            _wizard.CloseProgram();
        }
        catch (Exception error) when (error is InvalidDataException or HttpRequestException or IOException
                                           or TaskCanceledException or UnauthorizedAccessException or Win32Exception)
        {
            _wizard.SetBusy(null);
            ProgressCard.Visibility = Visibility.Collapsed;
            ResultNotice.Kind = NoticeKind.Warning;
            ResultText.Text = error is InvalidDataException ? error.Message : Strings.UpdatePage_Failed;
            ResultNotice.Visibility = Visibility.Visible;

            PrimaryButton.Assign(Strings.Common_TryAgain, Install);
            SecondaryButton.Assign(Strings.UpdatePage_NotNow, () => _wizard.ShowCard(lookAgain: true));
        }
    }
}
