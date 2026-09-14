using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Inspectrol.App.Pages;
using Inspectrol.App.Simulator;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Catalog;
using Inspectrol.Core.Planning;
using Inspectrol.Core.Updates;

namespace Inspectrol.App;

public partial class MainWindow : Window, IWizard
{
    private static readonly string[] StepNames =
    [
        Strings.MainWindow_StepCard,
        Strings.MainWindow_StepPlan,
        Strings.MainWindow_StepDownload,
        Strings.MainWindow_StepRecordings,
        Strings.MainWindow_StepWrite,
        Strings.MainWindow_StepInCar,
        Strings.MainWindow_StepFinish,
    ];

    private static string NothingChangedYet => Strings.MainWindow_NothingChangedYet;

    private static string DeviceIsUpdating => Strings.MainWindow_DeviceIsUpdating;

    private static string ChainNotFinished => Strings.MainWindow_ChainNotFinished;

    private static string InstructionNotTaken => Strings.MainWindow_InstructionNotTaken;

    private readonly CardPage _cardPage;
    private string? _busyReason;
    private string? _closeQuestion;
    private bool _closingForGood;

    public MainWindow()
    {
        InitializeComponent();
        AppNameText.Text = AboutInfo.Name;
        FooterText.Text = AboutInfo.Footer;

        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Inspectrol/{AboutInfo.Version}");
        Session = new WizardSession(http);

        _cardPage = new CardPage(this);
        Loaded += async (_, _) =>
        {
            if (await ShowPreviewIfAskedAsync())
                return;

            if (SimulatorWindow.IsRequested())
                SimulatorWindow.Attach(this);

            ShowCard(lookAgain: true);
            await OfferAppUpdateAsync();
        };
    }

    internal WizardSession Session { get; }

    WizardSession IWizard.Session => Session;

    public void ShowCard(bool lookAgain)
    {
        Show(_cardPage, 1, Session.PassesDone > 0 ? ChainNotFinished : null);
        if (lookAgain)
            _cardPage.LookForCard();
    }

    public void ShowModelPicker() => Show(new ModelPickerPage(this), 1, Session.PassesDone > 0 ? ChainNotFinished : null);

    public void ShowAppUpdate(AppUpdate update) => Show(new AppUpdatePage(this, update), 1, closeQuestion: null);

    private async Task OfferAppUpdateAsync()
    {
        if (!Version.TryParse(AboutInfo.Version, out var current) || AboutInfo.UpdateSource() is not { } source)
            return;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        if (await UpdateChecker.CheckAsync(Session.Http, source, current, timeout.Token) is { } update)
            _cardPage.OfferUpdate(update);
    }

    public void ChooseModel(CatalogModel model)
    {
        Show(_cardPage, 1, Session.PassesDone > 0 ? ChainNotFinished : null);
        _cardPage.ShowChosenByHand(model);
    }

    public void ShowPlan() => Show(new PlanPage(this), 2, Session.PassesDone > 0 ? ChainNotFinished : NothingChangedYet);

    // Download comes before the recordings backup, which can take hours, so a failed download wastes no time.
    public void ShowDownload() => Show(new DownloadPage(this), 3, Session.PassesDone > 0 ? ChainNotFinished : NothingChangedYet);

    public void ShowRecordings() => Show(new RecordingsPage(this), 4, Session.PassesDone > 0 ? ChainNotFinished : NothingChangedYet);

    public void ShowWrite() => Show(new WritePage(this), 5, Session.PassesDone > 0 ? ChainNotFinished : NothingChangedYet);

    public void ShowCarInstruction() => Show(new CarInstructionPage(this, Actions()), 6, InstructionNotTaken);

    public void ShowDeviceAction(int index)
    {
        var questions = CarInstructions.Questions(Actions());
        var detail = questions.Count > 1
            ? string.Format(Strings.MainWindow_QuestionDetail, index + 1, questions.Count)
            : null;

        Show(new DeviceActionPage(this, questions, index), 6, DeviceIsUpdating, detail);
    }

    private IReadOnlyList<DeviceAction> Actions() =>
        DeviceProcedure.For(Session.CurrentStep!, Session.PassNumber, Session.PassCount);

    public void ShowFinish(bool success, string? problem, int? backToAction) =>
        Show(new FinishPage(this, success, problem, backToAction), 7, closeQuestion: null);

    public void SetBusy(string? reason) => _busyReason = reason;

    public void SetCloseQuestion(string? question) => _closeQuestion = question;

    public void CloseProgram()
    {
        _closingForGood = true;
        Close();
    }

    private void Show(UserControl page, int stepNumber, string? closeQuestion, string? detail = null)
    {
        _busyReason = null;
        _closeQuestion = closeQuestion;
        PageHost.Content = page;
        ShowStep(stepNumber, detail);
    }

    private void ShowStep(int number, string? detail)
    {
        StepCounterText.Text = string.Format(Strings.MainWindow_StepCounter, number, StepNames.Length);

        var lines = new List<string>();
        if (Session.PassCount > 1)
            lines.Add(string.Format(Strings.MainWindow_PassDetail, Session.PassNumber, Session.PassCount));
        if (detail is not null)
            lines.Add(detail);

        StepsList.ItemsSource = StepNames
            .Select((name, index) => new StepItem(
                index + 1,
                name,
                index + 1 < number ? StepState.Done : index + 1 == number ? StepState.Current : StepState.Upcoming,
                index + 1 == number ? string.Join(Environment.NewLine, lines) : ""))
            .ToList();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closingForGood)
            return;

        if (_busyReason is not null)
        {
            MessageBox.Show(this, _busyReason, Strings.MainWindow_BusyTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Cancel = true;
            return;
        }

        if (_closeQuestion is null)
            return;

        var answer = MessageBox.Show(
            this,
            string.Format(Strings.MainWindow_CloseQuestion, _closeQuestion),
            "Inspectrol",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            e.Cancel = true;
    }

#if DEBUG
    internal CardPage CardPage => _cardPage;

    private Task<bool> ShowPreviewIfAskedAsync() => Preview.ScreenPreview.TryShowAsync(this);
#else
    private static Task<bool> ShowPreviewIfAskedAsync() => Task.FromResult(false);
#endif
}
