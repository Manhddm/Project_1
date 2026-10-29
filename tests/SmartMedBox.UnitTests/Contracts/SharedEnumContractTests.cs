using SmartMedBox.Domain.Enums;

namespace SmartMedBox.UnitTests.Contracts;

public sealed class SharedEnumContractTests
{
    [Fact]
    public void DoseStatus_Values_MatchContract() =>
        Assert.Equal(new[] { "Pending", "Confirmed", "Overdue", "Unknown" }, Enum.GetNames<DoseStatus>());

    [Fact]
    public void PairingState_Values_MatchContract() =>
        Assert.Equal(new[] { "Unpaired", "Paired" }, Enum.GetNames<PairingState>());

    [Fact]
    public void AlertType_Values_MatchContract() =>
        Assert.Equal(new[] { "OverdueUnconfirmed", "UnconfirmedOffline" }, Enum.GetNames<AlertType>());

    [Fact]
    public void DeviceEventType_Values_MatchContract() =>
        Assert.Equal(new[] { "LidOpened", "LidClosed" }, Enum.GetNames<DeviceEventType>());

    [Fact]
    public void InvitationStatus_Values_MatchContract() =>
        Assert.Equal(new[] { "Pending", "Accepted", "Revoked", "Expired" }, Enum.GetNames<InvitationStatus>());

    [Fact]
    public void RelationshipStatus_Values_MatchContract() =>
        Assert.Equal(new[] { "Active", "Revoked" }, Enum.GetNames<RelationshipStatus>());

    [Fact]
    public void EmailDeliveryStatus_Values_MatchContract() =>
        Assert.Equal(new[] { "Pending", "Sent", "Failed", "Exhausted" }, Enum.GetNames<EmailDeliveryStatus>());
}
