using SmartMedBox.Domain.Devices;

namespace SmartMedBox.UnitTests.Devices;

public sealed class DeviceProvisioningTests
{
    [Fact]
    public void Create_SeedsThreeFixedCompartments()
    {
        var provisioned = DeviceProvisioning.Create("dev-1", "serial-1", "hash-1");

        Assert.Equal(3, provisioned.Compartments.Count);
        Assert.Equal(new[] { 1, 2, 3 }, provisioned.Compartments.Select(x => x.SlotNumber));
        Assert.Equal(new[] { "Sáng", "Trưa", "Tối" }, provisioned.Compartments.Select(x => x.Name));
        Assert.All(provisioned.Compartments, compartment =>
        {
            Assert.Equal(provisioned.Device.Id, compartment.DeviceId);
            Assert.True(compartment.IsEnabled);
        });
        Assert.Equal(3, provisioned.Compartments.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void Create_LeavesDeviceUnpaired()
    {
        var provisioned = DeviceProvisioning.Create("dev-1", "serial-1", "hash-1");

        Assert.Null(provisioned.Device.OwnerUserId);
        Assert.Equal(0, provisioned.Device.ScheduleVersion);
    }

    [Fact]
    public void Create_TrimsIdentifiers()
    {
        var provisioned = DeviceProvisioning.Create("  dev-1  ", " serial-1 ", " hash-1 ");

        Assert.Equal("dev-1", provisioned.Device.DeviceId);
        Assert.Equal("serial-1", provisioned.Device.SerialNumber);
        Assert.Equal("hash-1", provisioned.Device.DeviceApiKeyHash);
    }

    [Theory]
    [InlineData("", "serial-1", "hash-1")]
    [InlineData("dev-1", " ", "hash-1")]
    [InlineData("dev-1", "serial-1", "")]
    public void Create_MissingRequiredValue_Throws(string deviceId, string serialNumber, string deviceApiKeyHash)
    {
        Assert.Throws<ArgumentException>(() => DeviceProvisioning.Create(deviceId, serialNumber, deviceApiKeyHash));
    }

    [Fact]
    public void Create_ValueTooLong_Throws()
    {
        Assert.Throws<ArgumentException>(() => DeviceProvisioning.Create(new string('d', 101), "serial-1", "hash-1"));
        Assert.Throws<ArgumentException>(() => DeviceProvisioning.Create("dev-1", new string('s', 101), "hash-1"));
        Assert.Throws<ArgumentException>(() => DeviceProvisioning.Create("dev-1", "serial-1", new string('h', 501)));
    }
}
