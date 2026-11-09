namespace SmartMedBox.Domain.Abstractions;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
