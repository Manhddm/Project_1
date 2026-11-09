using Microsoft.Extensions.Options;
using SmartMedBox.Infrastructure.Security;

namespace SmartMedBox.UnitTests.Security;

public sealed class HmacTokenHasherTests
{
    private const string Token = "abcDEF123_-token";

    [Fact]
    public void Hash_SameTokenAndPepper_IsDeterministic()
    {
        var hasher = CreateHasher("pepper-one-0123456789abcdef0123456789");

        Assert.Equal(hasher.Hash(Token), hasher.Hash(Token));
    }

    [Fact]
    public void Hash_DifferentTokens_ProduceDifferentHashes()
    {
        var hasher = CreateHasher("pepper-one-0123456789abcdef0123456789");

        Assert.NotEqual(hasher.Hash(Token), hasher.Hash(Token + "x"));
    }

    [Fact]
    public void Hash_DifferentPeppers_ProduceDifferentHashes()
    {
        var first = CreateHasher("pepper-one-0123456789abcdef0123456789");
        var second = CreateHasher("pepper-two-0123456789abcdef0123456789");

        Assert.NotEqual(first.Hash(Token), second.Hash(Token));
    }

    [Fact]
    public void Hash_DoesNotContainPlainToken()
    {
        var hash = CreateHasher("pepper-one-0123456789abcdef0123456789").Hash(Token);

        Assert.DoesNotContain(Token, hash);
        Assert.Equal(44, hash.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Hash_BlankToken_Throws(string token)
    {
        var hasher = CreateHasher("pepper-one-0123456789abcdef0123456789");

        Assert.ThrowsAny<ArgumentException>(() => hasher.Hash(token));
    }

    [Fact]
    public void Constructor_MissingPepper_Throws()
    {
        var options = Options.Create(new TokenPepperOptions { TokenPepper = "" });

        Assert.Throws<InvalidOperationException>(() => new HmacTokenHasher(options));
    }

    private static HmacTokenHasher CreateHasher(string pepper) =>
        new(Options.Create(new TokenPepperOptions { TokenPepper = pepper }));
}