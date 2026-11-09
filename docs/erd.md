# ERD — Hộp thuốc thông minh (P0-02)

> Phiên bản: 1.1 · Ngày: 2026-10-29 · Nguồn: HTML §CSDL, [AGENTS.md](AGENTS.md) §3.3
>
> 17 bảng nghiệp vụ = 18 bảng HTML trừ `RefreshTokens` (D-02 dùng cookie session), cộng `AuthTokens` (bổ sung kỹ thuật cho token xác minh email/đặt lại mật khẩu — P1-02/P1-04). Migrations: `InitialSchema`, `AddScheduleDateRangeCheck`, `AddAuthTokens`. Snapshot SQL: [schema.sql](schema.sql).

## 1. Bảng → Entity

| Bảng | Entity | Ghi chú |
|---|---|---|
| `ApplicationUsers` | `ApplicationUser` | Email unique; `IsDeleted` (ẩn danh theo D-17) |
| `AuthTokens` | `AuthToken` | Token một lần cho `EmailVerification`/`PasswordReset`; chỉ lưu HMAC-SHA256 + pepper |
| `CareRelationships` | `CareRelationship` | Unique filtered cặp Active |
| `RelativeInvitations` | `RelativeInvitation` | Unique filtered Pending theo `(DeviceId, InviteeEmail)` |
| `Devices` | `Device` | `DeviceId`/`SerialNumber` unique; owner unique filtered |
| `DevicePairingCodes` | `DevicePairingCode` | `CodeHash` unique; dùng một lần |
| `Compartments` | `Compartment` | Unique `(DeviceId, SlotNumber)`, check 1–3 |
| `DeviceEvents` | `DeviceEvent` | Unique `(DeviceId, EventId)`; append-only |
| `MedicationSchedules` | `MedicationSchedule` | Xóa mềm; grace 1–120; `EndDate >= StartDate` |
| `ScheduleDays` | `ScheduleDay` | PK `(ScheduleId, DayOfWeek)`, check 1–7 |
| `ScheduleItems` | `ScheduleItem` | Quantity `decimal(10,2)` > 0 |
| `DoseInstances` | `DoseInstance` | Unique `(ScheduleId, ScheduledAt)`; trạng thái enum |
| `DoseItemSnapshots` | `DoseItemSnapshot` | Bất biến sau khi sinh |
| `Alerts` | `Alert` | Unique `(DoseInstanceId, AlertType)` |
| `AlertRecipients` | `AlertRecipient` | Unique `(AlertId, RecipientUserId, Channel)`; Channel `Email` |
| `EmailDeliveries` | `EmailDelivery` | Unique `(AlertRecipientId, AttemptNumber)`; lease `LockedUntil` |
| `PrescriptionImages` | `PrescriptionImage` | Chỉ metadata; không cột binary |
| `AuditLogs` | `AuditLog` | Append-only; `Details` `nvarchar(max)` |

## 2. Ràng buộc chính

| Loại | Ràng buộc |
|---|---|
| Unique | `ApplicationUsers.Email`; `AuthTokens.TokenHash`; `Devices.DeviceId`; `Devices.SerialNumber`; `DevicePairingCodes.CodeHash`; `Compartments(DeviceId, SlotNumber)`; `DeviceEvents(DeviceId, EventId)`; `DoseInstances(ScheduleId, ScheduledAt)`; `Alerts(DoseInstanceId, AlertType)`; `AlertRecipients(AlertId, RecipientUserId, Channel)`; `EmailDeliveries(AlertRecipientId, AttemptNumber)`; `ScheduleDays(ScheduleId, DayOfWeek)` |
| Filtered unique | `Devices.OwnerUserId` khi khác NULL; `CareRelationships(DeviceId, RelativeUserId)` khi `Status='Active'`; `RelativeInvitations(DeviceId, InviteeEmail)` khi `Status='Pending'` |
| Check | Slot 1–3; DayOfWeek 1–7; Grace 1–120; `EndDate` NULL hoặc `>= StartDate`; Quantity > 0; `AuthTokens.Purpose` thuộc `{EmailVerification, PasswordReset}`; `Status`/`AlertType`/`EventType`/`Channel` thuộc enum đã chốt |

## 3. Index đường đọc nóng

| Index | Dùng cho |
|---|---|
| `IX_DoseInstances_Status_DeadlineAt` | Job đánh giá quá hạn |
| `IX_DeviceEvents_DeviceId_OccurredAt` | Lịch sử/ghép sự kiện |
| `IX_EmailDeliveries_Status_NextAttemptAt` | Worker retry email |
| `IX_MedicationSchedules_DeviceId_IsDeleted` | `schedule-sync` |
| `IX_AuthTokens_UserId_Purpose_ExpiresAt` | Vô hiệu token cũ khi resend/gửi lại |

## 4. Quyết định kỹ thuật trong schema

- Toàn bộ FK dùng `DeleteBehavior.Restrict`: dữ liệu nghiệp vụ là append-only/xóa mềm, không xóa dây chuyền.
- `AuthTokens` là bảng bổ sung [ĐỀ XUẤT KỸ THUẬT] (HTML không có bảng token): token xác minh email/đặt lại mật khẩu cần hạn dùng + dùng một lần + thu hồi được. Chỉ lưu `HMAC-SHA256(pepper, token)` Base64 (pepper bắt buộc `Security:TokenPepper`, ≥ 32 ký tự, đọc từ user-secrets/env); token gốc chỉ nằm trong email. Dùng chung bảng cho `EmailVerification` và `PasswordReset` (P1-02/P1-04), phân biệt bằng `Purpose`.
- `EmailDelivery.LockedUntil` là bổ sung kỹ thuật bắt buộc bởi [AGENTS.md](AGENTS.md) §4.4 (claim/lease, an toàn nhiều worker); HTML không liệt kê cột này.
- `DoseInstances` unique `(ScheduleId, ScheduledAt)` là [ĐỀ XUẤT KỸ THUẬT] để job sinh dose chạy lặp/song song không nhân đôi (AGENTS §9.1).
- Giá trị enum theo [API_CONTRACT.md §10.2](API_CONTRACT.md#enums) — **D-09 đã chốt 2026-10-29**; check constraint trong `InitialSchema` khớp đúng các giá trị này.
- `Devices.LastSeenAt` cho phép NULL = chưa từng thấy/offline (AGENTS §3.3).
- `PrescriptionImages` không có cột nhị phân; file nằm ngoài CSDL (D-04).
