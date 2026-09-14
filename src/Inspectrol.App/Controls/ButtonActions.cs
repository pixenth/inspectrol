using System.Windows;
using System.Windows.Controls;

namespace Inspectrol.App.Controls;

// Page buttons change meaning with the page state. Setting caption and action together keeps a button from showing
// the caption of one state and running the action of another.
internal static class ButtonActions
{
    public static void Assign(this Button button, string? text, Action? action, bool enabled = true)
    {
        button.Content = text;
        button.Tag = action;
        button.IsEnabled = enabled && action is not null;
        button.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public static void Run(object sender)
    {
        if (sender is Button { Tag: Action action, IsEnabled: true })
            action();
    }
}
