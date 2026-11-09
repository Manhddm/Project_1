using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Security;

namespace SmartMedBox.UnitTests.Devices;

public sealed class DeviceApiKeyTests
{
    private static readonly HmacTokenHasher Hasher =
        new(Options.Create(new TokenPepperOptions { TokenPepper = "unit-test-pepper-0123456789abcdef0123456789" }));

    [Fact]
    public void Create_Returns43CharBase64UrlWithoutPadding()
    {
        var key = DeviceApiKey.Create();

        Assert.Equal(43, key.Length);
        Assert.True(DeviceApiKey.IsWellFormedKey(key));
        Assert.DoesNotContain('=', key);
        Assert.NotEqual(key, DeviceApiKey.Create());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+")]
    public void IsWellFormedKey_InvalidKey_ReturnsFalse(string? key) => Assert.False(DeviceApiKey.IsWellFormedKey(key));

    [Theory]
    [InlineData("SMB-D7K2M9QX", true)]
    [InlineData("dev_1", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("bad id", false)]
    [InlineData("thiết-bị", false)]
    public void IsWellFormedDeviceId_ReturnsExpected(string? deviceId, bool expected) =>
        Assert.Equal(expected, DeviceApiKey.IsWellFormedDeviceId(deviceId));

    [Fact]
    public void IsWellFormedDeviceId_TooLong_ReturnsFalse() =>
        Assert.False(DeviceApiKey.IsWellFormedDeviceId(new string('a', 101)));

    [Fact]
    public void Matches_CorrectKey_ReturnsTrue()
    {
        var key = DeviceApiKey.Create();

        Assert.True(DeviceApiKey.Matches(Hasher, key, Hasher.Hash(key)));
    }

    [Fact]
    public void Matches_WrongKeyOrHash_ReturnsFalse()
    {
        var key = DeviceApiKey.Create();
        var stored = Hasher.Hash(key);

        Assert.False(DeviceApiKey.Matches(Hasher, DeviceApiKey.Create(), stored));
        Assert.False(DeviceApiKey.Matches(Hasher, key, key));
        Assert.False(DeviceApiKey.Matches(Hasher, key, string.Empty));
    }
}
