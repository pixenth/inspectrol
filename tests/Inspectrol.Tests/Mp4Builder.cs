using System.Text;

namespace Inspectrol.Tests;

// Real dashcam recordings are not kept in the repository: they are other people's private footage and hundreds of
// megabytes each, while the tag reader only needs the box headers.
internal static class Mp4Builder
{
    public static FileInfo WithTag(string tag) =>
        Write([.. Ftyp(), .. Mdat(), .. Atom("moov", Atom("udta", Ascii(tag)))]);

    public static FileInfo WithoutTag() =>
        Write([.. Ftyp(), .. Mdat(), .. Atom("moov", Atom("udta", Ascii("AMBAxV4")))]);

    public static FileInfo WithoutMoov() =>
        Write([.. Ftyp(), .. Mdat()]);

    public static FileInfo Truncated()
    {
        byte[] whole = [.. Ftyp(), .. Mdat(), .. Atom("moov", Atom("udta", Ascii("EMTOAtlaS_1.0.3")))];
        return Write(whole[..(whole.Length - 20)]);
    }

    private static byte[] Ftyp() => Atom("ftyp", Ascii("avc1avc1isom"));

    private static byte[] Mdat() => Atom("mdat", new byte[64]);

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    // The 32-bit big-endian size counts the 8-byte header.
    private static byte[] Atom(string type, byte[] payload)
    {
        var size = 8 + payload.Length;
        byte[] header =
        [
            (byte)(size >> 24), (byte)(size >> 16), (byte)(size >> 8), (byte)size,
            .. Ascii(type),
        ];
        return [.. header, .. payload];
    }

    private static FileInfo Write(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"inspectrol-{Guid.NewGuid():N}.MP4");
        File.WriteAllBytes(path, bytes);
        return new FileInfo(path);
    }
}
