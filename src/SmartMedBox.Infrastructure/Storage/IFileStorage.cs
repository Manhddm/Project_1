namespace SmartMedBox.Infrastructure.Storage;

public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListKeysAsync(CancellationToken cancellationToken = default);
}
