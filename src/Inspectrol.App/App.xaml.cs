using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Inspectrol.Core;

namespace Inspectrol.App;

public partial class App : Application
{
    private static string CrashLogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Inspectrol",
        Strings.App_CrashLogFileName);

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Write(args.ExceptionObject as Exception);

        DefineButtonStyles();
    }

    // Built in code because the primary style is based on the Fluent theme's AccentButtonStyle. A StaticResource to a
    // missing key would crash the window on load; here it falls back to the plain button style.
    private void DefineButtonStyles()
    {
        var plain = TryFindResource(typeof(Button)) as Style;
        Resources["StepButton"] = Sized(plain);
        Resources["PrimaryStepButton"] = Sized(TryFindResource("AccentButtonStyle") as Style ?? plain);

        static Style Sized(Style? basedOn) => new(typeof(Button), basedOn)
        {
            Setters =
            {
                new Setter(FrameworkElement.MinWidthProperty, 120d),
                new Setter(FrameworkElement.MinHeightProperty, 34d),
                new Setter(Control.PaddingProperty, new Thickness(18, 5, 18, 5)),
                new Setter(Control.FontSizeProperty, 14d),
            },
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Write(e.Exception);

        MessageBox.Show(
            string.Format(Strings.App_CrashMessage, e.Exception.Message, CrashLogPath),
            "Inspectrol",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
        Shutdown(1);
    }

    private static void Write(Exception? error)
    {
        if (error is null)
            return;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            File.WriteAllText(CrashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{error}");
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
            // Best effort; the message box is shown either way.
        }
    }
}
