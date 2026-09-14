namespace Inspectrol.Core.Cards;

public enum CardWriteStage { NotStarted, Checked, Formatting, Formatted, Copied, Verified, Ejected }

/// <param name="Problem">Text for the user. The system's own error message goes to <paramref name="Details"/>.</param>
/// <param name="NeedsAdministrator">Lets the wizard offer manual formatting without matching the problem text.</param>
/// <param name="CardAlreadyErased">
/// Set explicitly rather than derived from <paramref name="Stage"/>: a write without erasing also reaches
/// <see cref="CardWriteStage.Copied"/>, and the user must not be told the recordings are gone.
/// </param>
public sealed record CardWriteResult(
    bool Success,
    CardWriteStage Stage,
    string? Problem,
    bool NeedsAdministrator = false,
    bool CardAlreadyErased = false,
    string? Details = null,
    bool FilesPartlyWritten = false);

/// <remarks>
/// Every check that can fail runs before formatting, while the card is still intact. The card is compared with the
/// snapshot the user confirmed because it may have been swapped in the meantime.
/// </remarks>
public sealed class CardWriter(ICardStorage storage)
{
    /// <summary>Formats the card and writes the files to its root.</summary>
    public async Task<CardWriteResult> WriteAsync(
        IReadOnlyList<CardFile> files,
        DirectoryInfo cardRoot,
        CardSnapshot confirmed,
        string modelName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (CheckFiles(files) is { } fileProblem)
            return Failed(CardWriteStage.NotStarted, fileProblem);

        if (!storage.IsRemovable(cardRoot))
        {
            return Failed(CardWriteStage.NotStarted,
                string.Format(Strings.CardWriter_NotRemovableForFormat, cardRoot.Name));
        }

        if (Describe(cardRoot) is not { } now)
        {
            return Failed(CardWriteStage.NotStarted,
                Strings.CardWriter_CardMissing);
        }

        if (now != confirmed)
        {
            return Failed(CardWriteStage.NotStarted,
                Strings.CardWriter_CardReplacedBeforeFormat);
        }

        if (CheckCard(files, now, modelName) is { } cardProblem)
            return Failed(CardWriteStage.NotStarted, cardProblem);

        var fileSystem = CardRequirements.For(modelName).FileSystemFor(now.TotalBytes);

        // From here on the card counts as erased: an interrupted format also destroys the file system.
        var stage = CardWriteStage.Formatting;

        try
        {
            await storage.FormatAsync(cardRoot, fileSystem, cancellationToken);
            stage = CardWriteStage.Formatted;
        }
        catch (CardNeedsAdministratorException error)
        {
            // Windows refused before touching the card, so formatting never started.
            return new CardWriteResult(false, CardWriteStage.Checked, error.Message, NeedsAdministrator: true);
        }
        catch (CardNotRemovableException error)
        {
            // Unexpected after the IsRemovable check above, but report the real cause if it happens.
            return Failed(CardWriteStage.NotStarted, error.Message);
        }
        catch (Exception error) when (error is OperationCanceledException or UnauthorizedAccessException or IOException)
        {
            return Failed(stage, Explain(error, stage), erased: true, details: Details(error));
        }

        return await CopyAndVerifyAsync(files, cardRoot, stage, erased: true, progress, cancellationToken);
    }

    /// <summary>
    /// Used when Windows refuses to format without administrator rights and the user formats the card in Explorer.
    /// </summary>
    /// <remarks>
    /// Formatting changes the label, the file system and the usable size, so the card can only be matched by
    /// approximate size. In exchange it must be empty and have the expected file system.
    /// </remarks>
    public async Task<CardWriteResult> WriteToPreparedCardAsync(
        IReadOnlyList<CardFile> files,
        DirectoryInfo cardRoot,
        long expectedTotalBytes,
        string modelName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (CheckFiles(files) is { } fileProblem)
            return Failed(CardWriteStage.NotStarted, fileProblem);

        if (!storage.IsRemovable(cardRoot))
        {
            return Failed(CardWriteStage.NotStarted,
                string.Format(Strings.CardWriter_NotRemovableForWrite, cardRoot.Name));
        }

        if (Describe(cardRoot) is not { } now)
        {
            return Failed(CardWriteStage.NotStarted,
                Strings.CardWriter_CardMissing);
        }

        if (!WithinTolerance(now.TotalBytes, expectedTotalBytes))
        {
            return Failed(CardWriteStage.NotStarted,
                Strings.CardWriter_CardReplaced);
        }

        if (!storage.IsEmpty(cardRoot))
        {
            return Failed(CardWriteStage.NotStarted,
                Strings.CardWriter_CardNotEmpty);
        }

        var expectedFileSystem = CardRequirements.For(modelName).FileSystemFor(now.TotalBytes);
        if (!Matches(now.FileSystem, expectedFileSystem))
        {
            return Failed(CardWriteStage.NotStarted,
                string.Format(Strings.CardWriter_WrongFileSystem, now.FileSystem, Name(expectedFileSystem)));
        }

        if (CheckCard(files, now, modelName) is { } cardProblem)
            return Failed(CardWriteStage.NotStarted, cardProblem);

        return await CopyAndVerifyAsync(files, cardRoot, CardWriteStage.Formatted, erased: true, progress, cancellationToken);
    }

