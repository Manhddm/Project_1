using System.Buffers.Binary;
using System.Text;
using SmartMedBox.Infrastructure.Prescriptions;

namespace SmartMedBox.UnitTests.Prescriptions;

public sealed class ImageSanitizerTests
{
    private const string Secret = "GPS 21.0285N 105.8542E";

    [Fact]
    public void Detect_ByMagicBytes_IgnoresExtension()
    {
        Assert.Same(ImageSanitizer.Jpeg, ImageSanitizer.Detect(TestImages.Jpeg()));
        Assert.Same(ImageSanitizer.Png, ImageSanitizer.Detect(TestImages.Png()));
        Assert.Same(ImageSanitizer.WebP, ImageSanitizer.Detect(TestImages.WebP()));
        Assert.Null(ImageSanitizer.Detect("MZ\x90\0\x03\0\0\0"u8)); // .exe
        Assert.Null(ImageSanitizer.Detect("GIF89a"u8));
        Assert.Null(ImageSanitizer.Detect("RIFF\0\0\0\0WAVE"u8));
        Assert.Null(ImageSanitizer.Detect([0xFF, 0xD8]));
        Assert.Null(ImageSanitizer.Detect([]));
    }

    [Fact]
    public void StripMetadata_Jpeg_RemovesExifCommentAndTrailingData()
    {
        var clean = ImageSanitizer.StripMetadata(TestImages.Jpeg(), ImageSanitizer.Jpeg);

        Assert.NotNull(clean);
        Assert.Equal([0xFF, 0xD8], clean[..2]);
        Assert.Equal([0xFF, 0xD9], clean[^2..]);
        AssertAbsent(clean, "Exif", "GPS", "comment", "http://ns.adobe.com", "TRAILER");
        AssertPresent(clean, "JFIF");
        // Dữ liệu entropy (gồm byte stuffing FF 00 và RST) giữ nguyên.
        Assert.True(clean.AsSpan().IndexOf(TestImages.JpegScanData) >= 0);
        Assert.Same(ImageSanitizer.Jpeg, ImageSanitizer.Detect(clean));
    }

    [Fact]
    public void StripMetadata_Jpeg_TruncatedOrWithoutScan_IsInvalid()
    {
        var jpeg = TestImages.Jpeg();
        Assert.Null(ImageSanitizer.StripMetadata(jpeg[..40], ImageSanitizer.Jpeg));
        Assert.Null(ImageSanitizer.StripMetadata([0xFF, 0xD8, 0xFF, 0xD9], ImageSanitizer.Jpeg));
        Assert.Null(ImageSanitizer.StripMetadata([0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0xFF], ImageSanitizer.Jpeg));
    }

    [Fact]
    public void StripMetadata_Png_KeepsOnlyImageChunks()
    {
        var clean = ImageSanitizer.StripMetadata(TestImages.Png(), ImageSanitizer.Png);

        Assert.NotNull(clean);
        Assert.Equal(["IHDR", "pHYs", "IDAT", "IEND"], TestImages.PngChunkTypes(clean));
        AssertAbsent(clean, "GPS", "Exif", "tEXt", "zTXt", "iTXt", "eXIf", "tIME", "prVt");
    }

    [Fact]
    public void StripMetadata_Png_BadStructure_IsInvalid()
    {
        var png = TestImages.Png();
        Assert.Null(ImageSanitizer.StripMetadata(png[..^12], ImageSanitizer.Png)); // thiếu IEND
        Assert.Null(ImageSanitizer.StripMetadata(png[..30], ImageSanitizer.Png)); // chunk bị cắt
        Assert.Null(ImageSanitizer.StripMetadata(
            TestImages.PngFrom(TestImages.PngChunk("IDAT", [1]), TestImages.PngChunk("IEND", [])), ImageSanitizer.Png));
    }

    [Fact]
    public void StripMetadata_WebP_RemovesExifXmpAndFixesHeader()
    {
        var clean = ImageSanitizer.StripMetadata(TestImages.WebP(), ImageSanitizer.WebP);

        Assert.NotNull(clean);
        AssertAbsent(clean, "EXIF", "XMP ", "GPS");
        Assert.Equal(clean.Length - 8, (int)BinaryPrimitives.ReadUInt32LittleEndian(clean.AsSpan(4)));
        Assert.Equal("VP8X", Encoding.ASCII.GetString(clean, 12, 4));
        Assert.Equal(0x10, clean[20]); // chỉ còn cờ alpha
        AssertPresent(clean, "VP8L", "ALPH");
        Assert.Equal(12 + 18 + 8 + 4 + 8 + 5 + 1, clean.Length); // VP8X + ALPH + VP8L (lẻ, có byte đệm)
    }

    [Fact]
    public void StripMetadata_WebP_BadStructure_IsInvalid()
    {
        var webp = TestImages.WebP();
        Assert.Null(ImageSanitizer.StripMetadata(webp[..20], ImageSanitizer.WebP));
        Assert.Null(ImageSanitizer.StripMetadata(
            TestImages.WebPFrom(TestImages.RiffChunk("EXIF", Encoding.ASCII.GetBytes(Secret))), ImageSanitizer.WebP));
    }

