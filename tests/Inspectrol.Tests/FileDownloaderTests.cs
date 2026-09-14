using System.Net;
using Inspectrol.Core.Downloading;

namespace Inspectrol.Tests;

public class FileDownloaderTests : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _prefix = $"http://localhost:{Random.Shared.Next(20000, 30000)}/";
    private readonly byte[] _payload = [1, 2, 3, 4, 5];
    private bool _truncate;
    private bool _hideLength;

    public FileDownloaderTests()
    {
        _listener.Prefixes.Add(_prefix);
        _listener.Start();
        _ = Task.Run(async () =>
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch (HttpListenerException) { return; }
                catch (ObjectDisposedException) { return; }

                var body = _truncate ? _payload[..2] : _payload;
                if (!_hideLength)
                    context.Response.ContentLength64 = _payload.Length;
                try
                {
                    await context.Response.OutputStream.WriteAsync(body);
                    // A lost connection is simulated by aborting it. Closing a response that promised
                    // more bytes would leave the client waiting for the rest forever.
                    if (_truncate)
                        context.Response.Abort();
                    else
                        context.Response.Close();
                }
                catch (HttpListenerException) { /* A client that dropped the connection is an expected outcome here. */ }
            }
        });
    }

    public void Dispose()
    {
        _listener.Close();
        GC.SuppressFinalize(this);
    }

    // A stuck test fails instead of hanging the whole run.
    private static HttpClient NewClient() => new() { Timeout = TimeSpan.FromSeconds(15) };

    [Fact]
    public async Task Downloads_file_to_folder()
    {
        var folder = Directory.CreateTempSubdirectory();
        var downloader = new FileDownloader(NewClient());

        var file = await downloader.DownloadAsync($"{_prefix}update.zip", folder, null, CancellationToken.None);

        Assert.Equal("update.zip", file.Name);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(file.FullName));

        Assert.True(file.Exists);
        Assert.Equal(_payload.Length, file.Length);
    }

    [Fact]
    public async Task Reports_progress()
    {
        var folder = Directory.CreateTempSubdirectory();
        var reported = new List<double>();
        var downloader = new FileDownloader(NewClient());

        await downloader.DownloadAsync($"{_prefix}update.zip", folder, new Progress<double>(reported.Add), CancellationToken.None);

        Assert.NotEmpty(reported);
        Assert.True(reported[^1] >= 0.99, $"последний прогресс {reported[^1]}");
    }

    [Fact]
    public async Task Fails_when_size_does_not_match()
    {
        _truncate = true;
        var folder = Directory.CreateTempSubdirectory();
        var downloader = new FileDownloader(NewClient());

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync($"{_prefix}update.zip", folder, null, CancellationToken.None));

        Assert.Contains("не полностью", error.Message);
        Assert.Empty(folder.GetFiles());
    }

    [Fact]
    public async Task Keeps_spaces_and_quotes_in_the_file_name()
    {
        // The shared eMap file on the Inspector site is named "Scat SE_SEQ_MapS_AtlaS_eMap_Q2'2021.rar",
        // with spaces and an apostrophe, and its link encodes the spaces as %20.
        var folder = Directory.CreateTempSubdirectory();
        var downloader = new FileDownloader(NewClient());

        var file = await downloader.DownloadAsync($"{_prefix}Scat%20SE_eMap_Q2'2021.rar", folder, null, CancellationToken.None);

        Assert.Equal("Scat SE_eMap_Q2'2021.rar", file.Name);
    }

    [Fact]
    public async Task Refuses_a_link_without_a_file_name()
    {
        var folder = Directory.CreateTempSubdirectory();
        var downloader = new FileDownloader(NewClient());

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync(_prefix, folder, null, CancellationToken.None));

        Assert.Contains("имени файла", error.Message);
    }

    [Fact]
    public async Task Refuses_a_download_without_a_promised_size()
    {
        _hideLength = true;
        var folder = Directory.CreateTempSubdirectory();
        var downloader = new FileDownloader(NewClient());

        var error = await Assert.ThrowsAsync<InvalidDataException>(
            () => downloader.DownloadAsync($"{_prefix}update.zip", folder, null, CancellationToken.None));

        Assert.Contains("размер", error.Message);
        Assert.Empty(folder.GetFiles());
    }
}
