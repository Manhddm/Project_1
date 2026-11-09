using SmartMedBox.Domain.Devices;

namespace SmartMedBox.UnitTests.Devices;

public sealed class DeviceAccessPolicyTests
{
    [Theory]
    [InlineData(DeviceAccessRole.Owner, DeviceOperation.Read, true)]
    [InlineData(DeviceAccessRole.Owner, DeviceOperation.Manage, true)]
    [InlineData(DeviceAccessRole.Relative, DeviceOperation.Read, true)]
    [InlineData(DeviceAccessRole.Relative, DeviceOperation.Manage, false)]
    [InlineData(DeviceAccessRole.None, DeviceOperation.Read, false)]
    [InlineData(DeviceAccessRole.None, DeviceOperation.Manage, false)]
    public void Allows_RoleAndOperation_MatchesAuthorizationMatrix(
        DeviceAccessRole role,
        DeviceOperation operation,
        bool expected)
    {
        Assert.Equal(expected, DeviceAccessPolicy.Allows(role, operation));
    }
}
