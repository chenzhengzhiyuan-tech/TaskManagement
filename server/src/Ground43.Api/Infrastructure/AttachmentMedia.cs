using System.Buffers.Binary;
using System.Text;

namespace Ground43.Api.Infrastructure;

public static class AttachmentMedia
{
    public static string? Extension(string type) => type.ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg", "image/png" => ".png", "image/gif" => ".gif", "image/webp" => ".webp",
        "video/mp4" => ".mp4", "video/webm" => ".webm", _ => null
    };
    public static long Limit(string type, long configuredLimit) => Math.Min(configuredLimit,
        (type.ToLowerInvariant() switch { "video/mp4" or "video/webm" => 100L, "image/gif" => 20L, _ => 500L }) * 1024 * 1024);
    public static bool MatchesName(string name, string type)
    {
        var extension = Path.GetExtension(name).ToLowerInvariant();
        return extension == Extension(type) || (type.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) && extension == ".jpeg");
    }
    public static bool HasValidSignature(string path, string type)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(stream.Length, 4096)];
        stream.ReadExactly(buffer);
        ReadOnlySpan<byte> h = buffer;
        return type.ToLowerInvariant() switch
        {
            "image/jpeg" => h.Length >= 3 && h[0] == 255 && h[1] == 216 && h[2] == 255,
            "image/png" => h.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
            "image/gif" => h.Length >= 6 && Encoding.ASCII.GetString(h[..6]) is "GIF87a" or "GIF89a",
            "image/webp" => h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h[8..12].SequenceEqual("WEBP"u8),
            "video/mp4" => ValidMp4(h, stream.Length),
            "video/webm" => ValidWebm(h),
            _ => false
        };
    }
    private static bool ValidMp4(ReadOnlySpan<byte> h, long fileSize)
    {
        if (h.Length < 16 || !h[4..8].SequenceEqual("ftyp"u8)) return false;
        var boxSize = BinaryPrimitives.ReadUInt32BigEndian(h[..4]);
        if (boxSize < 16 || boxSize > fileSize || (boxSize - 16) % 4 != 0) return false;
        // Accept MP4-compatible brands; QuickTime-only MOV containers are not supported.
        for (var i = 8; i + 4 <= Math.Min((long)h.Length, boxSize); i += 4)
        {
            if (i == 12) continue; // Minor version, not a brand.
            if (Encoding.ASCII.GetString(h.Slice(i, 4)) is "isom" or "iso2" or "iso3" or "iso4" or "iso5" or "iso6" or "mp41" or "mp42" or "avc1" or "M4V ") return true;
        }
        return false;
    }
    private static bool ValidWebm(ReadOnlySpan<byte> h)
    {
        if (!h.StartsWith(new byte[] { 0x1A, 0x45, 0xDF, 0xA3 })) return false;
        var pos = 4;
        if (!ReadSize(h, ref pos, out var size) || size > h.Length - pos) return false;
        var end = pos + (int)size;
        while (pos < end)
        {
            var first = h[pos]; var width = 1; var mask = 0x80;
            while (width <= 4 && (first & mask) == 0) { width++; mask >>= 1; }
            if (width > 4 || pos + width > end) return false;
            var docType = width == 2 && h[pos] == 0x42 && h[pos + 1] == 0x82;
            pos += width;
            if (!ReadSize(h[..end], ref pos, out var length) || length > end - pos) return false;
            if (docType) return h.Slice(pos, (int)length).SequenceEqual("webm"u8);
            pos += (int)length;
        }
        return false;
    }
    private static bool ReadSize(ReadOnlySpan<byte> h, ref int pos, out long size)
    {
        size = 0;
        if (pos >= h.Length || h[pos] == 0) return false;
        var mask = 0x80; var width = 1;
        while ((h[pos] & mask) == 0) { width++; mask >>= 1; }
        if (pos + width > h.Length) return false;
        size = h[pos++] & (mask - 1);
        for (var i = 1; i < width; i++) size = (size << 8) | h[pos++];
        return true;
    }
}
