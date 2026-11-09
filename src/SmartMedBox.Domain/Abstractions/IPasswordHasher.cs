namespace SmartMedBox.Domain.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string passwordHash, string password);
}
