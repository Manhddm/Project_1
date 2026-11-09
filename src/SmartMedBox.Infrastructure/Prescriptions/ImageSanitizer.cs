using System.Buffers.Binary;
using System.Text;

namespace SmartMedBox.Infrastructure.Prescriptions;

public sealed record ImageFormat(string ContentType, string Extension);

/// <summary>
/// Nhận diện JPEG/PNG/WebP bằng magic bytes và ghi lại file chỉ giữ dữ liệu ảnh (whitelist segment/chunk),
/// bỏ mọi metadata (EXIF kể cả GPS, XMP, IPTC, comment, text...). Không giải mã điểm ảnh: chỉ kiểm tra cấu trúc
/// container — file hỏng cấu trúc trả null (IMAGE_INVALID).
/// </summary>
public static class ImageSanitizer
{
    public static readonly ImageFormat Jpeg = new("image/jpeg", "jpg");
    public static readonly ImageFormat Png = new("image/png", "png");
    public static readonly ImageFormat WebP = new("image/webp", "webp");

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Chunk PNG phụ trợ cần cho hiển thị; mọi chunk phụ trợ khác (tEXt, zTXt, iTXt, eXIf, tIME, chunk riêng...) bị bỏ.
    private static readonly HashSet<string> PngAncillaryKept =
        ["tRNS", "gAMA", "cHRM", "sRGB", "iCCP", "sBIT", "bKGD", "pHYs", "acTL", "fcTL", "fdAT"];

    // Chunk WebP chứa dữ liệu ảnh; EXIF, "XMP " và chunk lạ bị bỏ.
    private static readonly HashSet<string> WebPKept = ["VP8 ", "VP8L", "VP8X", "ALPH", "ANIM", "ANMF", "ICCP"];

    public static ImageFormat? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return Jpeg;
        }

        if (data.StartsWith(PngSignature))
        {
            return Png;
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return WebP;
        }

        return null;
    }

    /// <summary>Trả bản sao không metadata, hoặc null nếu cấu trúc file không hợp lệ.</summary>
    public static byte[]? StripMetadata(byte[] data, ImageFormat format) =>
        format == Jpeg ? StripJpeg(data)
        : format == Png ? StripPng(data)
        : format == WebP ? StripWebP(data)
        : throw new ArgumentOutOfRangeException(nameof(format));

    private static byte[]? StripJpeg(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2); // SOI
        var pos = 2;
        var sawScan = false;

        while (pos < data.Length)
        {
            if (data[pos] != 0xFF)
            {
                return null;
            }

            while (pos < data.Length && data[pos] == 0xFF)
            {
                pos++; // byte đệm 0xFF
            }

            if (pos >= data.Length)
            {
                return null;
            }

            var marker = data[pos++];
            if (marker == 0xD9) // EOI: dữ liệu nối sau EOI bị bỏ
            {
                if (!sawScan)
                {
                    return null;
                }

                output.Write([0xFF, 0xD9]);
                return output.ToArray();
            }

            if (marker is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                output.Write([0xFF, marker]);
                continue;
            }

            if (marker is 0x00 or 0xD8 || pos + 2 > data.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos));
            if (length < 2 || pos + length > data.Length)
            {
                return null;
            }

            if (KeepJpegSegment(marker, data.AsSpan(pos + 2, length - 2)))
            {
                output.Write([0xFF, marker]);
                output.Write(data, pos, length);
            }

            pos += length;
            if (marker != 0xDA) // SOS: theo sau là dữ liệu entropy tới marker kế tiếp
            {
                continue;
            }

            sawScan = true;
            var scanStart = pos;
            while (pos + 1 < data.Length && !(data[pos] == 0xFF && data[pos + 1] is not 0x00 and not (>= 0xD0 and <= 0xD7) and not 0xFF))
            {
                pos++;
            }

            if (pos + 1 >= data.Length)
            {
                return null;
            }

            output.Write(data, scanStart, pos - scanStart);
        }

        return null;
    }

    // Giữ mọi segment codec; trong APPn chỉ giữ JFIF (APP0) và Adobe (APP14, cần để giải mã đúng màu CMYK/YCCK).
    // Bỏ APP1 (EXIF/XMP), APP2 (ICC/FlashPix), APP13 (IPTC), các APPn khác và COM.
    private static bool KeepJpegSegment(byte marker, ReadOnlySpan<byte> payload) => marker switch
    {
        0xE0 => payload.StartsWith("JFIF\0"u8) || payload.StartsWith("JFXX\0"u8),
        0xEE => payload.StartsWith("Adobe"u8),
        >= 0xE1 and <= 0xEF => false,
        0xFE => false,
        _ => true,
    };

    private static byte[]? StripPng(byte[] data)
    {
        using var output = new MemoryStream(data.Length);
        output.Write(PngSignature);
        var pos = PngSignature.Length;
        var first = true;

        while (pos + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (length > int.MaxValue || pos + 12 + (long)length > data.Length)
            {
                return null;
            }

            var type = Encoding.ASCII.GetString(data, pos + 4, 4);
            if (first != (type == "IHDR"))
            {
                return null;
            }

            first = false;
            var chunkSize = 12 + (int)length;
            var isCritical = char.IsAsciiLetterUpper(type[0]);
            if (isCritical || PngAncillaryKept.Contains(type))
            {
                output.Write(data, pos, chunkSize);
            }

            pos += chunkSize;
            if (type == "IEND")
            {
                return output.ToArray();
            }
        }

        return null;
    }

    private static byte[]? StripWebP(byte[] data)
    {
        var riffEnd = 8 + (long)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        if (riffEnd > data.Length || riffEnd < 12 + 8)
        {
            return null;
        }

        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 12); // "RIFF" size "WEBP" — size được sửa ở cuối
        var pos = 12;
        var hasImage = false;

        while (pos < riffEnd)
        {
            if (pos + 8 > riffEnd)
            {
                return null;
            }

            var fourCc = Encoding.ASCII.GetString(data, pos, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(pos + 4));
            var chunkSize = 8 + (long)size + (size & 1); // chunk lẻ có 1 byte đệm
            if (pos + 8 + (long)size > riffEnd)
            {
                return null;
            }

            chunkSize = Math.Min(chunkSize, riffEnd - pos);
            if (pos == 12 && fourCc is not ("VP8 " or "VP8L" or "VP8X"))
            {
                return null;
            }

            if (WebPKept.Contains(fourCc))
            {
                var start = (int)output.Position;
                output.Write(data, pos, (int)chunkSize);
                if (fourCc == "VP8X" && size >= 1)
                {
                    // Cờ VP8X: 0x08 = EXIF, 0x04 = XMP — xóa vì chunk tương ứng đã bị bỏ.
                    var buffer = output.GetBuffer();
                    buffer[start + 8] &= unchecked((byte)~0x0C);
                }

                hasImage |= fourCc is "VP8 " or "VP8L" or "ANMF";
            }

            pos += (int)chunkSize;
        }

        if (!hasImage)
        {
            return null;
        }

        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        return result;
    }
}
