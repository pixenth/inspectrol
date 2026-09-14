using System.Net;
using Inspectrol.Core.Catalog;

namespace Inspectrol.Tests;

public class InspectorCatalogTests
{
    private sealed class FixtureHandler : HttpMessageHandler
    {
        public List<string> Requested { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            var file = url.Contains("obnovlenie-po") ? "barracuda-firmware.html"
                : url.Contains("barracuda-obnovlenie") ? "barracuda-database.html"
                : "support-index.html";
            var html = File.ReadAllText(Path.Combine("Fixtures", file));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
        }
    }

    private sealed class StubHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html) });
    }

    private static InspectorCatalog Catalog() => new(new HttpClient(new FixtureHandler()));

    private static async Task<CatalogModel> ModelAsync(InspectorCatalog catalog, string name) =>
        (await catalog.GetModelsAsync(CancellationToken.None)).Single(m => m.Name == name);

    [Fact]
    public async Task Returns_models_from_support_page()
    {
        var models = await Catalog().GetModelsAsync(CancellationToken.None);

        Assert.Contains(models, m => m.Name == "Inspector Barracuda");
    }

    [Fact]
    public async Task Collects_update_info_for_a_model()
    {
        var catalog = Catalog();
        var barracuda = await ModelAsync(catalog, "Inspector Barracuda");

        var info = await catalog.GetUpdateInfoAsync(barracuda, region: null, CancellationToken.None);

        Assert.Equal(new Version(1, 2, 5), info.LatestFirmware);
        Assert.Equal(new Version(1, 2, 0), info.FirmwareRequires);
        Assert.Equal(new Version(1, 2, 3), info.DatabaseRequiresFirmware);
        Assert.EndsWith("BarracudaDB_37.zip", info.DatabaseUrl);
        Assert.Contains(info.ArchiveFirmwares, f => f.Version == new Version(1, 2, 0));
    }

    [Fact]
    public async Task Carries_warnings_through_to_the_wizard()
    {
        var catalog = Catalog();
        var barracuda = await ModelAsync(catalog, "Inspector Barracuda");

        var info = await catalog.GetUpdateInfoAsync(barracuda, region: null, CancellationToken.None);

        Assert.Contains(info.FirmwareWarnings, w => w.Contains("не ниже версии 1.2.0"));
        Assert.Contains(info.DatabaseWarnings, w => w.Contains("не ниже версии 1.2.3"));
    }

    [Fact]
    public async Task Keeps_firmware_warnings_apart_from_database_warnings()
    {
        var catalog = Catalog();
        var barracuda = await ModelAsync(catalog, "Inspector Barracuda");

        var info = await catalog.GetUpdateInfoAsync(barracuda, region: null, CancellationToken.None);

        Assert.DoesNotContain(info.DatabaseWarnings, w => w.Contains("не ниже версии 1.2.0"));
        Assert.DoesNotContain(info.FirmwareWarnings, w => w.Contains("не ниже версии 1.2.3"));
    }

    [Fact]
    public async Task Reads_the_database_date_from_the_update_title()
    {
        var catalog = Catalog();
        var barracuda = await ModelAsync(catalog, "Inspector Barracuda");

        var info = await catalog.GetUpdateInfoAsync(barracuda, region: null, CancellationToken.None);

        Assert.Equal(InspectorCatalog.DateFromTitle(barracuda.Updates.First(u => u.Kind == UpdateKind.Database).Title), info.DatabaseDate);
        Assert.NotNull(info.DatabaseDate);
    }

    [Theory]
    [InlineData("Inspector AtlaS (обновление базы данных) 09/09/26", 2026, 9, 9)]
    [InlineData("Inspector AtlaS (обновление базы данных) 09/09/2026", 2026, 9, 9)]
    [InlineData("Inspector Hook (обновление базы данных) 1/10/26", 2026, 10, 1)]
    public void Reads_a_day_month_year_date_from_a_title(string title, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), InspectorCatalog.DateFromTitle(title));
    }

    [Theory]
    [InlineData("Inspector Barracuda (обновление ПО) v.1.2.5")]
    [InlineData("Inspector X (обновление базы данных) 31/02/26")]
    public void Finds_no_date_in_a_title_without_a_valid_one(string title)
    {
        Assert.Null(InspectorCatalog.DateFromTitle(title));
    }

    [Fact]
    public async Task Explains_that_database_files_are_hosted_on_another_site()
    {
        var page = """<html><body><a class="url_file_btn" href="https://www.cenmax.ru/upload/db.zip">Скачать</a></body></html>""";
        var catalog = new InspectorCatalog(new HttpClient(new StubHandler(page)));
        var model = new CatalogModel("Комбо", "Cenmax Alfa Signature",
            [new UpdateLink(UpdateKind.Database, "Cenmax Alfa Signature (обновление базы данных) 09/09/26", "/support/cenmax-db/")]);

        var error = await Assert.ThrowsAsync<CatalogFormatException>(
            () => catalog.GetUpdateInfoAsync(model, region: null, CancellationToken.None));

        Assert.Contains("на сайте производителя", error.Message);
        Assert.DoesNotContain("изменился", error.Message);
    }

    [Fact]
    public async Task Collects_the_emap_archive_link()
    {
        var page = """<html><body><a class="url_file_btn" href="https://www.inspector-update.me/SOFT/PO/eMap_Q2_2021.rar">Скачать</a></body></html>""";
        var catalog = new InspectorCatalog(new HttpClient(new StubHandler(page)));
        var model = new CatalogModel("Комбо", "Inspector AtlaS",
        [
            new UpdateLink(UpdateKind.Database, "Inspector AtlaS (обновление базы данных) 09/09/26", "/support/atlas-db/"),
            new UpdateLink(UpdateKind.EMap, "Inspector AtlaS (обновление eMap) Q2'2021", "/support/atlas-emap/"),
        ]);

        var info = await catalog.GetUpdateInfoAsync(model, region: null, CancellationToken.None);

        Assert.EndsWith(".rar", info.EMapUrl);
    }

    [Fact]
    public async Task Picks_firmware_for_the_requested_region()
    {
        var pro = await ModelAsync(Catalog(), "Inspector Marlin S Pro");

        var chosen = InspectorCatalog.SelectFirmware(pro, "РФ");

        Assert.Equal("РФ", chosen!.Region);
    }

    [Fact]
    public async Task Refuses_regional_model_without_region()
    {
        var pro = await ModelAsync(Catalog(), "Inspector Marlin S Pro");

        var error = Assert.Throws<CatalogFormatException>(() => InspectorCatalog.SelectFirmware(pro, region: null));

        Assert.Contains("регион", error.Message);
    }

    [Fact]
    public async Task Takes_the_only_firmware_when_there_are_no_regions()
    {
        var barracuda = await ModelAsync(Catalog(), "Inspector Barracuda");

        var chosen = InspectorCatalog.SelectFirmware(barracuda, region: null);

        Assert.Null(chosen!.Region);
        Assert.Equal(UpdateKind.Firmware, chosen.Kind);
    }

    [Fact]
    public async Task Explains_that_usb_models_are_not_supported()
    {
        var catalog = Catalog();
        var scout = await ModelAsync(catalog, "Inspector Scout");

        var error = await Assert.ThrowsAsync<CatalogFormatException>(
            () => catalog.GetUpdateInfoAsync(scout, region: "РФ", CancellationToken.None));

        Assert.Contains("через кабель", error.Message);
    }

    [Fact]
    public async Task Explains_when_the_page_is_not_recognised()
    {
        var catalog = new InspectorCatalog(new HttpClient(new StubHandler("<html><body>другой сайт</body></html>")));

        var error = await Assert.ThrowsAsync<CatalogFormatException>(() => catalog.GetModelsAsync(CancellationToken.None));

        Assert.Contains("Похоже, сайт Inspector изменился", error.Message);
    }

    [Fact]
    public void Reads_version_from_a_title_with_a_space_after_v()
    {
        // The site writes both "v.1.2.5" (Barracuda) and "v. 1.0.5" (AtlaS).
        Assert.Equal(new Version(1, 2, 5), InspectorCatalog.VersionFromTitle("Inspector Barracuda (обновление ПО) v.1.2.5"));
        Assert.Equal(new Version(1, 0, 5), InspectorCatalog.VersionFromTitle("Inspector AtlaS (обновление ПО) v. 1.0.5"));
    }

    [Fact]
    public void Does_not_invent_a_version_where_there_is_none()
    {
        Assert.Null(InspectorCatalog.VersionFromTitle("Inspector X (обновление базы данных) 09/09/26"));
        Assert.Null(InspectorCatalog.VersionFromTitle("Inspector AtlaS (обновление eMap) Q2'2021"));
    }

    [Fact]
    public void Archive_firmware_must_be_an_archive_of_the_same_model()
    {
        const string model = "Inspector Barracuda";

        // Accepted: a Barracuda firmware archive from the ARCHIVE section.
        Assert.Equal(new Version(1, 2, 0), InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/ARCHIVE/BARRACUDA_FW_1.2.0.zip", model));

        // Rejected: a PDF manual with a version number in its name.
        Assert.Null(InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/ARCHIVE/manual_barracuda_v2.1.pdf", model));

        // Rejected: another model.
        Assert.Null(InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/ARCHIVE/SPARTA_FW_1.2.0.zip", model));

        // Rejected: a file outside the ARCHIVE section.
        Assert.Null(InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/PLAYER/INSPECTOR_PC_Viewer_V1.0.4.zip", model));
    }

    [Fact]
    public void Archive_firmware_matches_a_model_name_of_several_words()
    {
        Assert.Equal(new Version(1, 0, 5), InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/ARCHIVE/MARLIN_S_PRO_1.0.5_RF.zip", "Inspector Marlin S Pro"));
    }

    [Theory]
    // On the site a short model name is often part of a longer one: Marlin S and Marlin S Pro,
    // Scat, Scat S and Scat SE, Shot Air and Shot Air Pro, Cayman and Cayman S.
    [InlineData("Inspector Marlin S", "MARLIN_S_PRO_1.0.5_RF.zip")]
    [InlineData("Inspector Mike S", "MIKE_S_PRO_1.0.3_UZ.zip")]
    [InlineData("Inspector Shark", "SHARK_LITE_1.0.7.zip")]
    [InlineData("Inspector Scat", "SCAT_S_2.3.4.zip")]
    [InlineData("Inspector Scat S", "SCAT_SE_2.3.2.zip")]
    [InlineData("Inspector Cayman", "CAYMAN_S_1.6.3.zip")]
    [InlineData("Inspector Shot Air", "SHOT_AIR_PRO_1.0.0.zip")]
    [InlineData("Inspector Alfa", "ALFA_SIGNATURE_2.0.0.zip")]
    // The same factory makes the Cenmax Alfa Signature.
    [InlineData("Inspector Alfa", "CENMAX_ALFA_2.0.0.zip")]
    // A file shared by several models, like the eMap "Scat SE_SEQ_MapS_AtlaS_…" file.
    [InlineData("Inspector AtlaS", "Scat SE_SEQ_MapS_AtlaS_1.0.5.zip")]
    public void Archive_firmware_of_a_longer_model_name_is_not_taken(string model, string file)
    {
        Assert.Null(InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/ARCHIVE/" + file, model));
    }

    [Theory]
    [InlineData("Inspector Marlin S Pro", "MARLIN_S_PRO_1.0.5_RF.zip", "1.0.5")]
    [InlineData("Inspector Scat SE", "SCAT_SE_2.3.2.zip", "2.3.2")]
    [InlineData("Inspector Shark Lite", "SHARK_LITE_1.0.7.zip", "1.0.7")]
    [InlineData("Inspector Barracuda", "Inspector_Barracuda_FW_1.2.0.zip", "1.2.0")]
    // Real file names from the site: the model name without separators and a radar part marker after the version.
    [InlineData("Inspector Marlin S", "MarlinS_1.6.4+E3.0.zip", "1.6.4")]
    [InlineData("Inspector AtlaS", "AtlaS_1.0.5+E3.0.zip", "1.0.5")]
    [InlineData("Inspector Marlin S Pro", "MARLIN S PRO_SW1.0.4+S.1.3 RF.zip", "1.0.4")]
    public void Archive_firmware_of_the_model_itself_is_still_taken(string model, string file, string version)
    {
        Assert.Equal(Version.Parse(version), InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/ARCHIVE/" + file, model));
    }

    [Fact]
    public void Archive_firmware_version_is_not_taken_from_the_model_name()
    {
        // The model name "Inspector Marlin S Pro 2.0" contains its own number, which is not a version.
        Assert.Equal(new Version(1, 0, 2), InspectorCatalog.ArchiveFirmwareVersion(
            "https://www.inspector-update.me/SOFT/ARCHIVE/MARLIN_S_PRO_2.0_1.0.2.zip", "Inspector Marlin S Pro 2.0"));
    }

    [Fact]
    public void Version_is_taken_only_from_the_v_mark()
    {
        Assert.Equal(new Version(1, 0, 2),
            InspectorCatalog.VersionFromTitle("Inspector Marlin S Pro 2.0 (обновление ПО) v.1.0.2"));
        Assert.Null(InspectorCatalog.VersionFromTitle("Inspector X (обновление ПО) от 09.09.2026"));
        Assert.Null(InspectorCatalog.VersionFromTitle("Inspector Marlin S Pro (обновление ПО) для РФ"));
    }

    [Theory]
    [InlineData("Inspector Spirit")]
    [InlineData("Inspector Tau S")]
    [InlineData("Inspector RD X2 Gamma")]
    [InlineData("Inspector RD X3 Beta")]
    public async Task Refuses_models_updated_over_a_cable(string name)
    {
        var catalog = Catalog();
        var models = await catalog.GetModelsAsync(CancellationToken.None);
        var model = models.FirstOrDefault(m => m.Name == name);
        Assert.NotNull(model);

        var error = await Assert.ThrowsAsync<CatalogFormatException>(
            () => catalog.GetUpdateInfoAsync(model, region: "РФ", CancellationToken.None));

        Assert.Contains("через кабель", error.Message);
    }

    [Theory]
    [InlineData("Inspector Star Air")]
    [InlineData("Inspector Shot Air")]
    [InlineData("Inspector Split Air")]
    public async Task Air_series_still_updates_from_the_card(string name)
    {
        var models = await Catalog().GetModelsAsync(CancellationToken.None);
        var model = models.FirstOrDefault(m => m.Name == name);
        Assert.NotNull(model);

        // The call cannot complete because fixtures exist only for Barracuda,
        // but it must not fail with the cable refusal.
        var error = await Record.ExceptionAsync(
            () => Catalog().GetUpdateInfoAsync(model, region: null, CancellationToken.None));

        Assert.DoesNotContain("через кабель", error?.Message ?? "");
    }
}
