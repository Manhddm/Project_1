# Checklist audit (P7-01)

Mọi dòng audit ghi qua `AuditLogWriter.Add` (`src/SmartMedBox.Infrastructure/Auditing/AuditLogWriter.cs`), cùng `SaveChanges`/transaction với thao tác nghiệp vụ. Nội dung chỉ gồm: `Action`, `EntityType`, `EntityId` (Guid), `ActorUserId`, `DeviceId`, `Details` tối thiểu (version, `by=owner|relative`, `relationshipId`, loại cảnh báo + số người nhận). Không ghi mật khẩu, token, pairing code, `DeviceApiKey`/hash, email, tên file, storage key, tên thuốc, nội dung email/cảnh báo.

| Hành động | Hằng số `AuditActions` | Nơi ghi | Test | Trạng thái |
|---|---|---|---|---|
| Provision thiết bị | `DeviceProvisioned` | `DeviceOnboardingService.ProvisionAsync` | `DeviceOnboardingTests.ProvisionAsync_NewSerial_StoresOnlyHashesAndThreeCompartments` | Có |
| Cấp lại pairing code | `PairingCodeIssued` | `DeviceOnboardingService.IssuePairingCodeAsync` | `DeviceOnboardingTests.IssuePairingCodeAsync_UnpairedDevice_ExpiresPreviousCodeAndAddsNewHash` | Có |
| Ghép hộp | `DevicePaired` | `DeviceOwnershipService` (transaction ghép) | `DevicePairingApiTests.Pair_ValidCode_AssignsOwnerConsumesCodeAndAudits`, `Pair_ConcurrentRequestsSameCode_ExactlyOneSucceeds` | Có |
| Tạo/sửa/xóa lịch | `ScheduleCreated` / `ScheduleUpdated` / `ScheduleDeleted` | `ScheduleService` | `SchedulesApiTests.Crud_Owner_CreatesReadsUpdatesSoftDeletesAndIncrementsVersion`, `ScheduleOverlapTests.Create_ExactDuplicate_ReturnsOverlapWithoutWrites` (không ghi khi bị chặn) | Có |
| Tạo lời mời | `InvitationCreated` | `RelativeInvitationService` | `RelativeInvitationApiTests.CreateInvitation_Owner_Returns201StoresHashSendsEmailAndAudits` | Có |
| Thu hồi lời mời | `InvitationRevoked` | `RelativeInvitationService` | `RelativeInvitationApiTests.RevokeInvitation_Owner_Returns204AndTokenNoLongerAccepted` | Có |
| Chấp nhận lời mời | `InvitationAccepted` | `RelativeInvitationService` | `RelativeInvitationApiTests.AcceptInvitation_MatchingEmail_CreatesActiveRelationshipOnceAndAudits` | Có |
| Chủ thu hồi người thân / người thân ngừng theo dõi | `RelationshipRevoked` (`by=owner` / `by=relative`) | `CareRelationshipService.RevokeAsync` | `CareRelationshipApiTests.DeleteRelationship_Owner_RevokesKeepsHistoryAndAccessEndsImmediately`, `DeleteRelationship_RelativeOwn_StopsFollowing` | Có |
| Upload ảnh đơn | `PrescriptionImageUploaded` | `PrescriptionImageService` | `PrescriptionUploadApiTests.Upload_Owner_Returns201_StoresCleanFileOutsideWebRootAndMetadataOnly` | Có |
| Xóa ảnh đơn | `PrescriptionImageDeleted` | `PrescriptionImageService` | `PrescriptionDeleteApiTests.Delete_Owner_SoftDeletesAndAuditsWhileKeepingFile` | Có |
| Tạo cảnh báo (job nền, actor = null) | `AlertCreated` | `AlertEvaluationService` (cùng transaction alert + recipients) | `AlertEvaluationServiceTests.EvaluateAsync_TwoConcurrentInstances_CreateOneAlertPerDose`, `EvaluateAsync_AlertInsertFails_RollsBackStatusRecipientsAndDeliveries` | **Thêm mới (P7-01)** |

## Kiểm tra tổng hợp

- `AuditLogCoverageTests.FullFlow_AllImportantActions_AuditedWithoutSecretsOrPii`: chạy luồng đầy đủ qua API (ghép → lịch → mời/chấp nhận/thu hồi → ảnh → cảnh báo → ngừng theo dõi), kiểm tra đủ 14 dòng audit đúng hành động và không dòng nào chứa email, mật khẩu, pairing code/hash, API key/hash, token lời mời/hash, tên thuốc, tên file, storage key, nội dung cảnh báo, ký tự `@`.
- Append-only ở mức API: `AuditLogCoverageTests.Routes_NoEndpointExposesAuditLogs_AppendOnlyAtApiLevel` (không route nào chứa `audit`). Rà mã: ngoài `AuditLogWriter.Add` (chỉ `db.AuditLogs.Add`), không có chỗ nào `Update`/`Remove`/`ExecuteUpdate`/`ExecuteDelete` trên `AuditLogs` (`grep -rn AuditLog src --include=*.cs`).

## Chưa xử lý

- Retention audit: **[TBD-D19]**.
- Không audit: xác nhận muộn (`Alerts.ResolvedAt`), gửi email (đã có `EmailDeliveries` append-only), đăng nhập/đổi mật khẩu — ngoài danh sách P7-01.
