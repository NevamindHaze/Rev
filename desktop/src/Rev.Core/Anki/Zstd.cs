using ZstdSharp;

namespace Rev.Core.Anki;

/// <summary>
/// Unpacks data compressed with Zstandard ("zstd").
/// Anki's newest format compresses its database, its media list and its media files this way.
/// </summary>
public static class Zstd
{
    public static byte[] Decompress(byte[] data)
    {
        try
        {
            using var input = new DecompressionStream(new MemoryStream(data));
            using var output = new MemoryStream();
            input.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception error) when (error is ZstdException or IOException)
        {
            throw new InvalidDataException($"Couldn't unpack the Anki file: {error.Message}");
        }
    }
}
