using SmartMedBox.Infrastructure.Identity;

namespace SmartMedBox.UnitTests.Identity;

public sealed class PasswordHasherServiceTests
{
    private const string Password = "P@ssw0rd!2026";
    private readonly PasswordHasherService _hasher = new();

    [Fact]
    public void Hash_ValidPassword_ReturnsSaltedHashWithoutPlaintext()
    {
        var hash = _hasher.Hash(Password);

        Assert.NotEqual(Password, hash);
        Assert.DoesNotContain(Password, hash);
        Assert.True(hash.Length > 40);
    }

    [Fact]
    public void Hash_SamePassword_ProducesDifferentHashes()
    {
        Assert.NotEqual(_hasher.Hash(Password), _hasher.Hash(Password));
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        Assert.True(_hasher.Verify(_hasher.Hash(Password), Password));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        Assert.False(_hasher.Verify(_hasher.Hash(Password), "wrong-password"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-valid-identity-hash")]
    public void Verify_InvalidHash_ReturnsFalse(string invalidHash)
    {
        Assert.False(_hasher.Verify(invalidHash, Password));
    }

    [Fact]
    public void Verify_EmptyPassword_ReturnsFalse()
    {
        Assert.False(_hasher.Verify(_hasher.Hash(Password), ""));
    }

    [Fact]
    public void Hash_EmptyPassword_Throws()
    {
        Assert.Throws<ArgumentException>(() => _hasher.Hash(""));
    }
}
