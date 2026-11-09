using System.Text.Json;
using SmartMedBox.Domain.Devices;

namespace SmartMedBox.UnitTests.Devices;

public sealed class PairingCodeTests
{
    [Fact]
    public void Create_ReturnsEightCrockfordCharacters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = PairingCode.Create();
            Assert.Equal(8, code.Length);
            Assert.All(code, c => Assert.Contains(c, PairingCode.Alphabet));
        }
    }

    [Fact]
    public void Create_ExcludesAmbiguousLetters()
    {
        Assert.Equal(32, PairingCode.Alphabet.Distinct().Count());
        Assert.DoesNotContain('I', PairingCode.Alphabet);
        Assert.DoesNotContain('L', PairingCode.Alphabet);
        Assert.DoesNotContain('O', PairingCode.Alphabet);
        Assert.DoesNotContain('U', PairingCode.Alphabet);
    }

    [Fact]
    public void Lifetime_IsFifteenMinutes() => Assert.Equal(TimeSpan.FromMinutes(15), PairingCode.Lifetime);

    [Theory]
    [InlineData("7F3K9Q2M", "7F3K9Q2M")]
    [InlineData("7f3k-9q2m", "7F3K9Q2M")]
    [InlineData("  7F3K 9Q2M ", "7F3K9Q2M")]
    [InlineData("iLoO-1100", "11001100")]
    public void TryNormalize_ValidInput_ReturnsCanonicalCode(string input, string expected)
    {
        Assert.True(PairingCode.TryNormalize(input, out var code));
        Assert.Equal(expected, code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7F3K9Q2")]
    [InlineData("7F3K9Q2MX")]
    [InlineData("7F3K9Q2U")]
    [InlineData("7F3K9Q2*")]
    public void TryNormalize_InvalidInput_ReturnsFalse(string? input)
    {
        Assert.False(PairingCode.TryNormalize(input, out var code));
        Assert.Equal(string.Empty, code);
    }

    [Fact]
    public void FormatForLabel_SplitsIntoTwoGroups() => Assert.Equal("7F3K-9Q2M", PairingCode.FormatForLabel("7F3K9Q2M"));

    [Fact]
    public void QrPayload_ContainsOnlyVersionAndCode()
    {
        var payload = PairingCode.QrPayload("7F3K9Q2M");

        Assert.Equal("""{"v":1,"code":"7F3K9Q2M"}""", payload);
        using var json = JsonDocument.Parse(payload);
        Assert.Equal(2, json.RootElement.EnumerateObject().Count());
    }
}
