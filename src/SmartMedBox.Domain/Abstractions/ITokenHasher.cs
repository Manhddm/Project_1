namespace SmartMedBox.Domain.Abstractions;

public interface ITokenHasher
{
    string Hash(string token);
}