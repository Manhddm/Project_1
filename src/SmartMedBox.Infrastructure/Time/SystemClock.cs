using SmartMedBox.Domain.Abstractions;

namespace SmartMedBox.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
