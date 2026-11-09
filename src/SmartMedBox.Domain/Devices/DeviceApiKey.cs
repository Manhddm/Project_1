using System.Security.Cryptography;
using System.Text;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Accounts;

namespace SmartMedBox.Domain.Devices;

/// <summary>DeviceApiKey theo D-07: 32 byte CSPRNG, base64url không padding (43 ký tự); server chỉ lưu HMAC.</summary>
public static class DeviceApiKey
{
    public const int EncodedLength = 43;
    public const int MaxDeviceIdLength = 100;

    public static string Create() => SecureToken.Create();

    public static bool IsWellFormedKey(string? key) =>
        key is { Length: EncodedLength } && key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static bool IsWellFormedDeviceId(string? deviceId) =>
        deviceId is { Length: > 0 and <= MaxDeviceIdLength }
        && deviceId.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>So sánh constant-time HMAC của khóa thô với hash đã lưu.</summary>
    public static bool Matches(ITokenHasher hasher, string key, string storedHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(hasher.Hash(key)),
            Encoding.UTF8.GetBytes(storedHash));
}
