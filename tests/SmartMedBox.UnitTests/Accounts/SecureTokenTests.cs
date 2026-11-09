using System.Text.RegularExpressions;
using SmartMedBox.Domain.Accounts;

namespace SmartMedBox.UnitTests.Accounts;

public sealed partial class SecureTokenTests
{
    [Fact]
    public void Create_ReturnsBase64UrlTokenOfExpectedLength()
    {
        var token = SecureToken.Create();

        Assert.Equal(43, token.Length);
        Assert.Matches(Base64UrlPattern(), token);
    }

    [Fact]
    public void Create_CalledTwice_ReturnsDifferentTokens()
    {
        Assert.NotEqual(SecureToken.Create(), SecureToken.Create());
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$")]
    private static partial Regex Base64UrlPattern();
}