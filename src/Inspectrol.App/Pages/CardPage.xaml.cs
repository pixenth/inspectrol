using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Cards;
using Inspectrol.Core.Catalog;
using Inspectrol.Core.Devices;
using Inspectrol.Core.Updates;

namespace Inspectrol.App.Pages;

public partial class CardPage : UserControl
{
    private static string DefaultHeader => Strings.CardPage_DefaultHeader;

    private static string DefaultSubheader => Strings.CardPage_DefaultSubheader;

    private readonly IWizard _wizard;
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(2) };

    private string? _drivesSeen;
    private bool _searching;
    private RemovableCard? _card;
    private CatalogModel? _chosenModel;
    private Version? _detectedFirmware;
    private bool _versionConfirmed;
    private AppUpdate? _update;

    internal CardPage(IWizard wizard)
    {
        InitializeComponent();
        _wizard = wizard;

        HeaderText.Text = DefaultHeader;
        SubheaderText.Text = DefaultSubheader;

        _watch.Tick += OnWatchTick;
        Loaded += (_, _) => _watch.Start();
        Unloaded += (_, _) => _watch.Stop();
    }

    private WizardSession Session => _wizard.Session;

    private Visibility FirstScreenNotice => Session.PassesDone == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    public async void LookForCard() => await LookForCardAsync();

    public void OfferUpdate(AppUpdate update)
    {
        _update = update;
        // A portable copy cannot be updated by the installer, which would install a second copy elsewhere.
        UpdateText.Text = string.Format(
            AboutInfo.IsPortable ? Strings.CardPage_UpdateAvailablePortable : Strings.CardPage_UpdateAvailable,
            update.Version);
        UpdateButton.Visibility = AboutInfo.IsPortable ? Visibility.Collapsed : Visibility.Visible;
        UpdateNotice.Visibility = FirstScreenNotice;
    }

    private void OnUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_update is not null)
            _wizard.ShowAppUpdate(_update);
    }

    public void ShowChosenByHand(CatalogModel model)
    {
        if (_card is null)
        {
            LookForCard();
            return;
        }

        _chosenModel = model;
        _detectedFirmware = null;
        _versionConfirmed = false;

        HeaderText.Text = Strings.CardPage_ChosenByHandHeader;
        SubheaderText.Text = Strings.CardPage_ChosenByHandSubheader;

        DeviceTitle.Text = model.Name;
        DeviceFirmware.Text = Strings.CardPage_ChosenFromList;
        DeviceConfidence.Text = Strings.CardPage_CheckStickerSimilarModels;

        FirmwareBox.Text = "";
        ShowDevice();
        PrimaryButton.Assign(Strings.Common_Next, Continue);
        SecondaryButton.Assign(Strings.CardPage_ChooseAnotherModel, _wizard.ShowModelPicker);
        UpdateContinueState();
    }

    private async void OnWatchTick(object? sender, EventArgs e)
    {
        if (_searching || _drivesSeen is null)
            return;

        var cards = await Task.Run(Session.FindCards);
        var drives = Signature(cards);
        if (!IsLoaded || _searching || drives == _drivesSeen)
            return;

        // The user may be typing a version into the shown card. Another drive appearing must not trigger a rescan
        // that discards the input, so rescan only when this card is gone or replaced.
        if (_card is { } shown && cards.Any(c => c.Root.FullName == shown.Root.FullName && c.TotalBytes == shown.TotalBytes))
        {
            _drivesSeen = drives;
            return;
        }

        await LookForCardAsync();
    }

    private static string Signature(IReadOnlyList<RemovableCard> cards) =>
        string.Join("|", cards.Select(card => $"{card.Root.FullName}:{card.TotalBytes}:{card.LooksLikeDashcam}"));

    private async Task LookForCardAsync()
    {
        if (_searching)
            return;

        _searching = true;
        try
        {
            await SearchAsync();
        }
        finally
        {
            _searching = false;
        }
    }

    private async Task SearchAsync()
    {
        // Forget the previous card: another drive, such as a flash drive with photos, may now have the same letter
        // and must not be erased through the old path.
        Session.CardRoot = null;
        _card = null;
        _chosenModel = null;
        _detectedFirmware = null;
        _versionConfirmed = false;

        Busy(Strings.CardPage_SearchingTitle, Strings.CardPage_SearchingText);

        var cards = await Task.Run(Session.FindCards);
        _drivesSeen = Signature(cards);

        if (Session.PassesDone > 0)
        {
            ContinueChain(cards);
            return;
        }

        if (cards.Count == 0)
        {
            Idle(
                Strings.CardPage_NotFoundTitle,
                Strings.CardPage_NotFoundText);
            HowToPanel.Visibility = Visibility.Visible;
            return;
        }

        // The chosen card may get erased later, so never guess between several.
        var dashcams = cards.Where(c => c.LooksLikeDashcam).ToList();
        if (dashcams.Count > 1)
        {
            Idle(
                Strings.CardPage_SeveralDashcamCardsTitle,
                string.Format(Strings.CardPage_SeveralDashcamCardsText, dashcams.Count, string.Join(", ", dashcams.Select(c => DriveName(c)))));
            return;
        }

        if (dashcams.Count == 0 && cards.Count > 1)
        {
            Idle(
                Strings.CardPage_SeveralDrivesTitle,
                string.Format(Strings.CardPage_SeveralDrivesText, cards.Count, string.Join(", ", cards.Select(c => DriveName(c)))));
            return;
        }

        var card = dashcams.Count == 1 ? dashcams[0] : cards[0];

        // A card without recordings may be new or already hold update files. A drive with unrelated files, such as
        // photos, is rejected because the card may get erased later.
        if (!card.LooksLikeDashcam
            && await Task.Run(() => CardContents.Classify(card.Root)) == CardContentKind.OtherFiles)
        {
            Idle(
                string.Format(Strings.CardPage_OtherFilesTitle, DriveName(card), Gigabytes(card)),
                Strings.CardPage_OtherFilesText);
            return;
        }

        Busy(
            string.Format(Strings.CardPage_ReadingTitle, DriveName(card), Gigabytes(card)),
            Strings.CardPage_ReadingText);

        IReadOnlyList<CatalogModel> models;
        try
        {
            models = Session.Models ??= await new InspectorCatalog(Session.Http).GetModelsAsync(CancellationToken.None);
        }
        catch (Exception error) when (error is CatalogFormatException or HttpRequestException or TaskCanceledException)
        {
            Idle(
                Strings.CardPage_NoConnectionTitle,
                Strings.CardPage_NoConnectionText,
                retryText: Strings.Common_TryAgain);
            return;
        }

        var recognition = await Task.Run(() => DeviceRecognition.Recognise(card.Root, models));

        _card = card;
        Session.CardRoot = card.Root;
        ShowRecognition(recognition, card);
    }

    private void ShowRecognition(CardRecognition recognition, RemovableCard card)
    {
        BusyBar.Visibility = Visibility.Collapsed;
        HowToPanel.Visibility = Visibility.Collapsed;
        UnofficialNotice.Visibility = Visibility.Collapsed;
        StatusTitle.Text = string.Format(Strings.CardPage_CardTitle, DriveName(card), Gigabytes(card));
        StatusText.Text = Strings.CardPage_CardRead;

        if (recognition.Device is null)
        {
            HeaderText.Text = Strings.CardPage_NotRecognisedHeader;
            SubheaderText.Text = recognition.Explanation;

            ResultNotice.Visibility = Visibility.Collapsed;
            DeviceCard.Visibility = Visibility.Collapsed;

            PrimaryButton.Assign(Strings.CardPage_ChooseFromList, _wizard.ShowModelPicker);
            SecondaryButton.Assign(null, null);
            return;
        }

        var device = recognition.Device;
        _chosenModel = device.Model;
        _detectedFirmware = device.Firmware;

        HeaderText.Text = Strings.CardPage_RecognisedHeader;
        SubheaderText.Text = device.FromUpdateFiles
            ? Strings.CardPage_RecognisedFromUpdateFiles
            : Strings.CardPage_RecognisedFromRecordings;

        DeviceTitle.Text = device.Model.Name;

        // Barracuda recordings carry only the model name and update files carry no version. Never guess the version:
        // it decides which updates may be installed.
        DeviceFirmware.Text = device.Firmware is not null
            ? string.Format(Strings.CardPage_CurrentFirmware, device.Firmware)
            : device.FromUpdateFiles
                ? Strings.CardPage_NoVersionInUpdateFiles
                : Strings.CardPage_NoVersionInRecordings;

        DeviceConfidence.Text = device.Firmware is null
            ? Strings.CardPage_LookUpVersion
              + (device.FromUpdateFiles ? Strings.CardPage_LookUpVersionAfterUpdate : "")
            : device.Confidence == RecognitionConfidence.Verified
                ? Strings.CardPage_ModelVerified
                : Strings.CardPage_VersionMayDiffer;

        FirmwareBox.Text = device.Firmware?.ToString() ?? "";
        ShowDevice();
        PrimaryButton.Assign(Strings.CardPage_YesThisIsIt, Continue);
        SecondaryButton.Assign(Strings.CardPage_NoChooseFromList, _wizard.ShowModelPicker);
        UpdateContinueState();
    }

    // Recordings are not read on later passes: after erasing there are none, and without erasing the old ones still
    // carry the previous version, which would plan the first pass again.
    private void ContinueChain(IReadOnlyList<RemovableCard> cards)
    {
        if (cards.Count != 1)
        {
            Idle(
                cards.Count == 0 ? Strings.CardPage_InsertCardBackTitle : Strings.CardPage_SeveralCardsTitle,
                cards.Count == 0
                    ? Strings.CardPage_InsertCardBackText
                    : Strings.CardPage_LeaveOneCardText);
            HowToPanel.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        var card = cards[0];

        // Model and version come from the session here, so the size is the only check that the same card is back
        // and a different one is not offered for erasing.
        if (Session.CardBytes is { } expected && expected != card.TotalBytes)
        {
            Idle(
                Strings.CardPage_DifferentCardTitle,
                string.Format(Strings.CardPage_DifferentCardText, expected / 1_000_000_000d, Gigabytes(card)));
            return;
        }

        _card = card;
        _chosenModel = Session.Model;
        _detectedFirmware = Session.ConfirmedFirmware;
        Session.CardRoot = card.Root;

        HeaderText.Text = Strings.CardPage_ContinueHeader;
        SubheaderText.Text = Strings.CardPage_ContinueSubheader;
        StatusTitle.Text = string.Format(Strings.CardPage_CardTitle, DriveName(card), Gigabytes(card));
        StatusText.Text = Strings.CardPage_CardBackInComputer;

        DeviceTitle.Text = Session.Model?.Name ?? "";
        DeviceFirmware.Text = Session.ConfirmedFirmware is { } firmware
            ? string.Format(Strings.CardPage_CurrentFirmware, firmware)
            : Strings.CardPage_EnterVersionFromMenu;
        DeviceConfidence.Text = Session.ConfirmedFirmware is null ? "" : Strings.CardPage_VersionSeenAfterPass;

        FirmwareBox.Text = Session.ConfirmedFirmware?.ToString() ?? "";
        ShowDevice();
        FirmwarePanel.Visibility = Session.ConfirmedFirmware is null ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Assign(Strings.Common_Next, Continue);
        SecondaryButton.Assign(null, null);
        UpdateContinueState();
    }

    private void Continue()
    {
        if (_chosenModel is null || _card is null || TypedFirmware() is not { } firmware)
            return;

        // Check the size before recordings are backed up, not after an hour of copying.
        if (CardRequirements.For(_chosenModel.Name).Check(_card.TotalBytes) is { } problem)
        {
            ResultNotice.Kind = NoticeKind.Warning;
            ResultText.Text = problem;
            ResultNotice.Visibility = Visibility.Visible;
            return;
        }

        // A hand-typed version is confirmed separately: a wrong digit can lead to unsuitable firmware.
        if (firmware != _detectedFirmware && !_versionConfirmed)
        {
            AskToConfirm(firmware);
            return;
        }

        Session.Model = _chosenModel;
        Session.Firmware = firmware;
        Session.CardRoot = _card.Root;
        Session.CardBytes = _card.TotalBytes;
        _wizard.ShowPlan();
    }

    private void AskToConfirm(Version firmware)
    {
        var header = HeaderText.Text;
        var subheader = SubheaderText.Text;
        var primary = (Text: PrimaryButton.Content as string, Action: PrimaryButton.Tag as Action);
        var secondary = (Text: SecondaryButton.Content as string, Action: SecondaryButton.Tag as Action);

        HeaderText.Text = Strings.CardPage_CheckVersionHeader;
        SubheaderText.Text = _chosenModel?.Name ?? "";
        ConfirmQuestion.Text = string.Format(Strings.CardPage_ConfirmVersionQuestion, firmware);
        FirmwarePanel.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Visible;

        // The detected version would contradict the one being confirmed.
        DeviceFirmware.Visibility = Visibility.Collapsed;
        DeviceConfidence.Visibility = Visibility.Collapsed;

        PrimaryButton.Assign(Strings.CardPage_YesCorrect, () =>
        {
            _versionConfirmed = true;
            Continue();
        });

        SecondaryButton.Assign(Strings.CardPage_NoCorrectIt, () =>
        {
            HeaderText.Text = header;
            SubheaderText.Text = subheader;
            ConfirmPanel.Visibility = Visibility.Collapsed;
            FirmwarePanel.Visibility = Visibility.Visible;
            DeviceFirmware.Visibility = Visibility.Visible;
            DeviceConfidence.Visibility = Visibility.Visible;
            PrimaryButton.Assign(primary.Text, primary.Action);
            SecondaryButton.Assign(secondary.Text, secondary.Action);
            UpdateContinueState();
            FirmwareBox.Focus();
            FirmwareBox.SelectAll();
        });
    }

    private void OnFirmwareChanged(object sender, TextChangedEventArgs e)
    {
        _versionConfirmed = false;
        UpdateContinueState();
    }

    private Version? TypedFirmware() => FirmwareInput.Parse(FirmwareBox.Text);

    private void UpdateContinueState()
    {
        if (DeviceCard.Visibility != Visibility.Visible || ConfirmPanel.Visibility == Visibility.Visible)
            return;

        var typed = FirmwareBox.Text.Trim();
        var parsed = TypedFirmware();

        FirmwareHint.Text = typed.Length == 0
            ? Strings.CardPage_EnterVersionHint
            : Strings.CardPage_VersionFormatHint;
        FirmwareHint.Visibility = parsed is null ? Visibility.Visible : Visibility.Collapsed;

        PrimaryButton.IsEnabled = _chosenModel is not null && _card is not null && parsed is not null;
    }

    private void ShowDevice()
    {
        BusyBar.Visibility = Visibility.Collapsed;
        HowToPanel.Visibility = Visibility.Collapsed;
        UnofficialNotice.Visibility = Visibility.Collapsed;
        DeviceFirmware.Visibility = Visibility.Visible;
        DeviceConfidence.Visibility = Visibility.Visible;
        ResultNotice.Visibility = Visibility.Collapsed;
        ConfirmPanel.Visibility = Visibility.Collapsed;
        FirmwarePanel.Visibility = Visibility.Visible;
        DeviceCard.Visibility = Visibility.Visible;
    }

    private void Busy(string title, string text)
    {
        HeaderText.Text = DefaultHeader;
        SubheaderText.Text = DefaultSubheader;
        StatusTitle.Text = title;
        StatusText.Text = text;
        BusyBar.Visibility = Visibility.Visible;
        HowToPanel.Visibility = Visibility.Collapsed;
        UnofficialNotice.Visibility = FirstScreenNotice;
        UpdateNotice.Visibility = _update is null ? Visibility.Collapsed : FirstScreenNotice;
        ResultNotice.Visibility = Visibility.Collapsed;
        DeviceCard.Visibility = Visibility.Collapsed;
        PrimaryButton.Assign(null, null);
        SecondaryButton.Assign(null, null);
    }

    // retryText is for failures the drive watch cannot resolve on its own, such as a network error.
    private void Idle(string title, string text, string? retryText = null)
    {
        HeaderText.Text = Session.PassesDone > 0 ? Strings.CardPage_ContinueHeader : DefaultHeader;
        SubheaderText.Text = Session.PassesDone > 0 ? Strings.CardPage_ContinueSubheader : DefaultSubheader;
        StatusTitle.Text = title;
        StatusText.Text = text;
        BusyBar.Visibility = Visibility.Collapsed;
        ResultNotice.Visibility = Visibility.Collapsed;
        DeviceCard.Visibility = Visibility.Collapsed;
        UnofficialNotice.Visibility = FirstScreenNotice;
        UpdateNotice.Visibility = _update is null ? Visibility.Collapsed : FirstScreenNotice;

        HowToPanel.Visibility = Visibility.Collapsed;

        if (retryText is null)
        {
            PrimaryButton.Assign(null, null);
            SecondaryButton.Assign(Strings.CardPage_LookAgain, LookForCard);
        }
        else
        {
            PrimaryButton.Assign(retryText, LookForCard);
            SecondaryButton.Assign(null, null);
        }
    }

#if DEBUG
    internal void PreviewRecognition(CardRecognition recognition, RemovableCard card, string? typedFirmware = null)
    {
        _card = card;
        Session.CardRoot = card.Root;
        ShowRecognition(recognition, card);

        if (typedFirmware is not null)
        {
            FirmwareBox.Text = typedFirmware;
            Continue();
        }
    }

    internal void PreviewChosenByHand(CatalogModel model, RemovableCard card)
    {
        _card = card;
        ShowChosenByHand(model);
    }
#endif

    private static string DriveName(RemovableCard card) => card.Root.Name.TrimEnd('\\');

    private static string Gigabytes(RemovableCard card) => $"{card.TotalBytes / 1_000_000_000d:0.#}";
}
