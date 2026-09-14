using System.Text;

namespace Inspectrol.Core.Logging;

// The wizard does not write to the log yet. Logging must never get in the way of the update, so write failures are
// swallowed.
public sealed class ActionLog
{
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly string _path;
    private readonly Lock _lock = new();

    public ActionLog(string? filePath = null) => _path = filePath ?? DefaultPath();

    public string Path => _path;

    public static string DefaultPath() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Inspectrol",
        "журнал.txt");

    public void Write(string message)
    {
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
                RotateIfTooBig();
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public void WriteError(string message, Exception error) =>
        Write(string.Format(Strings.ActionLog_ErrorEntry, message, error.GetType().Name, error.Message));

    // Windows version, bitness and administrator rights go first: formatting and access rights depend on them, and
    // those cause most failures.
    public bool TrySaveReport(string targetPath)
    {
        lock (_lock)
        {
            try
            {
                var report = new StringBuilder();
                report.AppendLine(Strings.ActionLog_ReportTitle);
                report.AppendLine(string.Format(Strings.ActionLog_ReportCreated, DateTime.Now));
                report.AppendLine(string.Format(Strings.ActionLog_ReportWindows, Environment.OSVersion.VersionString, Environment.Is64BitOperatingSystem));
                report.AppendLine(string.Format(Strings.ActionLog_ReportAdministrator, IsElevated()));
                report.AppendLine(string.Format(Strings.ActionLog_ReportVersion, typeof(ActionLog).Assembly.GetName().Version));
                report.AppendLine();
                report.AppendLine(Strings.ActionLog_ReportLogHeader);
                report.AppendLine(File.Exists(_path) ? File.ReadAllText(_path) : Strings.ActionLog_ReportLogEmpty);

                File.WriteAllText(targetPath, report.ToString(), Encoding.UTF8);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception error) when (error is PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void RotateIfTooBig()
    {
        var file = new FileInfo(_path);
        if (!file.Exists || file.Length < MaxBytes)
            return;

        var previous = _path + ".старый";
        File.Delete(previous);
        File.Move(_path, previous);
    }
}
