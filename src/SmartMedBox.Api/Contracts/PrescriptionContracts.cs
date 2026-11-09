namespace SmartMedBox.Api.Contracts;

public sealed record PrescriptionImageDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    int SizeBytes,
    DateTimeOffset UploadedAt);

public sealed record PrescriptionImageListDto(IReadOnlyList<PrescriptionImageDto> Items);
