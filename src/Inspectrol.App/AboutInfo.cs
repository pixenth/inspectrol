using System.IO;
using System.Reflection;
using Inspectrol.Core;
using Inspectrol.Core.Updates;

namespace Inspectrol.App;

internal static class AboutInfo
{
    public const string Name = "Inspectrol";

    public static string Author => Strings.AboutInfo_Author;

    // Must match the LICENSE file at the repository root.
    public const string License = "GPL-3.0";

    // Year of the first release.
    public const int Year = 2026;

    public static string Version
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null ? "" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    public const string UpdateUrl = "https://inspectrol.ru/update";

    // The installer puts Uninstall.exe next to the program; the portable archive has none.
    public static bool IsPortable => !File.Exists(Path.Combine(AppContext.BaseDirectory, "Uninstall.exe"));

    // --update-url=... points the update check at a test server.
    public static Uri? UpdateSource()
    {
        const string option = "--update-url=";
        var argument = Environment.GetCommandLineArgs()
            .FirstOrDefault(value => value.StartsWith(option, StringComparison.OrdinalIgnoreCase));

        return Uri.TryCreate(argument?[option.Length..] ?? UpdateUrl, UriKind.Absolute, out var source)
            && UpdateChecker.IsAllowed(source)
                ? source
                : null;
    }

    public static string Footer =>
        string.Format(Strings.AboutInfo_Footer, Name, Version, Year, Author, License);
}
