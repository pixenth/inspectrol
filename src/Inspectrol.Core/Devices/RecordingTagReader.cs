using System.Text;
using System.Text.RegularExpressions;

namespace Inspectrol.Core.Devices;

/// <remarks>
/// MP4 (Sparta, AtlaS): the tag "EMTO&lt;model&gt;_&lt;version&gt;" is in <c>udta</c> inside <c>moov</c>, and the
/// dashcam writes <c>moov</c> at the end of the file, so the reader walks the top-level atoms instead of reading
/// the whole file.
/// AVI (Barracuda): the model name is in the <c>strd</c> chunk and there is no firmware version at all.
/// </remarks>
public static partial class RecordingTagReader
{
    // The tag is at the start of moov, which can be megabytes long.
    private const int MoovBytesToRead = 64 * 1024;

    // AVI header chunks come first, followed by the video data.
    private const int RiffBytesToRead = 64 * 1024;

    // Guards against truncated or corrupt files.
    private const int MaxTopLevelAtoms = 64;

    // RIFF > LIST hdrl > LIST strl > strd
    private const int MaxRiffDepth = 4;

    private const int MinModelNameLength = 3;
    private const int MaxModelNameLength = 32;

    public static RecordingTag? Read(FileInfo file)
    {
        try
        {
            // The card may be open in Explorer, so do not lock out other readers and writers.
            using var stream = file.Open(new FileStreamOptions
            {
                Mode = FileMode.Open,
                Access = FileAccess.Read,
                Share = FileShare.ReadWrite,
            });

            return IsRiff(stream) ? ReadFromRiff(stream) : ReadFromMp4(stream);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsRiff(Stream stream)
    {
        var head = new byte[4];
        stream.Position = 0;
        return stream.ReadAtLeast(head, 4, throwOnEndOfStream: false) == 4
            && Encoding.ASCII.GetString(head) == "RIFF";
    }

    private static RecordingTag? ReadFromMp4(Stream stream)
    {
        var moov = FindMoov(stream);
        if (moov is null)
            return null;

        var text = ReadText(stream, moov.Value.Offset, (int)Math.Min(moov.Value.Size, MoovBytesToRead));
        var match = TagRegex().Match(text);
        if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var firmware))
            return null;

        return new RecordingTag(match.Groups["model"].Value, firmware);
    }

    // Walk the chunk tree instead of searching for "strd": those four bytes can occur by chance in the video data.
    private static RecordingTag? ReadFromRiff(Stream stream)
    {
        var data = ReadBytes(stream, 0, (int)Math.Min(stream.Length, RiffBytesToRead));

        // Skip the "RIFF" id, the size and the file type; chunks follow.
        var model = FindStreamData(data, 12, data.Length, depth: 0);
        return model is null ? null : new RecordingTag(model, null);
    }

    private static string? FindStreamData(byte[] data, int start, int end, int depth)
    {
        if (depth > MaxRiffDepth)
            return null;

        var position = start;

        while (position + 8 <= end)
        {
            var id = Encoding.ASCII.GetString(data, position, 4);
            long size = ReadUInt32LittleEndian(data, position + 4);
            var payload = position + 8;
            var payloadEnd = (int)Math.Min(end, payload + size);

            if (id == "strd")
                return ModelFrom(data, payload, payloadEnd);

            // A list holds child chunks after its four-byte list type.
            if (id is "LIST" or "RIFF" && payload + 4 < payloadEnd
                && FindStreamData(data, payload + 4, payloadEnd, depth + 1) is { } inside)
            {
                return inside;
            }

            // Chunks are padded to an even number of bytes.
            var next = payload + size + (size % 2);
            if (next <= position || next > int.MaxValue)
                return null;

            position = (int)next;
        }

        return null;
    }

    // On Barracuda the rest of strd is a GPS track of the user's trips, which must not leak into the tag. The name
    // stops at the first byte outside [A-Za-z0-9_-], space included: "BARRACUDA 55 7558 N" is already coordinates.
    private static string? ModelFrom(byte[] data, int start, int end)
    {
        var length = 0;
        while (start + length < end
            && length < MaxModelNameLength
            && IsModelNameByte(data[start + length]))
        {
            length++;
        }

        // A name that runs to the end of the buffer may be truncated.
        if (start + length >= end)
            return null;

        if (length < MinModelNameLength || !char.IsLetter((char)data[start]))
            return null;

        return Encoding.ASCII.GetString(data, start, length);
    }

    private static bool IsModelNameByte(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z'
            or >= (byte)'a' and <= (byte)'z'
            or >= (byte)'0' and <= (byte)'9'
            or (byte)'_' or (byte)'-';

    private static uint ReadUInt32LittleEndian(byte[] bytes, int offset) =>
        bytes[offset] | ((uint)bytes[offset + 1] << 8)
        | ((uint)bytes[offset + 2] << 16) | ((uint)bytes[offset + 3] << 24);

    private static byte[] ReadBytes(Stream stream, long offset, int length)
    {
        if (length <= 0)
            return [];

        var buffer = new byte[length];
        stream.Position = offset;
        var read = stream.ReadAtLeast(buffer, length, throwOnEndOfStream: false);
        return read == length ? buffer : buffer[..read];
    }

    private static string ReadText(Stream stream, long offset, int length)
    {
        if (length <= 0)
            return "";

        var buffer = new byte[length];
        stream.Position = offset;
        var read = stream.ReadAtLeast(buffer, length, throwOnEndOfStream: false);

        // Latin-1 maps every byte to exactly one character, so arbitrary binary data is safe to decode.
        return Encoding.Latin1.GetString(buffer, 0, read);
    }

    private static (long Offset, long Size)? FindMoov(Stream stream)
    {
        var header = new byte[8];
        long position = 0;

        for (var atom = 0; atom < MaxTopLevelAtoms; atom++)
        {
            if (position + 8 > stream.Length)
                return null;

            stream.Position = position;
            if (stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) < 8)
                return null;

            long size = ReadUInt32(header, 0);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8;

            if (size == 1)
            {
                // The size did not fit in four bytes; the real 64-bit size follows the header.
                var extended = new byte[8];
                if (stream.ReadAtLeast(extended, 8, throwOnEndOfStream: false) < 8)
                    return null;
                size = (long)(((ulong)ReadUInt32(extended, 0) << 32) | ReadUInt32(extended, 4));
                headerSize = 16;
            }
            else if (size == 0)
            {
                // Zero means the atom extends to the end of the file.
                size = stream.Length - position;
            }

            if (size < headerSize || position + size > stream.Length)
                return null;

            if (type == "moov")
                return (position + headerSize, size - headerSize);

            position += size;
        }

        return null;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16)
        | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];

    // "EMTOAtlaS_1.0.3" -> model "AtlaS", version "1.0.3"
    [GeneratedRegex(@"EMTO(?<model>[A-Za-z0-9 \-_]*?)_(?<version>\d+(?:\.\d+)+)")]
    private static partial Regex TagRegex();
}
