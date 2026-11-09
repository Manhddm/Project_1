namespace SmartMedBox.Api.Contracts;

public sealed record PrescriptionImageDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    int SizeBytes,
    DateTimeOffset UploadedAt);
