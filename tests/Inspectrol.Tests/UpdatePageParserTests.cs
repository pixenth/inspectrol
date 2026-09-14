using Inspectrol.Core.Catalog;

namespace Inspectrol.Tests;

public class UpdatePageParserTests
{
    private static string Html(string name) => File.ReadAllText(Path.Combine("Fixtures", name));

    private static string PageWithButton(string href) => $$"""
        <html><body>
          <h1>Inspector X (обновление ПО) v.1.0</h1>
          <a class="url_file_btn" href="{{href}}">Скачать</a>
        </body></html>
        """;

    private static string InspectorPage(string body, bool withButton = true) => $$"""
        <html><body>
          <div class="bx_item_title"><h1><span>Inspector X (обновление ПО) v.1.0</span></h1></div>
          <div class="item_info_section">
        {{body}}
          </div>
          {{(withButton ? """<a class="url_file_btn" href="https://www.inspector-update.me/SOFT/PO/X_1.0.zip">Скачать</a>""" : "")}}
        </body></html>
        """;

    [Fact]
    public void Reads_firmware_page()
    {
        var page = UpdatePageParser.Parse(Html("barracuda-firmware.html"));

        Assert.Equal("https://www.inspector-update.me/SOFT/PO/BARRACUDA_FW_1.2.5.zip", page.FileUrl);
        Assert.Null(page.ForeignFileUrl);
        Assert.Equal(new Version(1, 2, 0), page.MinimumFirmware);
        Assert.Contains(page.ArchiveFileUrls, u => u.Contains("BARRACUDA_FW_1.2.0.zip"));
    }

    [Fact]
    public void Keeps_the_warning_whole()
    {
        var page = UpdatePageParser.Parse(Html("barracuda-firmware.html"));
        var warning = Assert.Single(page.Warnings);

        // Version numbers contain periods, so the warning cannot be cut at the first one.
        Assert.Contains("1.2.5", warning);
        Assert.Contains("не ниже версии 1.2.0", warning);

        // The end of the warning sits in a neighbouring element on the page.
        Assert.Contains("скачать которое можно по ссылке", warning);
    }

    [Fact]
    public void Reads_database_page()
    {
        var page = UpdatePageParser.Parse(Html("barracuda-database.html"));
        var warning = Assert.Single(page.Warnings);

        Assert.Equal("https://www.inspector-update.me/SOFT/DB/BarracudaDB_37.zip", page.FileUrl);
        Assert.Equal(new Version(1, 2, 3), page.MinimumFirmware);
        Assert.Contains("не ниже версии 1.2.3", warning);

        // The "!!!!!" before "ВНИМАНИЕ" sits in a separate element and is part of the warning too.
        Assert.StartsWith("!!!!!", warning);
    }

    [Fact]
    public void Keeps_every_warning_in_page_order()
    {
        var page = UpdatePageParser.Parse(InspectorPage("""
            <div><b>ВНИМАНИЕ:</b> <span>короткое предупреждение</span></div>
            <p>Обычный текст между предупреждениями.</p>
            <div><b>ВАЖНО:</b> <span>второе предупреждение, заметно длиннее первого</span></div>
            """));

        Assert.Equal(2, page.Warnings.Count);
        Assert.Contains("короткое предупреждение", page.Warnings[0]);
        Assert.Contains("второе предупреждение", page.Warnings[1]);
    }

    [Fact]
    public void Does_not_repeat_nested_warnings()
    {
        var page = UpdatePageParser.Parse(InspectorPage(
            """<div><b><i><span>ВНИМАНИЕ: одно предупреждение в трёх вложенных элементах</span></i></b></div>"""));

        var warning = Assert.Single(page.Warnings);
        Assert.Contains("одно предупреждение", warning);
    }

    [Fact]
    public void Unknown_markup_gives_empty_result()
    {
        var page = UpdatePageParser.Parse("<html><body><p>другой сайт</p></body></html>");

        Assert.Null(page.FileUrl);
        Assert.Null(page.ForeignFileUrl);
        Assert.Null(page.MinimumFirmware);
        Assert.Empty(page.Warnings);
        Assert.Empty(page.ArchiveFileUrls);
    }

