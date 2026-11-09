using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Abstractions;

namespace SmartMedBox.Infrastructure.Security;

public sealed class TokenPepperOptions
{
    public const string SectionName = "Security";

    public string TokenPepper { get; set; } = string.Empty;
}

public sealed class HmacTokenHasher : ITokenHasher
{
    private readonly byte[] _pepper;

    public HmacTokenHasher(IOptions<TokenPepperOptions> options)
    {
        var pepper = options.Value.TokenPepper;
        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new InvalidOperationException(
                "Security:TokenPepper is required. Configure it with user-secrets or environment variables (see README).");
        }

        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    public string Hash(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var mac = HMACSHA256.HashData(_pepper, Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(mac);
    }
}