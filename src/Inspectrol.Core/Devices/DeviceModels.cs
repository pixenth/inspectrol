using Inspectrol.Core.Catalog;

namespace Inspectrol.Core.Devices;

public sealed record RecordingTag(string ModelTag, Version? Firmware);

public enum RecognitionConfidence { Guessed, Verified }

public sealed record CardDevice(
    CatalogModel Model,
    Version? Firmware,
    RecognitionConfidence Confidence,
    bool FromUpdateFiles = false);

// Exactly one of Device and Explanation is set.
public sealed record CardRecognition(CardDevice? Device, string? Explanation)
{
    public static CardRecognition Recognised(CardDevice device) => new(device, null);

    public static CardRecognition Unknown(string explanation) => new(null, explanation);
}
