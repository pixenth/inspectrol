using System.Windows;
using System.Windows.Controls;

namespace Inspectrol.App.Controls;

// The template is in App.xaml.
public class NumberedStep : Control
{
    public static readonly DependencyProperty NumberProperty = DependencyProperty.Register(
        nameof(Number), typeof(int), typeof(NumberedStep), new PropertyMetadata(1));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(NumberedStep), new PropertyMetadata(""));

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(NumberedStep), new PropertyMetadata(""));

    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(
        nameof(Hint), typeof(string), typeof(NumberedStep), new PropertyMetadata(null));

    public int Number
    {
        get => (int)GetValue(NumberProperty);
        set => SetValue(NumberProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string? Hint
    {
        get => (string?)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }
}
