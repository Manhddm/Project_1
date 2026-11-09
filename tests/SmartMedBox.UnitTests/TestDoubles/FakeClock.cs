using SmartMedBox.Domain.Abstractions;

namespace SmartMedBox.UnitTests.TestDoubles;

public sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = utcNow;
}
