using Inspectrol.Core.Catalog;
using Inspectrol.Core.Devices;
using Inspectrol.Core.Planning;

// Manual check against the live Inspector site:
//   dotnet run --project tools/Inspectrol.Cli -- "Inspector Barracuda" 1.1.0
// Without arguments it checks the three models that real test cards are available for.

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Inspects a real card: dotnet run --project tools/Inspectrol.Cli -- card F:\
if (args.Length >= 2 && args[0] == "card")
{
    var cardRoot = new DirectoryInfo(args[1]);
    using var cardHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    cardHttp.DefaultRequestHeaders.UserAgent.ParseAdd("Inspectrol/0.1 (catalog check)");

    var recordings = CardRecordings.Find(cardRoot);
    Console.WriteLine($"Записей на карте: {recordings.Count}");
    foreach (var file in recordings.Take(3))
        Console.WriteLine($"  {file.Name}  от {file.LastWriteTime}  {file.Length / 1024 / 1024} МБ");

    foreach (var file in recordings.Take(3))
        Console.WriteLine($"  метка в {file.Name}: {RecordingTagReader.Read(file)?.ToString() ?? "нет"}");

    var catalogModels = await new InspectorCatalog(cardHttp).GetModelsAsync(CancellationToken.None);
    var recognition = DeviceRecognition.Recognise(cardRoot, catalogModels);
    if (recognition.Device is null)
    {
        Console.WriteLine($"Не опознано: {recognition.Explanation}");
        return 1;
    }

    Console.WriteLine($"Опознано: {recognition.Device.Model.Name}, прошивка {recognition.Device.Firmware}, "
        + $"уверенность: {recognition.Device.Confidence}");
    return 0;
}

// Extracts an update archive and lists what would be written to the card:
//   dotnet run --project tools/Inspectrol.Cli -- extract archive.rar folder
if (args.Length >= 3 && args[0] == "extract")
{
    var cardFiles = Inspectrol.Core.Downloading.ArchiveExtractor.Extract(new FileInfo(args[1]), new DirectoryInfo(args[2]));
    Console.WriteLine($"Файлов для карты: {cardFiles.Count}, всего {cardFiles.Sum(file => file.Length) / 1_000_000} МБ");
    foreach (var file in cardFiles.Take(40))
        Console.WriteLine($"  {file.CardPath}  {file.Length} байт");
    return 0;
}

var checks = args.Length > 0
    ? [(Name: args[0], Current: args.Length > 1 && Version.TryParse(args[1], out var v) ? v : new Version(1, 0, 0))]
    : new (string Name, Version Current)[]
    {
        ("Inspector Sparta", new Version(1, 0, 1)),
        ("Inspector AtlaS", new Version(1, 0, 3)),
        ("Inspector Barracuda", new Version(1, 1, 0)),
    };

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Inspectrol/0.1 (catalog check)");
var catalog = new InspectorCatalog(http);

IReadOnlyList<CatalogModel> models;
try
{
    models = await catalog.GetModelsAsync(CancellationToken.None);
}
catch (Exception error) when (error is CatalogFormatException or HttpRequestException or TaskCanceledException)
{
    Console.WriteLine($"Не удалось прочитать список моделей: {error.Message}");
    return 1;
}

Console.WriteLine($"Моделей на сайте: {models.Count}");

var failed = false;
foreach (var (name, current) in checks)
{
    Console.WriteLine();
    Console.WriteLine(new string('=', 70));
    var model = models.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    if (model is null)
    {
        Console.WriteLine($"Модель «{name}» не найдена.");
        failed = true;
        continue;
    }

    Console.WriteLine($"{model.Name}  ({model.Category})");
    foreach (var update in model.Updates)
        Console.WriteLine($"  есть: {update.Kind,-20} {update.Title}");

    ModelUpdateInfo info;
    try
    {
        info = await catalog.GetUpdateInfoAsync(model, region: "РФ", CancellationToken.None);
    }
    catch (Exception error) when (error is CatalogFormatException or HttpRequestException or TaskCanceledException)
    {
        Console.WriteLine($"  не разобрать: {error.Message}");
        failed = true;
        continue;
    }

    Console.WriteLine();
    Console.WriteLine($"  прошивка на сайте: {info.LatestFirmware?.ToString() ?? "нет"}"
        + (info.FirmwareRequires is null ? "" : $", ставится поверх {info.FirmwareRequires} и новее"));
    Console.WriteLine($"  на устройстве:     {current}");
    Console.WriteLine($"  база:              {info.DatabaseDate?.ToString("dd.MM.yyyy") ?? "без даты"}"
        + (info.DatabaseRequiresFirmware is null ? "" : $" (нужна прошивка {info.DatabaseRequiresFirmware} и новее)"));
    Console.WriteLine($"  промежуточных прошивок в архиве: {info.ArchiveFirmwares.Count}");
    Console.WriteLine($"  карты eMap:        {info.EMapUrl ?? "нет"}");

    foreach (var warning in info.FirmwareWarnings)
        Console.WriteLine($"  предупреждение Inspector о прошивке: {warning}");

    foreach (var warning in info.DatabaseWarnings)
        Console.WriteLine($"  предупреждение Inspector о базе: {warning}");

    var allParts = UpdateParts.Firmware | UpdateParts.Database | UpdateParts.EMap;
    foreach (var tier in new[] { UpdateParts.Database, UpdateParts.Firmware | UpdateParts.Database, UpdateParts.Firmware, allParts })
    {
        var plan = UpdatePlanner.Build(info, tier, current);
        Console.WriteLine();
        Console.WriteLine($"  {tier}:");
        if (!plan.IsPossible)
        {
            Console.WriteLine($"    нельзя: {plan.Blocker}");
            continue;
        }
        foreach (var step in plan.Steps)
            Console.WriteLine($"    заход: {step.Title} -> {string.Join(", ", step.FileUrls.Select(u => Path.GetFileName(new Uri(u).LocalPath)))}");
    }
}

return failed ? 1 : 0;
