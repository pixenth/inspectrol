namespace Inspectrol.Core.Cards;

/// <remarks>
/// Size and label are not enough: two unlabelled 128 GB exFAT cards report sizes identical to the byte.
/// The recording count and the newest recording tell such cards apart.
/// </remarks>
public sealed record CardSnapshot(
    long TotalBytes,
    string Label,
    string FileSystem,
    int RecordingCount,
    string NewestRecording);

public interface ICardStorage
{
    CardSnapshot Describe(DirectoryInfo root);

    /// <summary>
    /// Only removable drives may ever be formatted. <see cref="FormatAsync"/> repeats this check so it cannot be
    /// bypassed.
    /// </summary>
    bool IsRemovable(DirectoryInfo root);

    bool IsEmpty(DirectoryInfo root);

    // Not part of CardSnapshot: it changes while writing and would break the same-card check.
    long FreeBytes(DirectoryInfo root);

    /// <summary>Erases the whole card. The only irreversible operation in the app.</summary>
    /// <exception cref="CardNeedsAdministratorException">Windows requires administrator rights to format.</exception>
    /// <exception cref="CardNotRemovableException">The drive is not removable.</exception>
    Task FormatAsync(DirectoryInfo root, CardFileSystem fileSystem, CancellationToken cancellationToken);

    // Creates folders as needed; progress goes from 0 to 1.
    Task CopyToCardAsync(
        IReadOnlyList<CardFile> files,
        DirectoryInfo root,
        IProgress<double>? progress,
        CancellationToken cancellationToken);

    Task<bool> VerifyAsync(IReadOnlyList<CardFile> files, DirectoryInfo root, CancellationToken cancellationToken);

    Task<bool> TryEjectAsync(DirectoryInfo root, CancellationToken cancellationToken);
}
