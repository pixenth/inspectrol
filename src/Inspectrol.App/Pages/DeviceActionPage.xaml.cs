using System.Windows;
using System.Windows.Controls;
using Inspectrol.App.Controls;
using Inspectrol.App.Wizard;
using Inspectrol.Core;
using Inspectrol.Core.Planning;

namespace Inspectrol.App.Pages;

public partial class DeviceActionPage : UserControl
{
    private readonly IWizard _wizard;
    private readonly IReadOnlyList<DeviceAction> _actions;
    private readonly int _index;

    internal DeviceActionPage(IWizard wizard, IReadOnlyList<DeviceAction> actions, int index)
    {
        InitializeComponent();
        _wizard = wizard;
        _actions = actions;
        _index = index;

        var action = actions[index];
        TitleText.Text = action.Title;
        BodyText.Text = action.Text;
        QuestionText.Text = action.Question;
        CriticalNotice.Visibility = action.Critical ? Visibility.Visible : Visibility.Collapsed;

        // On critical steps Enter must not answer "yes" by accident.
        PrimaryButton.IsDefault = !action.Critical;

        PrimaryButton.Assign(Strings.DeviceActionPage_Yes, Next);
        SecondaryButton.Assign(Strings.DeviceActionPage_No, ShowHelp);
    }

    private DeviceAction Action => _actions[_index];

    private void OnButtonClick(object sender, RoutedEventArgs e) => ButtonActions.Run(sender);

    private void ShowHelp()
    {
        HelpText.Text = Action.IfNot;
        HelpNotice.Visibility = Visibility.Visible;
        HelpNotice.BringIntoView();

        PrimaryButton.Assign(Strings.DeviceActionPage_WorksNow, Next);
        SecondaryButton.Assign(Strings.DeviceActionPage_StillFails, GiveUp);
    }

    private void GiveUp()
    {
        // An optional step, such as a settings reset missing from the menu. The update is already installed.
        if (Action.GiveUp is null)
        {
            Next();
            return;
        }

        _wizard.ShowFinish(success: false, Action.GiveUp, backToAction: _index);
    }

    private void Next()
    {
        var session = _wizard.Session;

        if (Action.Kind == DeviceActionKind.CheckVersion)
            session.ConfirmedFirmware = session.CurrentStep?.FirmwareAfter;

        if (_index + 1 < _actions.Count)
        {
            _wizard.ShowDeviceAction(_index + 1);
            return;
        }

        if (Action.Kind == DeviceActionKind.CardBackToComputer)
        {
            session.StartNextPass();
            _wizard.ShowCard(lookAgain: true);
            return;
        }

        _wizard.ShowFinish(success: true, problem: null, backToAction: null);
    }
}
