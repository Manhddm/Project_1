using SmartMedBox.Domain.Accounts;

namespace SmartMedBox.UnitTests.Accounts;

public sealed class PasswordPolicyTests
{
    [Theory]
    [InlineData("Passw0rd")]
    [InlineData("a1234567")]
    [InlineData("ABCDEFG1")]
    [InlineData("matkhau1")]
    public void IsValid_PasswordWithLettersAndDigitsWithinLength_ReturnsTrue(string password)
    {
        Assert.True(PasswordPolicy.IsValid(password));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Pass1")]              // ngắn hơn 8
    [InlineData("abcdefgh")]           // không có số
    [InlineData("12345678")]           // không có chữ
    [InlineData("       1")]           // chỉ khoảng trắng + số
    public void IsValid_WeakPassword_ReturnsFalse(string? password)
    {
        Assert.False(PasswordPolicy.IsValid(password));
    }

    [Fact]
    public void IsValid_LengthOver128_ReturnsFalse()
    {
        var password = "a1" + new string('x', 127);

        Assert.False(PasswordPolicy.IsValid(password));
    }

    [Fact]
    public void IsValid_LengthExactly128_ReturnsTrue()
    {
        var password = "a1" + new string('x', 126);

        Assert.True(PasswordPolicy.IsValid(password));
    }
}