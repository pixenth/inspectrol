using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Cards;
using Inspectrol.Core.Formatting;

namespace Inspectrol.App.Pages;

public partial class WritePage : UserControl
{
    private static string WritingBusy => Strings.WritePage_WritingBusy;

    private static string WrittenButNotUpdated => Strings.WritePage_WrittenButNotUpdated;

    private readonly IWizard _wizard;
    private readonly ICardStorage _storage;
    private readonly IReadOnlyList<CardFile> _files;
    private readonly DirectoryInfo _cardRoot;
    private readonly string _modelName;

    private CardSnapshot? _confirmed;

    internal WritePage(IWizard wizard)
    {
        InitializeComponent();
        _wizard = wizard;

        var session = wizard.Session;
        _storage = session.CardStorage;
        _files = session.Files;
        _cardRoot = session.CardRoot!;
        _modelName = session.Model!.Name;

        ModelText.Text = string.Format(Strings.WritePage_StepForModel, session.CurrentStep?.Title, _modelName);
        FilesText.Text = string.Format(Strings.WritePage_Files, _files.Count, Size(_files.Sum(file => file.Length)));

        PrimaryButton.Assign(Strings.WritePage_EraseAndWrite, Write, enabled: false);
        ShowCard();
    }

    private string DriveName => _cardRoot.Name.TrimEnd('\\');

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private void ShowCard()
    {
        // Windows also reports a removed card as not removable. Only removable drives reach this step, so the card
        // was most likely taken out.
        if (!_storage.IsRemovable(_cardRoot))
        {
            Refuse(Strings.WritePage_CardNotVisible,
                Strings.WritePage_CardBack, _wizard.ShowWrite);
            return;
        }

        try
        {
            _confirmed = _storage.Describe(_cardRoot);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Refuse(Strings.WritePage_CardMissing,
                Strings.WritePage_CardBack, _wizard.ShowWrite);
            return;
        }

        var label = string.IsNullOrWhiteSpace(_confirmed.Label) ? "" : string.Format(Strings.WritePage_CardLabel, _confirmed.Label);
        CardText.Text = string.Format(Strings.WritePage_Card, DriveName, _confirmed.TotalBytes / 1_000_000_000d, label);

        if (CardRequirements.For(_modelName).Check(_confirmed.TotalBytes) is { } problem)
        {
            Refuse(problem, Strings.WritePage_InsertAnotherCard, () => _wizard.ShowCard(lookAgain: true));
            return;
        }

        ShowContents();

        if (_wizard.Session.RecordingsSavedTo is { } saved)
        {
            SavedText.Text = string.Format(Strings.WritePage_RecordingsSavedTo, saved);
            SavedText.Visibility = Visibility.Visible;
        }
    }

    private void ShowContents(bool willBeErased = true)
    {
        try
        {
            // Count files too: after an earlier update the card may hold only firmware files in the root.
            var entries = _cardRoot.EnumerateFileSystemInfos()
                .Count(entry => (entry.Attributes & (FileAttributes.System | FileAttributes.Hidden)) == 0);

            var recordings = _confirmed?.RecordingCount ?? 0;

            var count = RussianPlural.Format(recordings, Strings.Common_RecordingForms);
            CardContentsText.Text = recordings > 0
                ? string.Format(willBeErased ? Strings.WritePage_RecordingsWillBeErased : Strings.WritePage_RecordingsOnCard, count)
                : entries > 0
                    ? willBeErased ? Strings.WritePage_FilesWillBeErased : Strings.WritePage_FilesOnCard
                    : Strings.WritePage_CardLooksEmpty;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            CardContentsText.Text = Strings.WritePage_ContentsUnreadable;
        }
    }

    private void Refuse(string problem, string actionText, Action action)
    {
        ShowProblem(NoticeKind.Warning, problem, details: null);
        ConsentCard.Visibility = Visibility.Collapsed;
        PrimaryButton.Assign(actionText, action);
    }

    private void ShowProblem(NoticeKind kind, string text, string? details)
    {
        ProblemNotice.Kind = kind;
        ProblemText.Text = text;
        ProblemDetails.Text = details is null ? "" : string.Format(Strings.WritePage_TechnicalDetails, details);
        ProblemDetails.Visibility = details is null ? Visibility.Collapsed : Visibility.Visible;
        ProblemNotice.Visibility = Visibility.Visible;
    }

    private void OnConsentChanged(object sender, RoutedEventArgs e) =>
        PrimaryButton.IsEnabled = EraseConsent.IsChecked == true && _confirmed is not null;

    private async void Write()
    {
        if (_confirmed is null || EraseConsent.IsChecked != true || !WritingAllowed())
            return;

        Busy(Strings.WritePage_Erasing);

        var result = await new CardWriter(_storage).WriteAsync(
            _files, _cardRoot, _confirmed, _modelName, Progress(), CancellationToken.None);

        Done(result);
    }

    private async void WriteWithoutErasing()
    {
        if (_confirmed is null || !WritingAllowed())
            return;

        Busy(Strings.WritePage_WritingWithoutErasing);

        var result = await new CardWriter(_storage).WriteWithoutErasingAsync(
            _files, _cardRoot, _confirmed, _modelName, Progress(), CancellationToken.None);

        Done(result);
    }

