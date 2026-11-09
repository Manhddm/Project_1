using SmartMedBox.Infrastructure.Time;

namespace SmartMedBox.UnitTests.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentUtcTimestamp()
    {
        var before = DateTimeOffset.UtcNow;

        var now = new SystemClock().UtcNow;

        var after = DateTimeOffset.UtcNow;
        Assert.InRange(now, before, after);
        Assert.Equal(TimeSpan.Zero, now.Offset);
    }
}
