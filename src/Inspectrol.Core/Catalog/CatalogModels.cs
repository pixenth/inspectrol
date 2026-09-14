namespace Inspectrol.Core.Catalog;

public enum UpdateKind
{
    Unknown,
    Firmware,
    Database,
    FirmwareAndDatabase,
    EMap,
    FactoryFirmware,
}

/// <param name="Region">Set only where Inspector publishes separate files per country: "РФ" or "УЗ".</param>
public sealed record UpdateLink(UpdateKind Kind, string Title, string PageUrl, string? Region = null);

// Compare models by Name: record equality compares Updates by reference.
public sealed record CatalogModel(string Category, string Name, IReadOnlyList<UpdateLink> Updates);
