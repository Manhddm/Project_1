using Microsoft.EntityFrameworkCore;
using SmartMedBox.Domain.Abstractions;
using SmartMedBox.Domain.Entities;
using SmartMedBox.Infrastructure.Auditing;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Storage;

namespace SmartMedBox.Infrastructure.Prescriptions;

public enum PrescriptionUploadOutcome
{
    Ok,
    Forbidden,
    TooLarge,
    TypeNotAllowed,
    Invalid,
}

public enum PrescriptionImageAccessOutcome
{
    Ok,
    Forbidden,
    NotFound,
}

public sealed record PrescriptionImageView(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    int SizeBytes,
    DateTimeOffset UploadedAt);

public sealed record PrescriptionImageContent(Stream Content, string ContentType, string OriginalFileName);

/// <summary>
/// Ảnh đơn thuốc (P6-*): CSDL chỉ giữ metadata + storage key, nội dung nằm trong <see cref="IFileStorage"/> private
/// (local disk theo [ĐỀ XUẤT]; D-04 vẫn TBD). Chỉ chủ hộp được thao tác; người thân/người ngoài bị từ chối.
/// Không OCR, không chia sẻ, không URL công khai. Ảnh trùng/giới hạn số lượng là [TBD-D16]: chưa xử lý.
/// </summary>
public sealed class PrescriptionImageService(AppDbContext db, IFileStorage storage, IClock clock)
{
    public const int MaxBytes = 5 * 1024 * 1024;

    /// <summary>Hộp mà người dùng sở hữu; null nếu không phải chủ hộp nào (người thân hoặc người ngoài).</summary>
    public Task<Guid?> GetOwnedDeviceIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.Devices
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId)
            .Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<(PrescriptionImageAccessOutcome Outcome, IReadOnlyList<PrescriptionImageView>? Images)> ListAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (await GetOwnedDeviceIdAsync(userId, cancellationToken) is null)
        {
            return (PrescriptionImageAccessOutcome.Forbidden, null);
        }

        var images = await db.PrescriptionImages
            .AsNoTracking()
            .Where(x => x.OwnerUserId == userId && !x.IsDeleted)
            .OrderByDescending(x => x.UploadedAt)
            .Select(x => new PrescriptionImageView(
                x.Id,
                x.OriginalFileName,
                x.ContentType,
                x.SizeBytes,
                x.UploadedAt))
            .ToListAsync(cancellationToken);

        return (PrescriptionImageAccessOutcome.Ok, images);
    }

    public async Task<(PrescriptionImageAccessOutcome Outcome, PrescriptionImageContent? Content)> OpenContentAsync(
        Guid userId,
        Guid imageId,
        CancellationToken cancellationToken = default)
    {
        if (await GetOwnedDeviceIdAsync(userId, cancellationToken) is null)
        {
            return (PrescriptionImageAccessOutcome.Forbidden, null);
        }

        var image = await db.PrescriptionImages
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == imageId && x.OwnerUserId == userId && !x.IsDeleted, cancellationToken);
        if (image is null)
        {
            return (PrescriptionImageAccessOutcome.NotFound, null);
        }

        var content = await storage.OpenReadAsync(image.StorageKey, cancellationToken);
        if (content is null)
        {
            return (PrescriptionImageAccessOutcome.NotFound, null);
        }

        return (PrescriptionImageAccessOutcome.Ok,
            new PrescriptionImageContent(content, image.ContentType, image.OriginalFileName));
    }

    public async Task<(PrescriptionUploadOutcome Outcome, PrescriptionImageView? Image)> UploadAsync(
        Guid userId,
        string? originalFileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var deviceId = await GetOwnedDeviceIdAsync(userId, cancellationToken);
        if (deviceId is null)
        {
            return (PrescriptionUploadOutcome.Forbidden, null);
        }

        // Đọc tối đa MaxBytes + 1 để phát hiện vượt ngưỡng mà không buffer phần thừa.
        var buffer = new byte[MaxBytes + 1];
        var read = await content.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        if (read > MaxBytes)
        {
            return (PrescriptionUploadOutcome.TooLarge, null);
        }

        var data = buffer.AsSpan(0, read).ToArray();
        var format = ImageSanitizer.Detect(data);
        if (format is null)
        {
            return (PrescriptionUploadOutcome.TypeNotAllowed, null);
        }

        var clean = ImageSanitizer.StripMetadata(data, format);
        if (clean is null)
        {
            return (PrescriptionUploadOutcome.Invalid, null);
        }

        var storageKey = await storage.SaveAsync(new MemoryStream(clean), format.Extension, cancellationToken);
        try
        {
            var now = clock.UtcNow;
            var image = new PrescriptionImage
            {
                Id = Guid.NewGuid(),
                OwnerUserId = userId,
                StorageKey = storageKey,
                OriginalFileName = NormalizeFileName(originalFileName, format),
                ContentType = format.ContentType,
                SizeBytes = clean.Length,
                UploadedAt = now,
            };
            db.PrescriptionImages.Add(image);
            // Không ghi tên file vào audit: tên file có thể chứa thông tin sức khỏe.
            AuditLogWriter.Add(db, now, AuditActions.PrescriptionImageUploaded, nameof(PrescriptionImage),
                image.Id.ToString(), userId, deviceId);
            await db.SaveChangesAsync(cancellationToken);

            return (PrescriptionUploadOutcome.Ok,
                new PrescriptionImageView(image.Id, image.OriginalFileName, image.ContentType, image.SizeBytes, now));
        }
        catch
        {
            // Ghi metadata thất bại: không để lại file mồ côi.
            await storage.DeleteAsync(storageKey, CancellationToken.None);
            throw;
        }
    }

    private static string NormalizeFileName(string? fileName, ImageFormat format)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/')).Trim();
        name = new string(name.Where(c => !char.IsControl(c)).ToArray());
        if (name.Length == 0)
        {
            return $"image.{format.Extension}";
        }

        return name.Length <= 255 ? name : name[..255];
    }
}
