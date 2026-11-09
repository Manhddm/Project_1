using System.Security.Cryptography;
using System.Text.Json;

namespace SmartMedBox.Domain.Devices;

/// <summary>Pairing code theo D-08: 8 ký tự Crockford Base32 (40 bit), TTL 15 phút, dùng một lần.</summary>
public static class PairingCode
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const int Length = 8;
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);

    public static string Create() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>Chuẩn hóa mã người dùng nhập/quét: bỏ khoảng trắng và '-', không phân biệt hoa thường, I/L→1, O→0.</summary>
    public static bool TryNormalize(string? input, out string code)
    {
        code = string.Empty;
        if (input is null)
        {
            return false;
        }

        var chars = new List<char>(Length);
        foreach (var raw in input)
        {
            if (raw == '-' || char.IsWhiteSpace(raw))
            {
                continue;
            }

            var c = char.ToUpperInvariant(raw) switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                var other => other,
            };

            if (!Alphabet.Contains(c) || chars.Count == Length)
            {
                return false;
            }

            chars.Add(c);
        }

        if (chars.Count != Length)
        {
            return false;
        }

        code = new string(chars.ToArray());
        return true;
    }

    /// <summary>Dạng hiển thị trên nhãn: XXXX-XXXX.</summary>
    public static string FormatForLabel(string code) => $"{code[..4]}-{code[4..]}";

    /// <summary>Payload QR theo D-08: chỉ có phiên bản và mã, không có khóa/serial/URL.</summary>
    public static string QrPayload(string code) => JsonSerializer.Serialize(new { v = 1, code });
}
