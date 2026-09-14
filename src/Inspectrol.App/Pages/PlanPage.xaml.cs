using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Catalog;
using Inspectrol.Core.Planning;

namespace Inspectrol.App.Pages;

public partial class PlanPage : UserControl
{
    // One check box per risk, so each risk is read rather than accepted all at once.
    private static readonly string[] FirmwareConsents =
    [
        Strings.PlanPage_ConsentEngine,
        Strings.PlanPage_ConsentKeepCard,
        Strings.PlanPage_ConsentModelName,
        Strings.PlanPage_ConsentReset,
    ];

    private static string ChainConsent => Strings.PlanPage_ConsentChain;

    private readonly IWizard _wizard;
    private readonly Dictionary<UpdateParts, CheckBox> _choices = [];
    private readonly List<CheckBox> _consents = [];

    private ModelUpdateInfo? _info;
    private UpdatePlan? _plan;
    private (bool Firmware, int Passes) _consentsShownFor;
    private bool _loaded;

    internal PlanPage(IWizard wizard)
    {
        InitializeComponent();
        _wizard = wizard;

        SubheaderText.Text = string.Format(Strings.PlanPage_Subheader, wizard.Session.Model?.Name, wizard.Session.Firmware);
        SecondaryButton.Assign(Strings.Common_Back, () => wizard.ShowCard(lookAgain: false));

        Loaded += async (_, _) =>
        {
            if (_loaded)
                return;

            _loaded = true;
            await LoadAsync();
        };
    }

    private WizardSession Session => _wizard.Session;

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private async void Reload() => await LoadAsync();

    private async Task LoadAsync()
    {
        HeaderText.Text = Strings.PlanPage_LoadingHeader;
        HintText.Text = "";
        LoadingBar.Visibility = Visibility.Visible;
        OptionsPanel.Children.Clear();
        _choices.Clear();
        PlanCard.Visibility = Visibility.Collapsed;
        BlockerNotice.Visibility = Visibility.Collapsed;
        WarningsPanel.Children.Clear();
        FirmwareWarningsPanel.Children.Clear();
        FirmwareWarningsPanel.Visibility = Visibility.Collapsed;
        ConsentCard.Visibility = Visibility.Collapsed;
        _consents.Clear();
        _consentsShownFor = default;
        _info = null;
        _plan = null;
        PrimaryButton.Assign(Strings.Common_Next, Next, enabled: false);

        ModelUpdateInfo info;
        try
        {
            info = await new InspectorCatalog(Session.Http).GetUpdateInfoAsync(Session.Model!, region: null, CancellationToken.None);
        }
        catch (CatalogFormatException error)
        {
            // Cable-updated model, region required or a site change: retrying does not help.
            Fail(Strings.PlanPage_CannotUpdateHeader, error.Message);
            PrimaryButton.Assign(null, null);
            return;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            Fail(Strings.PlanPage_NoConnectionHeader,
                Strings.PlanPage_NoConnectionText);
            PrimaryButton.Assign(Strings.Common_TryAgain, Reload);
            return;
        }

        LoadingBar.Visibility = Visibility.Collapsed;
        HeaderText.Text = Strings.PlanPage_Header;
        _info = info;
        ShowWarnings(info);

        var options = UpdatePlanner.Options(info, Session.Firmware!);
        foreach (var option in options)
            OptionsPanel.Children.Add(OptionCard(option, info));

        // Only the camera database is preselected, and only when it installs on the current firmware.
        // Firmware and eMap are always the user's own choice.
        if (options.FirstOrDefault(option => option.Part == UpdateParts.Database) is { Available: true, Note: null })
            _choices[UpdateParts.Database].IsChecked = true;

        Refresh(firmwareJustSelected: false);
    }

    private void Fail(string header, string message)
    {
        LoadingBar.Visibility = Visibility.Collapsed;
        HeaderText.Text = header;
        WarningsPanel.Children.Add(new Notice
        {
            Kind = NoticeKind.Warning,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
        });
    }

    private void ShowWarnings(ModelUpdateInfo info)
    {
        foreach (var warning in info.DatabaseWarnings)
            WarningsPanel.Children.Add(WarningNotice(warning));

        foreach (var warning in info.FirmwareWarnings)
            FirmwareWarningsPanel.Children.Add(WarningNotice(warning));
    }

