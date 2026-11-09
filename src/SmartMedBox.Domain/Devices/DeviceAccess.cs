namespace SmartMedBox.Domain.Devices;

/// <summary>Vai trò của người dùng đối với một hộp cụ thể (API_CONTRACT §11).</summary>
public enum DeviceAccessRole
{
    None,
    Owner,
    Relative,
}

/// <summary>Nhóm thao tác theo hộp.</summary>
public enum DeviceOperation
{
    /// <summary>Xem lịch, dashboard, lịch sử, cảnh báo — chủ hộp và người thân Accepted.</summary>
    Read,

    /// <summary>Sửa lịch/cấu hình, mời/thu hồi người thân, ảnh đơn thuốc — chỉ chủ hộp.</summary>
    Manage,
}

public static class DeviceAccessPolicy
{
    public static bool Allows(DeviceAccessRole role, DeviceOperation operation) => operation switch
    {
        DeviceOperation.Read => role is DeviceAccessRole.Owner or DeviceAccessRole.Relative,
        DeviceOperation.Manage => role is DeviceAccessRole.Owner,
        _ => false,
    };
}
