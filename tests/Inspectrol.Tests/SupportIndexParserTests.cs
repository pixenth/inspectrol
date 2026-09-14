using Inspectrol.Core.Catalog;

namespace Inspectrol.Tests;

public class SupportIndexParserTests
{
    private static string Html() => File.ReadAllText(Path.Combine("Fixtures", "support-index.html"));

    [Fact]
    public void Reads_barracuda_with_two_updates()
    {
        var models = SupportIndexParser.Parse(Html());

        var barracuda = models.Single(m => m.Name == "Inspector Barracuda");
        Assert.Equal("Комбо (видеорегистраторы с радар-детектором)", barracuda.Category);
        Assert.Equal(2, barracuda.Updates.Count);

        var firmware = barracuda.Updates.Single(u => u.Kind == UpdateKind.Firmware);
        Assert.Contains("1.2.5", firmware.Title);
        Assert.Equal("/support/inspector-barracuda-obnovlenie-po/", firmware.PageUrl);

        var database = barracuda.Updates.Single(u => u.Kind == UpdateKind.Database);
        Assert.Equal("/support/inspector-barracuda-obnovlenie/", database.PageUrl);
    }

    [Fact]
    public void Recognises_emap_and_factory_firmware()
    {
        var models = SupportIndexParser.Parse(Html());

        Assert.Contains(models.Single(m => m.Name == "Inspector MapS").Updates, u => u.Kind == UpdateKind.EMap);
        Assert.Contains(models.Single(m => m.Name == "Inspector Hermes").Updates, u => u.Kind == UpdateKind.FactoryFirmware);
    }

    [Fact]
    public void Marks_regional_firmware()
    {
        var models = SupportIndexParser.Parse(Html());

        var pro = models.Single(m => m.Name == "Inspector Marlin S Pro");
        var firmware = pro.Updates.Where(u => u.Kind == UpdateKind.Firmware).ToList();

        Assert.Equal(2, firmware.Count);
        Assert.Contains(firmware, u => u.Region == "РФ");
        Assert.Contains(firmware, u => u.Region == "УЗ");
        Assert.All(pro.Updates.Where(u => u.Kind == UpdateKind.Database), u => Assert.Null(u.Region));
    }

    [Fact]
    public void Leaves_region_empty_for_single_firmware()
    {
        var models = SupportIndexParser.Parse(Html());

        var barracuda = models.Single(m => m.Name == "Inspector Barracuda");

        Assert.All(barracuda.Updates, u => Assert.Null(u.Region));
    }

    [Fact]
    public void Returns_empty_list_for_unknown_markup()
    {
        var models = SupportIndexParser.Parse("<html><body><div>другой сайт</div></body></html>");

        Assert.Empty(models);
    }

    private static string PageWith(string title, string href = "/support/x/") => $$"""
        <ul class="p-support__quest">
          <li class="p-support__item"><span>Категория</span>
            <ul class="p-support__subquest"><ul class="p-support__quest">
              <li class="p-support__item"><span>Модель</span>
                <ul class="p-support__subquest">
                  <li class="p-support__subitem">
                    <span>{{title}}</span>
                    <div class="p-support__subitem-otv"><a href="{{href}}">Скачать</a></div>
                  </li>
                </ul>
              </li>
            </ul></ul>
          </li>
        </ul>
        """;

    [Fact]
    public void Finds_every_model_exactly_once()
    {
        var models = SupportIndexParser.Parse(Html());

        Assert.Equal(61, models.Count);
        Assert.DoesNotContain(models.GroupBy(m => m.Name), g => g.Count() > 1);
    }

    [Fact]
    public void Does_not_return_categories_as_models()
    {
        var models = SupportIndexParser.Parse(Html());

        Assert.All(models, m => Assert.NotEmpty(m.Category));
        Assert.DoesNotContain(models, m =>
            m.Name.StartsWith("Сигнатурные") || m.Name.StartsWith("Комбо")
            || m.Name.StartsWith("Видеорегистраторы") || m.Name.StartsWith("Радар-детекторы"));
    }

    [Fact]
    public void Reads_sister_brands()
    {
        var models = SupportIndexParser.Parse(Html());

        var zulu = models.Single(m => m.Name == "Tomahawk Zulu S");
        Assert.Contains(zulu.Updates, u => u.Kind == UpdateKind.FactoryFirmware);
        Assert.Contains(models, m => m.Name == "Cenmax Alfa Signature");
    }

    [Fact]
    public void Marks_recovery_page_as_unknown()
    {
        var models = SupportIndexParser.Parse(Html());

        var maps = models.Single(m => m.Name == "Inspector MapS");
        Assert.Contains(maps.Updates, u => u.Kind == UpdateKind.Unknown && u.Title.Contains("восстановлени"));
    }

    [Fact]
    public void Keeps_region_for_combined_updates()
    {
        var models = SupportIndexParser.Parse(Html());

        var scout = models.Single(m => m.Name == "Inspector Scout");
        var combined = scout.Updates.Where(u => u.Kind == UpdateKind.FirmwareAndDatabase).ToList();
        Assert.Equal(2, combined.Count);
        Assert.Contains(combined, u => u.Region == "РФ");
        Assert.Contains(combined, u => u.Region == "УЗ");
    }

