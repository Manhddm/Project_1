using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Data;

namespace SmartMedBox.Infrastructure.Devices;

public sealed class DeviceProvisioningService(AppDbContext db)
{
    public async Task<ProvisionedDevice> ProvisionAsync(
        string deviceId,
        string serialNumber,
        string deviceApiKeyHash,
        CancellationToken cancellationToken = default)
    {
        var provisioned = DeviceProvisioning.Create(deviceId, serialNumber, deviceApiKeyHash);

        db.Devices.Add(provisioned.Device);
        db.Compartments.AddRange(provisioned.Compartments);
        await db.SaveChangesAsync(cancellationToken);

        return provisioned;
    }
}
