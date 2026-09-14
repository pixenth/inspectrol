namespace Inspectrol.Core.Downloading;

// Downloads go to a ".part" file: an incomplete file must never look finished, or it could reach the card and
// damage the dashcam.
public sealed class FileDownloader(HttpClient http)
{
    public async Task<FileInfo> DownloadAsync(
        string url,
        DirectoryInfo targetFolder,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        targetFolder.Create();
        var fileName = FileNameFrom(url);

        var temporary = new FileInfo(Path.Combine(targetFolder.FullName, fileName + ".part"));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var expected = response.Content.Headers.ContentLength;

        // A server that closes the connection cleanly mid-file raises no error, so without a declared size half of
        // the firmware would look finished. The Inspector file server always sends the size.
        if (expected is null or <= 0)
        {
            throw new InvalidDataException(
                Strings.FileDownloader_SizeUnknown);
        }

        long written = 0;
        var connectionLost = false;

        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = temporary.Create())
        {
            var buffer = new byte[81920];
            try
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                    progress?.Report((double)written / expected.Value);
                }
            }
            catch (IOException)
            {
                connectionLost = true;
            }
        }

        if (connectionLost || written != expected)
        {
            temporary.Delete();
            throw new InvalidDataException(string.Format(Strings.FileDownloader_Incomplete, written, expected));
        }

        var targetPath = Path.Combine(targetFolder.FullName, fileName);
        if (File.Exists(targetPath))
            File.Delete(targetPath);
        temporary.MoveTo(targetPath);
        progress?.Report(1);

        // A FileInfo created earlier may have cached that the file does not exist, and Length would then throw.
        return new FileInfo(targetPath);
    }

    // File names on the site may contain spaces and apostrophes ("Scat SE_SEQ_MapS_AtlaS_eMap_Q2'2021.rar").
    // Links encode spaces as %20, and Uri.LocalPath decodes them.
    private static string FileNameFrom(string url)
    {
        var fileName = Path.GetFileName(new Uri(url).LocalPath).Trim();

        if (fileName.Length == 0)
            throw new InvalidDataException(string.Format(Strings.FileDownloader_NoFileName, url));

        if (fileName.AsSpan().IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidDataException(string.Format(Strings.FileDownloader_InvalidFileName, fileName));

        return fileName;
    }
}
