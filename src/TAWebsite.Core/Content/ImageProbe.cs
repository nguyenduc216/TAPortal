using System.Buffers.Binary;

namespace TAWebsite.Core.Content;

// Recognises an uploaded image by its first bytes (never by file name or browser-sent type) and reads its pixel size.
public static class ImageProbe
{
    public static (string ContentType, int? Width, int? Height)? Inspect(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 24 && d[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return ("image/png", BinaryPrimitives.ReadInt32BigEndian(d[16..]), BinaryPrimitives.ReadInt32BigEndian(d[20..]));
        if (d.Length >= 10 && (d[..6].SequenceEqual("GIF87a"u8) || d[..6].SequenceEqual("GIF89a"u8)))
            return ("image/gif", BinaryPrimitives.ReadUInt16LittleEndian(d[6..]), BinaryPrimitives.ReadUInt16LittleEndian(d[8..]));
        if (d.Length >= 30 && d[..4].SequenceEqual("RIFF"u8) && d[8..12].SequenceEqual("WEBP"u8))
        {
            var chunk = d[12..16];
            if (chunk.SequenceEqual("VP8X"u8)) return ("image/webp", 1 + (d[24] | d[25] << 8 | d[26] << 16), 1 + (d[27] | d[28] << 8 | d[29] << 16));
            if (chunk.SequenceEqual("VP8 "u8)) return ("image/webp", BinaryPrimitives.ReadUInt16LittleEndian(d[26..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(d[28..]) & 0x3FFF);
            if (chunk.SequenceEqual("VP8L"u8))
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(d[21..]);
                return ("image/webp", (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1);
            }
            return ("image/webp", null, null);
        }
        if (d.Length >= 4 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF)
        {
            var i = 2;
            while (i + 9 < d.Length)
            {
                if (d[i] != 0xFF) { i++; continue; }
                var marker = d[i + 1];
                var length = d[i + 2] << 8 | d[i + 3];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return ("image/jpeg", d[i + 7] << 8 | d[i + 8], d[i + 5] << 8 | d[i + 6]);
                if (length < 2) break;
                i += 2 + length;
            }
            return ("image/jpeg", null, null);
        }
        return null;
    }
}
