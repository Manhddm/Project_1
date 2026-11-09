using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace SmartMedBox.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private static readonly Regex StorageKeyPattern = new(
        "^[a-f0-9]{32}\\.[a-z0-9]{1,10}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExtensionPattern = new(
        "^[a-z0-9]{1,10}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _rootPath;

    public LocalFileStorage(IOptions<FileStorageOptions> options)
    {
        _rootPath = Path.GetFullPath(options.Value.RootPath);
    }

    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var normalizedExtension = NormalizeExtension(extension);
        var storageKey = $"{Guid.NewGuid():N}.{normalizedExtension}";
        var path = Resolve(storageKey);

        Directory.CreateDirectory(_rootPath);

        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, cancellationToken);

        return storageKey;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task<bool> DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        File.Delete(path);
        return Task.FromResult(true);
    }

    private string Resolve(string storageKey)
    {
        if (storageKey is null || !StorageKeyPattern.IsMatch(storageKey))
        {
            throw new ArgumentException("Storage key không hợp lệ.", nameof(storageKey));
        }

        return Path.Combine(_rootPath, storageKey);
    }

    private static string NormalizeExtension(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        var normalized = extension.TrimStart('.').ToLowerInvariant();
        if (!ExtensionPattern.IsMatch(normalized))
        {
            throw new ArgumentException("Phần mở rộng tệp không hợp lệ.", nameof(extension));
        }

        return normalized;
    }
}
