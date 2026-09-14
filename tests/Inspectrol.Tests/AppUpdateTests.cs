using System.Net;
using System.Security.Cryptography;
using System.Text;
using Inspectrol.Core.Updates;

namespace Inspectrol.Tests;

public class AppUpdateTests
{
    private static readonly Version Current = new(0, 1, 0);
    private static readonly byte[] Installer = Encoding.ASCII.GetBytes("pretend this is Inspectrol-0.2.0-setup.exe");
    private static readonly string InstallerHash = Convert.ToHexStringLower(SHA256.HashData(Installer));
    private static readonly Uri Source = new("https://inspectrol.ru/update");

    private static string Manifest(
        string version = "0.2.0",
        string url = "https://inspectrol.ru/download/Inspectrol-0.2.0-setup.exe",
        string? sha256 = null) =>
        $$"""{"version":"{{version}}","url":"{{url}}","size":{{Installer.Length}},"sha256":"{{sha256 ?? InstallerHash}}","notes":"Что нового"}""";

    private static HttpClient Server(Func<HttpRequestMessage, HttpResponseMessage> respond) => new(new Handler(respond));

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Offers_a_newer_version()
    {
        var update = await UpdateChecker.CheckAsync(Server(_ => Json(Manifest())), Source, Current, CancellationToken.None);

        Assert.Equal(new Version(0, 2, 0), update?.Version);
        Assert.Equal("Что нового", update?.Notes);
        Assert.Equal(Installer.Length, update?.Size);
    }

    [Theory]
    [InlineData("0.1.0")]
    [InlineData("0.1")]
    [InlineData("0.1.0.0")]
    [InlineData("0.0.9")]
    public async Task Does_not_offer_the_same_or_an_older_version(string version)
    {
        var update = await UpdateChecker.CheckAsync(Server(_ => Json(Manifest(version))), Source, Current, CancellationToken.None);

        Assert.Null(update);
    }

    [Theory]
    [InlineData("""{"version":"0.2.0","url":"http://inspectrol.ru/download/setup.exe","sha256":"{hash}"}""")]
    [InlineData("""{"version":"0.2.0","url":"https://inspectrol.ru/download/setup.zip","sha256":"{hash}"}""")]
    [InlineData("""{"version":"0.2.0","url":"https://inspectrol.ru/download/setup.exe","sha256":"abc"}""")]
    [InlineData("""{"url":"https://inspectrol.ru/download/setup.exe","sha256":"{hash}"}""")]
    [InlineData("""<html>Сайт на обслуживании</html>""")]
    public void Refuses_a_manifest_it_cannot_trust(string json)
    {
        Assert.Null(UpdateChecker.Parse(json.Replace("{hash}", InstallerHash)));
    }

    [Fact]
    public async Task Stays_quiet_when_the_server_is_unreachable()
    {
        var offline = Server(_ => throw new HttpRequestException("No such host is known."));
        var broken = Server(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.Null(await UpdateChecker.CheckAsync(offline, Source, Current, CancellationToken.None));
        Assert.Null(await UpdateChecker.CheckAsync(broken, Source, Current, CancellationToken.None));
    }

    [Fact]
    public async Task Stays_quiet_when_the_server_sends_an_unknown_charset()
    {
        var server = Server(_ =>
        {
            var response = Json(Manifest());
            response.Content.Headers.ContentType!.CharSet = "no-such-charset";
            return response;
        });

        Assert.Null(await UpdateChecker.CheckAsync(server, Source, Current, CancellationToken.None));
    }

    [Fact]
    public async Task Downloads_an_installer_that_matches_the_manifest()
    {
        var update = UpdateChecker.Parse(Manifest())!;
        var http = Server(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) });

        var file = await UpdateDownloader.DownloadAsync(http, update, Directory.CreateTempSubdirectory(), progress: null, CancellationToken.None);

        Assert.Equal(Installer, File.ReadAllBytes(file.FullName));
    }

    [Fact]
    public async Task Deletes_an_installer_that_does_not_match_the_manifest()
    {
        var update = UpdateChecker.Parse(Manifest(sha256: new string('0', 64)))!;
        var folder = Directory.CreateTempSubdirectory();
        var http = Server(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => UpdateDownloader.DownloadAsync(http, update, folder, progress: null, CancellationToken.None));

        Assert.Empty(folder.GetFiles());
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
