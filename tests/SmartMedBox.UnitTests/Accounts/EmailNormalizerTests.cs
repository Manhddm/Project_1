using SmartMedBox.Domain.Accounts;
using SmartMedBox.Domain.Entities;

namespace SmartMedBox.UnitTests.Accounts;

public sealed class EmailNormalizerTests
{
    [Theory]
    [InlineData("  User@Example.COM ", "user@example.com")]
    [InlineData("owner@example.com", "owner@example.com")]
    [InlineData("OWNER@EXAMPLE.COM", "owner@example.com")]
    public void Normalize_ValidEmail_TrimsAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, EmailNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_Whitespace_Throws(string input)
    {
        Assert.Throws<ArgumentException>(() => EmailNormalizer.Normalize(input));
    }

    [Fact]
    public void Normalize_Null_Throws()
    {
        Assert.Throws<ArgumentException>(() => EmailNormalizer.Normalize(null!));
    }

    [Fact]
    public void Normalize_TooLong_Throws()
    {
        var email = new string('a', EmailNormalizer.MaxLength) + "@example.com";

        Assert.Throws<ArgumentException>(() => EmailNormalizer.Normalize(email));
    }

    [Fact]
    public void ApplicationUser_EmailSetter_NormalizesValue()
    {
        var user = new ApplicationUser { Email = "  Owner@Example.COM " };

        Assert.Equal("owner@example.com", user.Email);
    }
}
