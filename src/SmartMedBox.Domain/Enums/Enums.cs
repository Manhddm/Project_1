namespace SmartMedBox.Domain.Enums;

public enum DoseStatus
{
    Pending,
    Confirmed,
    Overdue,
    Unknown,
}

public enum PairingState
{
    Unpaired,
    Paired,
}

public enum AlertType
{
    OverdueUnconfirmed,
    UnconfirmedOffline,
}

public enum DeviceEventType
{
    LidOpened,
    LidClosed,
}

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
    Expired,
}

public enum RelationshipStatus
{
    Active,
    Revoked,
}

public enum EmailDeliveryStatus
{
    Pending,
    Sent,
    Failed,
    Exhausted,
}
