using Inspectrol.Core.Cards;

namespace Inspectrol.Core.Downloading;

public interface IUpdateDownloads
{
    Task<FileInfo> DownloadAsync(string url, DirectoryInfo folder, IProgress<double>? progress, CancellationToken cancellationToken);

    IReadOnlyList<CardFile> Extract(FileInfo archive, DirectoryInfo folder);
}

public sealed class WebUpdateDownloads(HttpClient http) : IUpdateDownloads
{
    public Task<FileInfo> DownloadAsync(string url, DirectoryInfo folder, IProgress<double>? progress, CancellationToken cancellationToken) =>
        new FileDownloader(http).DownloadAsync(url, folder, progress, cancellationToken);

    public IReadOnlyList<CardFile> Extract(FileInfo archive, DirectoryInfo folder) => ArchiveExtractor.Extract(archive, folder);
}
