using System.Text;

namespace Rev.Core.Anki;

/// <summary>
/// Reads the few bits of Google's "protobuf" format that Anki's newest files use
/// (note type settings, card templates, the media list).
/// A protobuf message is a list of numbered fields; we only need numbers, text and nested messages.
/// </summary>
public sealed class ProtobufMessage
{
    private readonly Dictionary<int, List<ulong>> numbers = [];
    private readonly Dictionary<int, List<byte[]>> blobs = [];

    public ProtobufMessage(byte[] bytes)
    {
        int index = 0;

        ulong? ReadVarint()
        {
            ulong result = 0;
            for (int shift = 0; index < bytes.Length && shift < 64; shift += 7)
            {
                byte b = bytes[index++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
            }
            return null;
        }

        while (index < bytes.Length)
        {
            if (ReadVarint() is not ulong key) break;
            int field = (int)(key >> 3);
            switch (key & 0x7)
            {
                case 0:   // a number
                    if (ReadVarint() is not ulong value) return;
                    Add(numbers, field, value);
                    break;
                case 1:   // 8 fixed bytes (not needed)
                    index += 8;
                    break;
                case 2:   // text, bytes or a nested message
                    if (ReadVarint() is not ulong length || length > (ulong)(bytes.Length - index)) return;
                    Add(blobs, field, bytes[index..(index + (int)length)]);
                    index += (int)length;
                    break;
                case 5:   // 4 fixed bytes (not needed)
                    index += 4;
                    break;
                default:
                    return;   // a format we don't understand; stop safely
            }
        }
    }

    private static void Add<T>(Dictionary<int, List<T>> map, int field, T value)
    {
        if (!map.TryGetValue(field, out var list)) map[field] = list = [];
        list.Add(value);
    }

    public string? String(int field) =>
        blobs.TryGetValue(field, out var list) ? Encoding.UTF8.GetString(list[0]) : null;

    public ulong? Number(int field) => numbers.TryGetValue(field, out var list) ? list[0] : null;

    public List<ProtobufMessage> Messages(int field) =>
        blobs.TryGetValue(field, out var list) ? list.Select(b => new ProtobufMessage(b)).ToList() : [];
}