    private async void WriteToPreparedCard()
    {
        if (_confirmed is null || !WritingAllowed())
            return;

        Busy(Strings.WritePage_WritingToPreparedCard);

        var result = await new CardWriter(_storage).WriteToPreparedCardAsync(
            _files, _cardRoot, _confirmed.TotalBytes, _modelName, Progress(), CancellationToken.None);

        Done(result);
    }

    // The screen preview runs on made-up data and must never write to a real card.
    private bool WritingAllowed()
    {
        if (!_wizard.Session.Preview)
            return true;

        WritePanel.Visibility = Visibility.Visible;
        StatusText.Text = Strings.WritePage_PreviewMode;
        return false;
    }

    private IProgress<double> Progress() => new Progress<double>(value => WriteBar.Value = value * 100);

    private void Busy(string status)
    {
        _wizard.SetBusy(WritingBusy);

        PrimaryButton.IsEnabled = false;
        EraseConsent.IsEnabled = false;
        OpenComputerButton.IsEnabled = false;
        ProblemNotice.Visibility = Visibility.Collapsed;

        WritePanel.Visibility = Visibility.Visible;
        WriteBar.Value = 0;
        StatusText.Text = status;
    }

    private void Done(CardWriteResult result)
    {
        _wizard.SetBusy(null);
        WritePanel.Visibility = Visibility.Collapsed;
        ConsentCard.Visibility = Visibility.Collapsed;
        FallbackCard.Visibility = Visibility.Collapsed;

        if (result.Success)
        {
            _wizard.SetCloseQuestion(WrittenButNotUpdated);

            HeaderText.Text = Strings.WritePage_ReadyHeader;
            SubheaderText.Text = Strings.WritePage_ReadySubheader;

            // The card description was taken before writing and is stale now.
            CardInfoCard.Visibility = Visibility.Collapsed;
            WhatTitle.Text = Strings.WritePage_WhatWasWritten;

            ShowProblem(NoticeKind.Success, Strings.WritePage_ReadyText, details: null);
            PrimaryButton.Assign(Strings.Common_Next, _wizard.ShowCarInstruction);
            return;
        }

        HeaderText.Text = result.NeedsAdministrator ? Strings.WritePage_EraseFailedHeader : Strings.WritePage_WriteFailedHeader;

        // The subheader says the card is about to be erased, which is no longer true.
        SubheaderText.Visibility = Visibility.Collapsed;

        if (result.CardAlreadyErased)
            CardContentsText.Text = Strings.WritePage_CardErased;
        else
            ShowContents(willBeErased: false);

        var problem = result.CardAlreadyErased
            ? string.Format(Strings.WritePage_FailedAfterErase, result.Problem)
            : result.FilesPartlyWritten
                ? string.Format(Strings.WritePage_FailedPartlyWritten, result.Problem)
                : string.Format(Strings.WritePage_FailedCardUnchanged, result.Problem);

        var kind = result.CardAlreadyErased || result.FilesPartlyWritten ? NoticeKind.Critical : NoticeKind.Warning;
        ShowProblem(kind, problem, result.Details);

        if (result.NeedsAdministrator)
        {
            OfferFallback();
            return;
        }

        PrimaryButton.Assign(Strings.Common_TryAgain, _wizard.ShowWrite);
    }

#if DEBUG
    internal void PreviewResult(CardWriteResult result) => Done(result);
#endif

    private void OfferFallback()
    {
        var needed = _files.Sum(file => file.Length);
        var free = _storage.FreeBytes(_cardRoot);
        var enough = free >= needed;

        KeepFilesText.Text = enough
            ? string.Format(Strings.WritePage_EnoughSpace, Size(needed), Size(free))
            : string.Format(Strings.WritePage_NotEnoughSpace, Size(needed), Size(free));

        var fileSystem = _confirmed is not null
            && CardRequirements.For(_modelName).FileSystemFor(_confirmed.TotalBytes) == CardFileSystem.Fat32
                ? "FAT32"
                : "exFAT";
        var gigabytes = _confirmed is null ? "" : string.Format(Strings.WritePage_Gigabytes, _confirmed.TotalBytes / 1_000_000_000d);

        ManualFormatText.Text = string.Format(Strings.WritePage_ManualFormatSteps, DriveName, gigabytes, fileSystem);
        ManualSection.Visibility = enough ? Visibility.Collapsed : Visibility.Visible;
        OpenComputerButton.IsEnabled = true;
        FallbackCard.Visibility = Visibility.Visible;

        PrimaryButton.Assign(
            enough ? Strings.WritePage_WriteWithoutErasing : Strings.WritePage_ManualFormatDone,
            enough ? WriteWithoutErasing : WriteToPreparedCard);

        // The explanation, or the manual formatting steps, must be read before pressing the button.
        FrameworkElement details = enough ? FallbackCard : ManualSection;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => details.BringIntoView());
    }

    private void OnOpenComputerClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "shell:MyComputerFolder") { UseShellExecute = true });
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception)
        {
            ManualFormatText.Text += Strings.WritePage_OpenThisPcFailed;
        }
    }

    // Decimal units, as printed on card labels.
    private static string Size(long bytes) => bytes >= 1_000_000_000
        ? string.Format(Strings.WritePage_Gigabytes, bytes / 1_000_000_000d)
        : string.Format(Strings.WritePage_Megabytes, bytes / 1_000_000);
}
