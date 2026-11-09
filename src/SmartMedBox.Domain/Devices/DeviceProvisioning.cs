using SmartMedBox.Domain.Entities;

namespace SmartMedBox.Domain.Devices;

public sealed record ProvisionedDevice(Device Device, IReadOnlyList<Compartment> Compartments);

public static class DeviceProvisioning
{
    public const int CompartmentCount = 3;

    private static readonly string[] CompartmentNames = ["Sáng", "Trưa", "Tối"];

    public static ProvisionedDevice Create(string deviceId, string serialNumber, string deviceApiKeyHash)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            DeviceId = Require(deviceId, 100, nameof(deviceId)),
            SerialNumber = Require(serialNumber, 100, nameof(serialNumber)),
            DeviceApiKeyHash = Require(deviceApiKeyHash, 500, nameof(deviceApiKeyHash)),
            ScheduleVersion = 0,
        };

        var compartments = new Compartment[CompartmentCount];
        for (var slot = 1; slot <= CompartmentCount; slot++)
        {
            compartments[slot - 1] = new Compartment
            {
                Id = Guid.NewGuid(),
                DeviceId = device.Id,
                SlotNumber = slot,
                Name = CompartmentNames[slot - 1],
                IsEnabled = true,
            };
        }

        return new ProvisionedDevice(device, compartments);
    }

    private static string Require(string value, int maxLength, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException($"Value must not exceed {maxLength} characters.", paramName);
        }

        return trimmed;
    }
}