    /// <summary>
    /// The dashcam picks up update files from the card root and does not need a formatted card, so this path keeps
    /// the recordings and works without administrator rights.
    /// </summary>
    public async Task<CardWriteResult> WriteWithoutErasingAsync(
        IReadOnlyList<CardFile> files,
        DirectoryInfo cardRoot,
        CardSnapshot confirmed,
        string modelName,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (CheckFiles(files) is { } fileProblem)
            return Failed(CardWriteStage.NotStarted, fileProblem);

        if (!storage.IsRemovable(cardRoot))
        {
            return Failed(CardWriteStage.NotStarted,
                string.Format(Strings.CardWriter_NotRemovableForWrite, cardRoot.Name));
        }

        if (Describe(cardRoot) is not { } now)
        {
            return Failed(CardWriteStage.NotStarted,
                Strings.CardWriter_CardMissing);
        }

        // Nothing has touched the card yet, so the snapshot must match exactly.
        if (now != confirmed)
        {
            return Failed(CardWriteStage.NotStarted,
                Strings.CardWriter_CardReplaced);
        }

        if (CheckCard(files, now, modelName) is { } cardProblem)
            return Failed(CardWriteStage.NotStarted, cardProblem);

        if (CheckFreeSpace(files, cardRoot) is { } spaceProblem)
            return Failed(CardWriteStage.NotStarted, spaceProblem);

        return await CopyAndVerifyAsync(files, cardRoot, CardWriteStage.Checked, erased: false, progress, cancellationToken);
    }

    // Files with the same names get overwritten, so the space they occupy counts as free.
    private string? CheckFreeSpace(IReadOnlyList<CardFile> files, DirectoryInfo cardRoot)
    {
        var needed = files.Sum(file => file.Length);
        var free = storage.FreeBytes(cardRoot);

        foreach (var file in files)
        {
            try
            {
                var existing = new FileInfo(file.PathOn(cardRoot));
                if (existing.Exists)
                    free += existing.Length;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // The existing file cannot be inspected, so assume it frees no space.
            }
        }

        return free >= needed
            ? null
            : string.Format(Strings.CardWriter_NotEnoughFreeSpace, Megabytes(needed), Megabytes(free));
    }

    private static long Megabytes(long bytes) => bytes / 1000 / 1000;

    private async Task<CardWriteResult> CopyAndVerifyAsync(
        IReadOnlyList<CardFile> files,
        DirectoryInfo cardRoot,
        CardWriteStage stage,
        bool erased,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            await storage.CopyToCardAsync(files, cardRoot, progress, cancellationToken);
            stage = CardWriteStage.Copied;

            if (!await storage.VerifyAsync(files, cardRoot, cancellationToken))
            {
                return Failed(stage,
                    Strings.CardWriter_VerifyFailed, erased, partlyWritten: true);
            }

            stage = CardWriteStage.Verified;
        }
        catch (Exception error) when (error is OperationCanceledException or UnauthorizedAccessException or IOException)
        {
            return Failed(stage, Explain(error, stage), erased, Details(error), partlyWritten: true);
        }

        // A failed eject is harmless: the files are already verified and the user can remove the card manually.
        if (await storage.TryEjectAsync(cardRoot, cancellationToken))
            stage = CardWriteStage.Ejected;

        return new CardWriteResult(true, stage, null, CardAlreadyErased: erased);
    }

    private static string? CheckFiles(IReadOnlyList<CardFile> files)
    {
        if (files.Count == 0)
            return Strings.CardWriter_NoFiles;

        foreach (var file in files)
        {
            file.Source.Refresh();

            if (!file.Source.Exists)
                return string.Format(Strings.CardWriter_FileNotFound, file.CardPath);

            // Update files in the card root are never empty, but the eMap folder holds empty markers ("reinstall.dat").
            if (file.Length == 0 && !file.InFolder)
                return string.Format(Strings.CardWriter_FileEmpty, file.CardPath);
        }

        var duplicate = files
            .GroupBy(file => file.CardPath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        return duplicate is null
            ? null
            : string.Format(Strings.CardWriter_DuplicateFileNames, duplicate.Key);
    }

    private static string? CheckCard(IReadOnlyList<CardFile> files, CardSnapshot card, string modelName)
    {
        if (CardRequirements.For(modelName).Check(card.TotalBytes) is { } problem)
            return problem;

        var needed = files.Sum(file => file.Length);
        return needed > card.TotalBytes
            ? string.Format(Strings.CardWriter_FilesLargerThanCard, needed / 1000 / 1000)
            : null;
    }

    private CardSnapshot? Describe(DirectoryInfo root)
    {
        try
        {
            return storage.Describe(root);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // Usable volume size changes with the file system and cluster size, so an exact match would reject a card
    // the user formatted correctly.
    private static bool WithinTolerance(long actual, long expected) =>
        Math.Abs(actual - expected) <= Math.Max(expected / 20, 64L * 1024 * 1024);

    private static bool Matches(string actual, CardFileSystem expected) =>
        actual.Replace(" ", "").Equals(Name(expected), StringComparison.OrdinalIgnoreCase);

    private static string Name(CardFileSystem fileSystem) =>
        fileSystem == CardFileSystem.Fat32 ? "FAT32" : "exFAT";

    private static string Explain(Exception error, CardWriteStage stage) => (error, stage) switch
    {
        (OperationCanceledException, _) => Strings.CardWriter_Cancelled,

        (UnauthorizedAccessException, _) =>
            Strings.CardWriter_WriteProtected,

        (_, CardWriteStage.Formatting) =>
            Strings.CardWriter_FormatFailed,

        _ =>
            Strings.CardWriter_ConnectionLost,
    };

    // Null when the user-facing text already says everything.
    private static string? Details(Exception error) =>
        error is OperationCanceledException or UnauthorizedAccessException ? null : error.Message;

    private static CardWriteResult Failed(
        CardWriteStage stage, string problem, bool erased = false, string? details = null, bool partlyWritten = false) =>
        new(false, stage, problem, CardAlreadyErased: erased, Details: details, FilesPartlyWritten: partlyWritten);
}
