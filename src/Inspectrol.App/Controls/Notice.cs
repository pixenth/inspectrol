using System.Windows;
using System.Windows.Controls;

namespace Inspectrol.App.Controls;

public enum NoticeKind { Info, Warning, Critical, Success }

// InfoBar-like box; the template is in App.xaml.
public class Notice : ContentControl
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(NoticeKind), typeof(Notice), new PropertyMetadata(NoticeKind.Info));

    public NoticeKind Kind
    {
        get => (NoticeKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }
}