    [Theory]
    [InlineData("Inspector X (обновление ПО) v.1.0", UpdateKind.Firmware)]
    [InlineData("Inspector X (обновление базы данных) 09/26", UpdateKind.Database)]
    [InlineData("Inspector X (обновление базовой прошивки) v.1.0", UpdateKind.Unknown)]
    [InlineData("Inspector X (процедура восстановления после неудачного обновления ПО)", UpdateKind.Unknown)]
    [InlineData("Inspector X (обновление ПО и базовой прошивки) v.1.0", UpdateKind.Firmware)]
    [InlineData("Inspector X (обновление ПО, версия 1.2)", UpdateKind.Firmware)]
    public void Classifies_titles(string title, UpdateKind expected)
    {
        var models = SupportIndexParser.Parse(PageWith(title));

        Assert.Equal(expected, models.Single().Updates.Single().Kind);
    }

    [Fact]
    public void Ignores_links_outside_support_section()
    {
        var page = """
            <ul class="p-support__quest">
              <li class="p-support__item"><span>Категория</span>
                <ul class="p-support__subquest"><ul class="p-support__quest">
                  <li class="p-support__item"><span>Модель</span>
                    <ul class="p-support__subquest">
                      <li class="p-support__subitem">
                        <span>Inspector X (обновление ПО) v.1.0</span>
                        <div class="p-support__subitem-otv"><a href="/support/x/">Скачать</a></div>
                      </li>
                      <li class="p-support__subitem">
                        <span>Inspector X (обновление базы данных) 09/26</span>
                        <div class="p-support__subitem-otv"><a href="https://evil.example/x.zip">Скачать</a></div>
                      </li>
                    </ul>
                  </li>
                </ul></ul>
              </li>
            </ul>
            """;

        var update = Assert.Single(SupportIndexParser.Parse(page).Single().Updates);

        Assert.Equal("/support/x/", update.PageUrl);
    }

    [Fact]
    public void Reports_when_all_links_lead_outside_support_section()
    {
        var page = PageWith("Inspector X (обновление ПО) v.1.0", href: "https://evil.example/x.zip");

        Assert.Throws<CatalogFormatException>(() => SupportIndexParser.Parse(page));
    }

    [Fact]
    public void Reports_unrecognised_markup_when_links_are_lost()
    {
        var broken = """
            <ul class="p-support__quest">
              <li class="p-support__item"><span>Модель</span>
                <ul class="p-support__subquest">
                  <li class="p-support__subitem">
                    <b>Inspector X (обновление ПО) v.1.0</b>
                    <div class="p-support__subitem-otv"><a href="/support/x/">Скачать</a></div>
                  </li>
                </ul>
              </li>
            </ul>
            """;

        var error = Assert.Throws<CatalogFormatException>(() => SupportIndexParser.Parse(broken));

        Assert.Contains("Страница поддержки Inspector изменилась", error.Message);
    }

    // The first brokenTitles titles are wrapped in <b> instead of <span>, so the parser cannot read them.
    private static string PageWithManyUpdates(int links, int brokenTitles)
    {
        var items = string.Join(Environment.NewLine, Enumerable.Range(0, links).Select(i =>
        {
            var title = $"Inspector X (обновление базы данных) {i:00}/26";
            var titleMarkup = i < brokenTitles ? $"<b>{title}</b>" : $"<span>{title}</span>";
            return $"""
                  <li class="p-support__subitem">
                    {titleMarkup}
                    <div class="p-support__subitem-otv"><a href="/support/x{i}/">Скачать</a></div>
                  </li>
                """;
        }));

        return $"""
            <ul class="p-support__quest">
              <li class="p-support__item"><span>Категория</span>
                <ul class="p-support__subquest"><ul class="p-support__quest">
                  <li class="p-support__item"><span>Модель</span>
                    <ul class="p-support__subquest">
            {items}
                    </ul>
                  </li>
                </ul></ul>
              </li>
            </ul>
            """;
    }

    [Fact]
    public void Reports_when_link_markup_is_renamed()
    {
        var page = Html().Replace("p-support__subitem-otv", "p-support__subitem-download");

        var error = Assert.Throws<CatalogFormatException>(() => SupportIndexParser.Parse(page));

        Assert.Contains("изменилась", error.Message);
        Assert.Contains("rd-inspector.ru/support/", error.Message);
    }

    [Fact]
    public void Reports_when_addresses_become_absolute()
    {
        var page = Html().Replace("href=\"/support/", "href=\"https://www.rd-inspector.ru/support/");

        Assert.Throws<CatalogFormatException>(() => SupportIndexParser.Parse(page));
    }

    [Fact]
    public void Reports_even_a_small_loss_of_updates()
    {
        var page = PageWithManyUpdates(links: 20, brokenTitles: 2);

        var error = Assert.Throws<CatalogFormatException>(() => SupportIndexParser.Parse(page));

        Assert.Contains("20", error.Message);
        Assert.Contains("18", error.Message);
    }
}
