using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Planning;

namespace Inspectrol.App.Pages;

public partial class CarInstructionPage : UserControl
{
    private readonly IWizard _wizard;
    private readonly IReadOnlyList<DeviceAction> _actions;

    internal CarInstructionPage(IWizard wizard, IReadOnlyList<DeviceAction> actions)
    {
        InitializeComponent();
        _wizard = wizard;
        _actions = actions;

        HeaderText.Text = Strings.CarInstructionPage_Header;
        SubheaderText.Text =
            Strings.CarInstructionPage_Subheader;

        for (var number = 1; number <= actions.Count; number++)
            StepsPanel.Children.Add(StepCard(number, actions[number - 1], last: number == actions.Count));

        SecondaryButton.Assign(Strings.CarInstructionPage_SaveToFile, Save);
        PrimaryButton.Assign(Strings.CarInstructionPage_AllDone, Next);
    }

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private static UIElement StepCard(int number, DeviceAction action, bool last) => new NumberedStep
    {
        Number = number,
        Title = action.Title,
        Text = action.Text,
        Hint = string.Format(Strings.CarInstructions_IfNot, action.IfNot),
        Margin = new Thickness(0, 14, 0, last ? 14 : 0),
    };

    private void Save()
    {
        var model = _wizard.Session.Model?.Name ?? Strings.CarInstructionPage_DefaultModelName;
        var text = CarInstructions.Text(_actions, model);

        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Strings.CarInstructionPage_FileName);

            // The UTF-8 byte order mark lets older versions of Notepad detect the encoding and show Cyrillic correctly.
            File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            SavedNotice.Kind = NoticeKind.Success;
            SavedText.Text = string.Format(Strings.CarInstructionPage_Saved, Strings.CarInstructionPage_FileName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SavedNotice.Kind = NoticeKind.Warning;
            SavedText.Text = string.Format(Strings.CarInstructionPage_SaveFailed, error.Message);
        }

        SavedNotice.Visibility = Visibility.Visible;
        SavedNotice.BringIntoView();
    }

    private void Next()
    {
        if (CarInstructions.Questions(_actions).Count > 0)
        {
            _wizard.ShowDeviceAction(0);
            return;
        }

        _wizard.ShowFinish(success: true, problem: null, backToAction: null);
    }
}
