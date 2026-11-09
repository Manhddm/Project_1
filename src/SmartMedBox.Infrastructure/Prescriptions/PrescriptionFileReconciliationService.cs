using Microsoft.EntityFrameworkCore;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Storage;

namespace SmartMedBox.Infrastructure.Prescriptions;

public sealed record PrescriptionFileReconciliationReport(
    int MetadataCount,
    int StorageFileCount,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<string> OrphanFiles,
    IReadOnlyList<string> InvalidStorageFiles,
    IReadOnlyList<string> DuplicateMetadataKeys)
{
    public bool IsClean => MissingFiles.Count == 0
        && OrphanFiles.Count == 0
        && InvalidStorageFiles.Count == 0
        && DuplicateMetadataKeys.Count == 0;
}

public sealed class PrescriptionFileReconciliationService(AppDbContext db, IFileStorage storage)
{
    public async Task<PrescriptionFileReconciliationReport> ReconcileAsync(
        CancellationToken cancellationToken = default)
    {
        var metadataKeys = await db.PrescriptionImages
            .AsNoTracking()
            .Select(image => image.StorageKey)
            .ToListAsync(cancellationToken);
        var storageFiles = await storage.ListKeysAsync(cancellationToken);

        var metadataSet = metadataKeys.ToHashSet(StringComparer.Ordinal);
        var storageSet = storageFiles.ToHashSet(StringComparer.Ordinal);
        var duplicateMetadataKeys = metadataKeys
            .GroupBy(static key => key, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var invalidStorageFiles = storageFiles
            .Where(static key => !IsValidStorageKey(key))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var missingFiles = metadataSet
            .Except(storageSet, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var orphanFiles = storageSet
            .Except(metadataSet, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new PrescriptionFileReconciliationReport(
            metadataKeys.Count,
            storageFiles.Count,
            missingFiles,
            orphanFiles,
            invalidStorageFiles,
            duplicateMetadataKeys);
    }

    private static bool IsValidStorageKey(string key)
    {
        if (key.Length is < 34 or > 43 || key[32] != '.')
        {
            return false;
        }

        for (var index = 0; index < 32; index++)
        {
            var character = key[index];
            if ((character < '0' || character > '9') && (character < 'a' || character > 'f'))
            {
                return false;
            }
        }

        var extension = key.AsSpan(33);
        return extension.Length is >= 1 and <= 10
            && extension.ToArray().All(static character => character is >= 'a' and <= 'z' or >= '0' and <= '9');
    }
}