    private static Notice WarningNotice(string warning) => new()
    {
        Kind = NoticeKind.Warning,
        Content = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = Strings.PlanPage_SiteWarningCaption,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = warning, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap },
            },
        },
    };

    private Border OptionCard(UpdatePartOption option, ModelUpdateInfo info)
    {
        var (glyph, title, description) = option.Part switch
        {
            UpdateParts.Database => (0xE707, Strings.PlanPage_DatabaseTitle, Strings.PlanPage_DatabaseText),
            UpdateParts.Firmware => (0xE713, Strings.PlanPage_FirmwareTitle,
                info.LatestFirmware is { } latest
                    ? string.Format(Strings.PlanPage_FirmwareText, latest)
                    : Strings.PlanPage_FirmwareTextNoVersion),
            _ => (0xE774, Strings.PlanPage_EMapTitle, Strings.PlanPage_EMapText),
        };

        var icon = new TextBlock
        {
            Text = char.ConvertFromUtf32(glyph),
            Style = (Style)FindResource("Glyph"),
            FontSize = 20,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = TryFindResource("AccentTextFillColorPrimaryBrush") as Brush ?? Brushes.SteelBlue,
        };
        DockPanel.SetDock(icon, Dock.Left);

        var header = new DockPanel();
        header.Children.Add(icon);
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });

        // The Fluent check box centres its glyph on the whole content, so only the one-line header goes inside it
        // and the description sits below, indented past the glyph.
        var box = new CheckBox
        {
            IsEnabled = option.Available,
            Tag = option.Part,
            Padding = new Thickness(8, 0, 0, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = header,
        };
        AutomationProperties.SetName(box, title);
        AutomationProperties.SetHelpText(box, option.Note is null ? description : description + " " + option.Note);
        box.Checked += OnChoiceChanged;
        box.Unchecked += OnChoiceChanged;
        _choices[option.Part] = box;

        var details = new StackPanel { Margin = new Thickness(28, 0, 0, 0) };
        details.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("SecondaryText") });

        if (option.Note is not null)
        {
            details.Children.Add(new TextBlock
            {
                Text = option.Note,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = option.Available
                    ? TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.Black
                    : TryFindResource("SystemFillColorCautionBrush") as Brush ?? Brushes.DarkOrange,
            });
        }

        var card = new Border
        {
            Style = (Style)FindResource("Card"),
            Margin = new Thickness(0, 0, 0, 12),
            Child = new StackPanel { Children = { box, details } },
        };

        // A click on the check box itself is handled by it and does not reach the card.
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (box.IsEnabled)
                box.IsChecked = box.IsChecked != true;
        };

        return card;
    }

    private void OnChoiceChanged(object sender, RoutedEventArgs e) =>
        Refresh(firmwareJustSelected: sender is CheckBox { Tag: UpdateParts.Firmware, IsChecked: true } && IsLoaded);

    private void Refresh(bool firmwareJustSelected)
    {
        if (_info is null)
            return;

        var parts = _choices
            .Where(choice => choice.Value.IsChecked == true)
            .Aggregate(UpdateParts.None, (all, choice) => all | choice.Key);

        _plan = parts == UpdateParts.None ? null : UpdatePlanner.Build(_info, parts, Session.Firmware!);
        ShowPlan(_plan);

        var withFirmware = parts.HasFlag(UpdateParts.Firmware);
        FirmwareWarningsPanel.Visibility = withFirmware && FirmwareWarningsPanel.Children.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var passes = _plan is { IsPossible: true } ? _plan.Steps.Count : 0;
        var consentsFor = (withFirmware && passes > 0, passes);
        if (consentsFor != _consentsShownFor)
        {
            BuildConsents(consentsFor.Item1, passes);
            _consentsShownFor = consentsFor;
        }

        UpdateButtons();

        // Warnings and consents appear below the fold. Scroll to them so the disabled button does not look broken.
        if (!firmwareJustSelected)
            return;

        FrameworkElement target = FirmwareWarningsPanel.Visibility == Visibility.Visible
            ? FirmwareWarningsPanel
            : ConsentCard.Visibility == Visibility.Visible ? ConsentCard : BlockerNotice;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => target.BringIntoView());
    }

    private void ShowPlan(UpdatePlan? plan)
    {
        PassesPanel.Children.Clear();
        PlanCard.Visibility = plan is { IsPossible: true } ? Visibility.Visible : Visibility.Collapsed;
        BlockerNotice.Visibility = plan is { IsPossible: false } ? Visibility.Visible : Visibility.Collapsed;

        if (plan is null)
            return;

        if (!plan.IsPossible)
        {
            BlockerText.Text = plan.Blocker;
            return;
        }

        for (var index = 0; index < plan.Steps.Count; index++)
        {
            PassesPanel.Children.Add(new TextBlock
            {
                Text = plan.Steps.Count > 1
                    ? string.Format(Strings.PlanPage_PassOfPasses, index + 1, plan.Steps.Count, plan.Steps[index].Title)
                    : plan.Steps[index].Title,
                Margin = new Thickness(0, 4, 0, 0),
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap,
            });
        }
    }

    private void BuildConsents(bool firmware, int passes)
    {
        ConsentsPanel.Children.Clear();
        _consents.Clear();

        string[] texts = firmware
            ? passes > 1 ? [.. FirmwareConsents, ChainConsent] : FirmwareConsents
            : [];

        foreach (var text in texts)
        {
            var box = new CheckBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 15 },
            };
            box.Checked += (_, _) => UpdateButtons();
            box.Unchecked += (_, _) => UpdateButtons();

            _consents.Add(box);
            ConsentsPanel.Children.Add(box);
        }

        ConsentCard.Visibility = texts.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateButtons()
    {
        var possible = _plan is { IsPossible: true };
        var agreed = _consents.All(box => box.IsChecked == true);

        PrimaryButton.IsEnabled = possible && agreed;
        HintText.Text = _plan is null
            ? Strings.PlanPage_ChooseHint
            : possible && !agreed ? Strings.PlanPage_CheckAllHint : "";
    }

    private void Next()
    {
        if (_plan is not { IsPossible: true } plan || !_consents.All(box => box.IsChecked == true))
            return;

        Session.Plan = plan;
        _wizard.ShowDownload();
    }
}
