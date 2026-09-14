using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Memory;

namespace Inspectrol.App.Pages;

public partial class FinishPage : UserControl
{
    private static readonly string SuccessGlyph = char.ConvertFromUtf32(0xE930);
    private static readonly string WarningGlyph = char.ConvertFromUtf32(0xE7BA);

    internal FinishPage(IWizard wizard, bool success, string? problem, int? backToAction)
    {
        InitializeComponent();
        var session = wizard.Session;

        if (success)
        {
            HeaderText.Text = Strings.FinishPage_SuccessHeader;
            ShowIcon(SuccessGlyph, "SystemFillColorSuccessBrush");
            ResultText.Text = Strings.FinishPage_SuccessLead;

            AddSummaryRow(Strings.FinishPage_ModelLabel, session.Model?.Name);
            AddSummaryRow(Strings.FinishPage_UpdatedLabel, session.CurrentStep?.Title,
                session.PassesDone > 0 ? Strings.FinishPage_AllPassesDone : null);
            AddSummaryRow(Strings.FinishPage_FirmwareLabel, session.ConfirmedFirmware?.ToString());
            AddSummaryRow(Strings.FinishPage_RecordingsLabel, session.RecordingsSavedTo);
            SummaryCard.Visibility = Visibility.Visible;
            DetailsText.Visibility = Visibility.Collapsed;

            if (!session.Preview && !session.Simulated)
                Remember(session);
        }
        else
        {
            HeaderText.Text = Strings.FinishPage_NotFinishedHeader;
            ShowIcon(WarningGlyph, "SystemFillColorCautionBrush");
            ResultText.Text = problem ?? "";
            DetailsText.Text = backToAction is null
                ? ""
                : Strings.FinishPage_BackToHintExplanation;
        }

        PrimaryButton.Assign(Strings.FinishPage_CloseProgram, wizard.CloseProgram);
        SecondaryButton.Assign(
            backToAction is null ? null : Strings.FinishPage_BackToHint,
            backToAction is { } index ? () => wizard.ShowDeviceAction(index) : null);
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private void ShowIcon(string glyph, string brushKey)
    {
        ResultIcon.Text = glyph;
        ResultIcon.Foreground = TryFindResource(brushKey) as Brush ?? ResultIcon.Foreground;
    }

    private void AddSummaryRow(string label, string? value, string? note = null)
    {
        if (string.IsNullOrEmpty(value))
            return;

        var secondary = (Style)FindResource("SecondaryText");
        SummaryPanel.Children.Add(new TextBlock
        {
            Text = label,
            Margin = new Thickness(0, SummaryPanel.Children.Count == 0 ? 0 : 10, 0, 0),
            Style = secondary,
        });
        SummaryPanel.Children.Add(new TextBlock { Text = value, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });

        if (note is not null)
            SummaryPanel.Children.Add(new TextBlock { Text = note, Margin = new Thickness(0, 2, 0, 0), Style = secondary });
    }

    private static void Remember(WizardSession session)
    {
        var store = new AppMemoryStore();
        store.TrySave(store.Load() with
        {
            ModelName = session.Model?.Name,
            Firmware = (session.ConfirmedFirmware ?? session.Firmware)?.ToString(),
            UpdatedAt = DateTime.Now,
            UpdatedWhat = session.CurrentStep?.Title,
            UnofficialWarningSeen = true,
        });
    }
}