    private static void AssertAbsent(byte[] data, params string[] needles)
    {
        foreach (var needle in needles)
        {
            Assert.True(data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) < 0, $"'{needle}' vẫn còn trong file");
        }
    }

    private static void AssertPresent(byte[] data, params string[] needles)
    {
        foreach (var needle in needles)
        {
            Assert.True(data.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0, $"'{needle}' bị mất");
        }
    }
}

/// <summary>Ảnh tối giản dựng tay, có metadata EXIF/GPS để kiểm tra việc loại bỏ (dùng chung với integration test).</summary>
public static class TestImages
{
    public const string Gps = "GPS 21.0285N 105.8542E";

    public static readonly byte[] JpegScanData = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56];

    public static byte[] Jpeg()
    {
        using var ms = new MemoryStream();
        ms.Write([0xFF, 0xD8]);
        Segment(ms, 0xE0, [.. "JFIF\0"u8, 1, 1, 0, 0, 1, 0, 1, 0, 0]);
        Segment(ms, 0xE1, [.. "Exif\0\0"u8, .. Encoding.ASCII.GetBytes(Gps)]);
        Segment(ms, 0xE1, Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0<x>" + Gps + "</x>"));
        Segment(ms, 0xFE, "comment: patient"u8.ToArray());
        Segment(ms, 0xDB, new byte[65]); // DQT
        Segment(ms, 0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]); // SOF0 1x1
        Segment(ms, 0xC4, new byte[20]); // DHT
        Segment(ms, 0xDA, [1, 1, 0, 0, 0x3F, 0]); // SOS
        ms.Write(JpegScanData);
        Segment(ms, 0xED, [.. "Photoshop 3.0\0"u8, .. Encoding.ASCII.GetBytes(Gps)]); // APP13 sau scan
        ms.Write([0xFF, 0xD9]);
        ms.Write("TRAILER"u8);
        return ms.ToArray();
    }

    public static byte[] Png() => PngFrom(
        PngChunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 2, 0, 0, 0]),
        PngChunk("tEXt", [.. "Comment\0"u8, .. Encoding.ASCII.GetBytes(Gps)]),
        PngChunk("eXIf", [.. "MM\0*"u8, .. Encoding.ASCII.GetBytes(Gps)]),
        PngChunk("pHYs", [0, 0, 0x0B, 0x13, 0, 0, 0x0B, 0x13, 1]),
        PngChunk("iTXt", [.. "XML:com.adobe.xmp\0\0\0\0\0"u8, .. Encoding.ASCII.GetBytes(Gps)]),
        PngChunk("zTXt", [.. "Raw profile type exif\0\0"u8, 1, 2]),
        PngChunk("prVt", Encoding.ASCII.GetBytes(Gps)),
        PngChunk("IDAT", [0x78, 0x9C, 0x63, 0xF8, 0x0F, 0x00, 0x01, 0x01, 0x01, 0x00]),
        PngChunk("tIME", [7, 0xEA, 10, 29, 9, 0, 0]),
        PngChunk("IEND", []));

    public static byte[] PngFrom(params byte[][] chunks) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. chunks.SelectMany(x => x)];

    // CRC không được kiểm tra bởi sanitizer nên để 0.
    public static byte[] PngChunk(string type, byte[] data)
    {
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    public static List<string> PngChunkTypes(byte[] png)
    {
        var types = new List<string>();
        for (var pos = 8; pos + 12 <= png.Length; pos += 12 + (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(pos)))
        {
            types.Add(Encoding.ASCII.GetString(png, pos + 4, 4));
        }

        return types;
    }

    public static byte[] WebP() => WebPFrom(
        RiffChunk("VP8X", [0x10 | 0x08 | 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0]),
        RiffChunk("ALPH", [0, 0, 0, 0]),
        RiffChunk("VP8L", [0x2F, 0, 0, 0, 0]),
        RiffChunk("EXIF", [.. "MM\0*"u8, .. Encoding.ASCII.GetBytes(Gps)]),
        RiffChunk("XMP ", Encoding.ASCII.GetBytes("<x>" + Gps + "</x>")));

    public static byte[] WebPFrom(params byte[][] chunks)
    {
        byte[] body = [.. "WEBP"u8, .. chunks.SelectMany(x => x)];
        var file = new byte[8 + body.Length];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)body.Length);
        body.CopyTo(file, 8);
        return file;
    }

    public static byte[] RiffChunk(string fourCc, byte[] data)
    {
        var chunk = new byte[8 + data.Length + (data.Length & 1)];
        Encoding.ASCII.GetBytes(fourCc).CopyTo(chunk, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(4), (uint)data.Length);
        data.CopyTo(chunk, 8);
        return chunk;
    }

    private static void Segment(Stream stream, byte marker, byte[] payload)
    {
        stream.Write([0xFF, marker]);
        Span<byte> length = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(payload.Length + 2));
        stream.Write(length);
        stream.Write(payload);
    }
}
