using Microsoft.EntityFrameworkCore;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.IntegrationTests.Schema;

namespace SmartMedBox.IntegrationTests.Devices;

public sealed class DeviceProvisioningTests(SqlServerDatabaseFixture fixture)
    : IClassFixture<SqlServerDatabaseFixture>
{
    [Fact]
    public async Task ProvisionAsync_PersistsDeviceWithExactlyThreeCompartments()
    {
        await using var db = fixture.CreateContext();
        var service = new DeviceProvisioningService(db);
        var deviceId = $"device-{Guid.NewGuid():N}";

        var provisioned = await service.ProvisionAsync(
            deviceId,
            $"serial-{Guid.NewGuid():N}",
            "hashed-key");

        await using var verify = fixture.CreateContext();
        var storedDevice = await verify.Devices.AsNoTracking().SingleAsync(x => x.Id == provisioned.Device.Id);
        Assert.Equal(deviceId, storedDevice.DeviceId);
        Assert.Null(storedDevice.OwnerUserId);

        var compartments = await verify.Compartments.AsNoTracking()
            .Where(x => x.DeviceId == storedDevice.Id)
            .OrderBy(x => x.SlotNumber)
            .ToListAsync();

        Assert.Equal(3, compartments.Count);
        Assert.Equal(new[] { 1, 2, 3 }, compartments.Select(x => x.SlotNumber));
        Assert.Equal(new[] { "Sáng", "Trưa", "Tối" }, compartments.Select(x => x.Name));
        Assert.All(compartments, compartment => Assert.True(compartment.IsEnabled));
    }

    [Fact]
    public async Task ProvisionAsync_DuplicateDeviceId_LeavesNoCompartmentsForFailedAttempt()
    {
        await using var db = fixture.CreateContext();
        var service = new DeviceProvisioningService(db);
        var deviceId = $"device-{Guid.NewGuid():N}";

        await service.ProvisionAsync(deviceId, $"serial-{Guid.NewGuid():N}", "hashed-key");

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            service.ProvisionAsync(deviceId, $"serial-{Guid.NewGuid():N}", "hashed-key"));

        await using var verify = fixture.CreateContext();
        var storedDevice = await verify.Devices.AsNoTracking().SingleAsync(x => x.DeviceId == deviceId);
        var compartmentCount = await verify.Compartments.CountAsync(x => x.DeviceId == storedDevice.Id);
        Assert.Equal(3, compartmentCount);
    }
}
