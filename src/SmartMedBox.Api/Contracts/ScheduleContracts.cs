using System.ComponentModel.DataAnnotations;

namespace SmartMedBox.Api.Contracts;

public sealed class ScheduleRequest
{
    [Required]
    public int? CompartmentSlot { get; set; }

    [Required]
    [RegularExpression("^([01][0-9]|2[0-3]):[0-5][0-9]$", ErrorMessage = "Giờ phải theo định dạng HH:mm (00:00–23:59).")]
    public string TimeOfDay { get; set; } = string.Empty;

    [Required]
    public List<int>? DaysOfWeek { get; set; }

    [Required]
    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    [Required]
    [Range(1, 120, ErrorMessage = "Thời gian chờ phải từ 1 đến 120 phút.")]
    public int? GracePeriodMinutes { get; set; }

    [Required]
    [MaxLength(100)]
    public string TimeZoneId { get; set; } = string.Empty;

    [Required]
    [MinLength(1, ErrorMessage = "Cần ít nhất một loại thuốc.")]
    public List<ScheduleItemRequest>? Items { get; set; }
}

public sealed class ScheduleItemRequest : IValidatableObject
{
    [Required]
    [MaxLength(200)]
    public string MedicineName { get; set; } = string.Empty;

    [Required]
    [Range(typeof(decimal), "0.01", "99999999.99", ErrorMessage = "Số lượng phải lớn hơn 0.")]
    public decimal? Quantity { get; set; }

    [Required]
    [MaxLength(50)]
    public string Unit { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Instructions { get; set; }

    // decimal(10,2): không để SQL Server làm tròn liều âm thầm.
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Quantity is { } quantity && decimal.Round(quantity, 2) != quantity)
        {
            yield return new ValidationResult("Số lượng tối đa 2 chữ số thập phân.", [nameof(Quantity)]);
        }
    }
}

public sealed record ScheduleItemDto(Guid Id, string MedicineName, decimal Quantity, string Unit, string Instructions);

public sealed record ScheduleDto(
    Guid Id,
    Guid DeviceId,
    int CompartmentSlot,
    string CompartmentName,
    string TimeOfDay,
    IReadOnlyList<int> DaysOfWeek,
    DateOnly StartDate,
    DateOnly? EndDate,
    int GracePeriodMinutes,
    string TimeZoneId,
    IReadOnlyList<ScheduleItemDto> Items,
    int Version,
    DateTimeOffset UpdatedAt);

public sealed record ScheduleListResponse(IReadOnlyList<ScheduleDto> Items);

public sealed record ScheduleCreatedResponse(Guid Id, int ScheduleVersion, DateTimeOffset CreatedAt);

public sealed record ScheduleUpdatedResponse(Guid Id, int ScheduleVersion);
