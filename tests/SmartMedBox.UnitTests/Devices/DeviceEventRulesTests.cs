using SmartMedBox.Domain.Devices;
using SmartMedBox.Domain.Enums;

namespace SmartMedBox.UnitTests.Devices;

public sealed class DeviceEventRulesTests
{
    [Theory]
    [InlineData("018f2a7c-9d1e-7c3a-b1b2-3c4d5e6f7a8b", true)]
    [InlineData("e", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsValidEventId_Input_ReturnsExpected(string? eventId, bool expected) =>
        Assert.Equal(expected, DeviceEventRules.IsValidEventId(eventId));

    [Fact]
    public void IsValidEventId_LengthBoundary_AcceptsUpTo100()
    {
        Assert.True(DeviceEventRules.IsValidEventId(new string('a', 100)));
        Assert.False(DeviceEventRules.IsValidEventId(new string('a', 101)));
    }

    [Theory]
    [InlineData("LidOpened", true)]
    [InlineData("LidClosed", true)]
    [InlineData("lidopened", false)]
    [InlineData("0", false)]
    [InlineData("LidTampered", false)]
    [InlineData(null, false)]
    public void TryParseEventType_Input_OnlyExactContractNames(string? value, bool expected)
    {
        Assert.Equal(expected, DeviceEventRules.TryParseEventType(value, out var parsed));
        if (expected)
        {
            Assert.Equal(value, parsed.ToString());
        }
    }
}
