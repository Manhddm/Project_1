using Microsoft.Extensions.Options;
using SmartMedBox.Infrastructure.Storage;

namespace SmartMedBox.UnitTests.Storage;

public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "smartmedbox-storage-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_WritesFileAndReturnsStorageKey()
    {
        var storage = CreateStorage();
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        await using var source = new MemoryStream(payload);

        var storageKey = await storage.SaveAsync(source, ".PNG");

        Assert.Matches("^[a-f0-9]{32}\\.png$", storageKey);
        var path = Path.Combine(_root, storageKey);
        Assert.True(File.Exists(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));

        await using var read = await storage.OpenReadAsync(storageKey);
        Assert.NotNull(read);
        using var buffer = new MemoryStream();
        await read!.CopyToAsync(buffer);
        Assert.Equal(payload, buffer.ToArray());
    }

    [Fact]
    public async Task OpenReadAsync_UnknownKey_ReturnsNull()
    {
        var storage = CreateStorage();

        var stream = await storage.OpenReadAsync($"{new string('a', 32)}.png");

        Assert.Null(stream);
    }

    [Fact]
    public async Task DeleteAsync_ExistingKey_RemovesFileAndReturnsTrue()
    {
        var storage = CreateStorage();
        await using var source = new MemoryStream([9, 9, 9]);
        var storageKey = await storage.SaveAsync(source, "webp");

        var firstDelete = await storage.DeleteAsync(storageKey);
        var secondDelete = await storage.DeleteAsync(storageKey);

        Assert.True(firstDelete);
        Assert.False(secondDelete);
        Assert.False(File.Exists(Path.Combine(_root, storageKey)));
    }

    [Theory]
    [InlineData("../escape.png")]
    [InlineData("not-a-key.png")]
    [InlineData("0123456789abcdef0123456789abcdef.png/../x")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF.png")]
    public async Task OpenReadAsync_InvalidKey_Throws(string storageKey)
    {
        var storage = CreateStorage();

        await Assert.ThrowsAsync<ArgumentException>(() => storage.OpenReadAsync(storageKey));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../png")]
    [InlineData("ph p")]
    [InlineData("toolongextension")]
    public async Task SaveAsync_InvalidExtension_Throws(string extension)
    {
        var storage = CreateStorage();
        await using var source = new MemoryStream([1]);

        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveAsync(source, extension));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private LocalFileStorage CreateStorage() =>
        new(Options.Create(new FileStorageOptions { RootPath = _root }));
}
