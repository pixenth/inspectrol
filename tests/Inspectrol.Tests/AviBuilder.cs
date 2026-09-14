using System.Text;

namespace Inspectrol.Tests;

// Real Barracuda recordings are not kept in the repository: they are about 375 MB each and contain someone else's
// footage together with a GPS track.
internal static class AviBuilder
{
    public static FileInfo WithModel(string model) => Write(Riff(Chunk("strd", Padded(model))));

    // The layout of a real recording.
    public static FileInfo WithModelInsideLists(string model) =>
        Write(Riff(List("hdrl", List("strl", [.. Chunk("strf", Ascii("H264")), .. Chunk("strd", Padded(model))]))));

    // As on real Barracuda cards, the GPS track follows the model name with no zero byte in between.
    public static FileInfo WithGpsRightAfterModel(string model)
    {
        var payload = Ascii(model + " 55 7558 N 037 6173 E 12 09 2026 $GNRMC,112944.00,A,V*13\r\n");
        return Write(Riff(Chunk("strd", payload)));
    }

    public static FileInfo WithFakeStrdInVideoData(string model)
    {
        byte[] video = [.. Ascii("00dc"), .. Ascii("strd"), .. new byte[8], .. Ascii("MUSORNOE_IMYA")];
        return Write(Riff([.. Chunk("movi", video), .. Chunk("strd", Padded(model))]));
    }

    public static FileInfo WithoutModel() => Write(Riff(Chunk("strf", Ascii("H264"))));

    public static FileInfo Truncated()
    {
        byte[] whole = Riff(Chunk("strd", Padded("BARRACUDA")));
        return Write(whole[..(whole.Length - 6)]);
    }

    private static byte[] Riff(byte[] body) =>
        [.. Ascii("RIFF"), .. LittleEndian(body.Length + 4), .. Ascii("AVI "), .. body];

    private static byte[] List(string type, byte[] body) =>
        [.. Ascii("LIST"), .. LittleEndian(body.Length + 4), .. Ascii(type), .. body];

    // RIFF chunks are padded to an even number of bytes.
    private static byte[] Chunk(string name, byte[] payload) => payload.Length % 2 == 0
        ? [.. Ascii(name), .. LittleEndian(payload.Length), .. payload]
        : [.. Ascii(name), .. LittleEndian(payload.Length), .. payload, (byte)0];

    private static byte[] Padded(string model) => [.. Ascii(model), 0, 0, 0, 0];

    private static byte[] Ascii(string text) => Encoding.ASCII.GetBytes(text);

    private static byte[] LittleEndian(int value) =>
        [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    private static FileInfo Write(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"inspectrol-{Guid.NewGuid():N}.AVI");
        File.WriteAllBytes(path, bytes);
        return new FileInfo(path);
    }
}
