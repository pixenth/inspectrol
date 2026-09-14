namespace Inspectrol.Core.Cards;

/// <param name="CardPath">
/// Relative to the card root with "/" as the separator: "firmware.bin", or "eMap/map/cis/map.kdb" for eMap maps.
/// </param>
public sealed record CardFile(FileInfo Source, string CardPath)
{
    public static CardFile InRoot(FileInfo source) => new(source, source.Name);

    public long Length => Source.Length;

    public bool InFolder => CardPath.Contains('/');

    public string PathOn(DirectoryInfo root) =>
        Path.Combine(root.FullName, CardPath.Replace('/', Path.DirectorySeparatorChar));
}