    [Fact]
    public void Reports_button_without_address()
    {
        var page = """<html><body><a class="url_file_btn">Скачать</a></body></html>""";

        var error = Assert.Throws<CatalogFormatException>(() => UpdatePageParser.Parse(page));

        Assert.Contains("изменилась", error.Message);
    }

    [Fact]
    public void Reports_update_page_without_download_button()
    {
        var page = InspectorPage("<p>Файл обновления появится позже.</p>", withButton: false);

        var error = Assert.Throws<CatalogFormatException>(() => UpdatePageParser.Parse(page));

        Assert.Contains("изменилась", error.Message);
        Assert.Contains("Скачать", error.Message);
    }

    [Fact]
    public void Ignores_version_requirement_outside_the_warning()
    {
        // Inspector pages can have unrelated text with the same wording next to the warning.
        var page = UpdatePageParser.Parse(InspectorPage("""
            <div><b>ВНИМАНИЕ: убедитесь, что у вас установлено ПО не ниже версии 1.2.0</b></div>
            <p>Приложение для телефона требует Android не ниже версии 5.1.2</p>
            """));

        Assert.Equal(new Version(1, 2, 0), page.MinimumFirmware);
    }

    [Fact]
    public void Takes_the_strictest_requirement_when_warnings_disagree()
    {
        var page = UpdatePageParser.Parse(InspectorPage("""
            <div><b>ВНИМАНИЕ: нужно ПО не ниже версии 1.2.0</b></div>
            <div><b>ВАЖНО: для этой базы нужно ПО не ниже версии 1.2.3</b></div>
            """));

        Assert.Equal(new Version(1, 2, 3), page.MinimumFirmware);
    }

    [Fact]
    public void Keeps_foreign_download_separately()
    {
        var page = UpdatePageParser.Parse(PageWithButton("https://tomahawk.ru/obnovlenie/"));

        Assert.Null(page.FileUrl);
        Assert.Equal("https://tomahawk.ru/obnovlenie/", page.ForeignFileUrl);
    }

    [Fact]
    public void Ignores_files_on_other_hosts()
    {
        var page = UpdatePageParser.Parse(PageWithButton("https://evil.example/BarracudaDB_37.zip"));

        Assert.Null(page.FileUrl);
        Assert.Empty(page.ArchiveFileUrls);
    }

    [Fact]
    public void Ignores_warnings_outside_the_content_block()
    {
        var page = UpdatePageParser.Parse("""
            <html><body>
              <div class="header"><p>ВНИМАНИЕ! Сайт использует файлы cookie.</p></div>
              <div class="item_info_section"><p>Обычный текст обновления.</p></div>
              <a class="url_file_btn" href="https://www.inspector-update.me/SOFT/PO/X_1.0.zip">Скачать</a>
            </body></html>
            """);

        Assert.Empty(page.Warnings);
    }

    [Theory]
    [InlineData("ВНИМАНИЕ: обновление ставится на версии 1.2.0 и выше")]
    [InlineData("ВНИМАНИЕ: обновление ставится только поверх 1.2.0")]
    [InlineData("ВНИМАНИЕ: требуется ПО 1.2.0 или новее")]
    [InlineData("ВНИМАНИЕ: установите не ниже ПО версии 1.2.0")]
    public void Marks_a_requirement_it_could_not_read(string warning)
    {
        var page = UpdatePageParser.Parse(InspectorPage($"<div><b>{warning}</b></div>"));

        Assert.Null(page.MinimumFirmware);
        Assert.True(page.RequirementUnclear, "требование должно быть отмечено как непонятое");
    }

    [Fact]
    public void Notices_a_requirement_that_fell_into_the_next_block()
    {
        // The warning is split across two neighbouring blocks and the requirement is in the second one.
        var page = UpdatePageParser.Parse(InspectorPage("""
            <p><b>ВНИМАНИЕ:</b> ставить можно только на ПО</p>
            <p>не ниже версии 1.2.0, иначе устройство не включится.</p>
            """));

        Assert.True(page.RequirementUnclear, "потерянное требование должно быть замечено");
    }

    [Fact]
    public void Real_pages_have_no_unread_requirements()
    {
        Assert.False(UpdatePageParser.Parse(Html("barracuda-firmware.html")).RequirementUnclear);
        Assert.False(UpdatePageParser.Parse(Html("barracuda-database.html")).RequirementUnclear);
    }
}
