using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Prescriptions;
using SmartMedBox.Infrastructure.Storage;
using SmartMedBox.IntegrationTests.Schema;
using SmartMedBox.IntegrationTests.TestSupport;

namespace SmartMedBox.IntegrationTests.Prescriptions;

public sealed class PrescriptionFileReconciliationTests(SqlServerDatabaseFixture fixture) : IClassFixture<SqlServerDatabaseFixture>, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "smartmedbox-reconciliation", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ReconcileAsync_ReportsMissingOrphanInvalidAndDuplicateFiles_WithoutMutatingData()
    {
        var owner = await TestUsers.SeedAsync(fixture);
        var referencedKey = NewKey("jpg");
        var orphanKey = NewKey("png");
        var missingKey = NewKey("webp");
        var invalidKey = "unexpected-name.bin";

        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(Path.Combine(_root, referencedKey), [1]);
        await File.WriteAllBytesAsync(Path.Combine(_root, orphanKey), [2]);
        await File.WriteAllBytesAsync(Path.Combine(_root, invalidKey), [3]);

        await using (var db = fixture.CreateContext())
        {
            var device = await new DeviceProvisioningService(db).ProvisionAsync(
                $"esp32-{Guid.NewGuid():N}",
                $"SN-{Guid.NewGuid():N}",
                Guid.NewGuid().ToString("N"));
            device.Device.OwnerUserId = owner.Id;
            db.PrescriptionImages.AddRange(
                new PrescriptionImage
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = owner.Id,
                    StorageKey = referencedKey,
                    OriginalFileName = "ok.jpg",
                    ContentType = "image/jpeg",
                    SizeBytes = 1,
                    UploadedAt = DateTimeOffset.UtcNow,
                },
                new PrescriptionImage
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = owner.Id,
                    StorageKey = missingKey,
                    OriginalFileName = "missing.webp",
                    ContentType = "image/webp",
                    SizeBytes = 1,
                    UploadedAt = DateTimeOffset.UtcNow,
                },
                new PrescriptionImage
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = owner.Id,
                    StorageKey = referencedKey,
                    OriginalFileName = "duplicate.jpg",
                    ContentType = "image/jpeg",
                    SizeBytes = 1,
                    UploadedAt = DateTimeOffset.UtcNow,
                });
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            var service = new PrescriptionFileReconciliationService(
                db,
                new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = _root })));

            var report = await service.ReconcileAsync();

            Assert.Equal(3, report.MetadataCount);
            Assert.Equal(3, report.StorageFileCount);
            Assert.Equal(new[] { missingKey }, report.MissingFiles);
            Assert.Equal(new[] { orphanKey, invalidKey }.Order(StringComparer.Ordinal), report.OrphanFiles);
            Assert.Equal(new[] { invalidKey }, report.InvalidStorageFiles);
            Assert.Equal(new[] { referencedKey }, report.DuplicateMetadataKeys);
            Assert.False(report.IsClean);
        }

        Assert.True(File.Exists(Path.Combine(_root, referencedKey)));
        Assert.True(File.Exists(Path.Combine(_root, orphanKey)));
        Assert.True(File.Exists(Path.Combine(_root, invalidKey)));
        await using var verify = fixture.CreateContext();
        Assert.Equal(3, await verify.PrescriptionImages.CountAsync());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string NewKey(string extension) => $"{Guid.NewGuid():N}.{extension}";
}
