using System.Buffers.Binary;
using System.Text;

namespace Inspectrol.Core.Simulation;

// Minimal clips with the same model tags as real Inspector recordings.
internal static class FakeRecordings
{
    public static void WriteMp4(string path, string modelTag, Version firmware, int videoBytes)
    {
        using var stream = File.Create(path);
        stream.Write(Atom("ftyp", Encoding.ASCII.GetBytes("avc1\0\0\0\0isom")));
        stream.Write(Atom("mdat", new byte[videoBytes]));
        stream.Write(Atom("moov", Atom("udta", Encoding.ASCII.GetBytes($"AMBAxV4\0EMTO{modelTag}_{firmware}\0"))));
    }

    public static void WriteAvi(string path, string modelTag, int videoBytes)
    {
        var strd = Chunk("strd", [.. Encoding.ASCII.GetBytes(modelTag), 0, 0, 0, 0]);
        var header = List("hdrl", List("strl", strd));
        var movie = List("movi", new byte[videoBytes]);

        using var stream = File.Create(path);
        stream.Write(List("AVI ", [.. header, .. movie], id: "RIFF"));
    }

    private static byte[] Atom(string type, byte[] payload)
    {
        var atom = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(atom, (uint)atom.Length);
        Encoding.ASCII.GetBytes(type, atom.AsSpan(4, 4));
        payload.CopyTo(atom, 8);
        return atom;
    }

    private static byte[] Chunk(string id, byte[] payload)
    {
        var chunk = new byte[8 + payload.Length + payload.Length % 2];
        Encoding.ASCII.GetBytes(id, chunk.AsSpan(0, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)payload.Length);
        payload.CopyTo(chunk, 8);
        return chunk;
    }

    private static byte[] List(string type, byte[] children, string id = "LIST") =>
        Chunk(id, [.. Encoding.ASCII.GetBytes(type), .. children]);
}
