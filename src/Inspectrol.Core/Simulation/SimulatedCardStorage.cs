using Inspectrol.Core.Cards;
using Inspectrol.Core.Devices;

namespace Inspectrol.Core.Simulation;

public sealed class SimulatedCardStorage(SimulatedCard card) : ICardStorage
{
    private const int ChunkBytes = 256 * 1024;

    public TimeSpan FormatDuration { get; init; } = TimeSpan.FromSeconds(3);

    // The simulated files are small, so copying is slowed down to look like a real card reader.
    public TimeSpan MinimumCopyDuration { get; init; } = TimeSpan.FromSeconds(6);

    public CardSnapshot Describe(DirectoryInfo root)
    {
        EnsureCard(root);
        var recordings = CardRecordings.Find(root);

        return new CardSnapshot(
            card.TotalBytes,
            "",
            card.FileSystem,
            recordings.Count,
            recordings.Count > 0 ? recordings[0].Name : "");
    }

    public bool IsRemovable(DirectoryInfo root) => IsCard(root);

    public bool IsEmpty(DirectoryInfo root) => IsCard(root) && !root.EnumerateFileSystemInfos().Any();

    public long FreeBytes(DirectoryInfo root) => IsCard(root) ? card.FreeBytes() : 0;

    public async Task FormatAsync(DirectoryInfo root, CardFileSystem fileSystem, CancellationToken cancellationToken)
    {
        if (!IsCard(root))
            throw new CardNotRemovableException(string.Format(Strings.WindowsCardStorage_NotRemovable, root.Name));

        if (card.NeedsAdministrator)
            throw new CardNeedsAdministratorException(Strings.WindowsCardStorage_NeedsAdministrator);

        await Task.Delay(FormatDuration, cancellationToken);
        EnsureCard(root);

        foreach (var entry in root.EnumerateFileSystemInfos())
            SimulatedCard.Delete(entry);

        card.FileSystem = fileSystem == CardFileSystem.Fat32 ? "FAT32" : "exFAT";
    }

    public async Task CopyToCardAsync(
        IReadOnlyList<CardFile> files,
        DirectoryInfo root,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var total = Math.Max(1, files.Sum(file => file.Length));
        var pause = MinimumCopyDuration / Math.Max(1, total / ChunkBytes);
        var buffer = new byte[ChunkBytes];
        long copied = 0;

        foreach (var file in files)
        {
            EnsureCard(root);
            var destination = file.PathOn(root);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            await using var source = file.Source.OpenRead();
            await using var target = File.Create(destination);

            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                EnsureCard(root);
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                progress?.Report((double)copied / total);

                if (pause > TimeSpan.Zero)
                    await Task.Delay(pause, cancellationToken);
            }
        }

        progress?.Report(1);
    }

    public Task<bool> VerifyAsync(IReadOnlyList<CardFile> files, DirectoryInfo root, CancellationToken cancellationToken) =>
        IsCard(root) ? new WindowsCardStorage().VerifyAsync(files, root, cancellationToken) : Task.FromResult(false);

    public async Task<bool> TryEjectAsync(DirectoryInfo root, CancellationToken cancellationToken)
    {
        await card.RemoveAsync();
        return true;
    }

    private bool IsCard(DirectoryInfo root) =>
        card.Inserted
        && string.Equals(
            Path.TrimEndingDirectorySeparator(root.FullName),
            Path.TrimEndingDirectorySeparator(card.Root.FullName),
            StringComparison.OrdinalIgnoreCase);

    private void EnsureCard(DirectoryInfo root)
    {
        if (!IsCard(root))
            throw new IOException(Strings.SimulatedCard_NotReady);
    }
}
