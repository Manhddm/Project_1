# Báo cáo nghiệm thu tự động — P7-02a/P7-02b

> Ngày chạy: 2026-11-12 · Môi trường: SQL Server Express cục bộ (`SMARTMEDBOX_TEST_SERVER`), mỗi AC một CSDL mới.
> Lệnh: `dotnet test tests/SmartMedBox.IntegrationTests --filter "FullyQualifiedName~Acceptance"` → **12/12 PASS**.

## Cách kiểm thử

- **Harness** (`tests/SmartMedBox.IntegrationTests/Acceptance/AcceptanceHarness.cs`): chạy ứng dụng thật qua `WebApplicationFactory` + SQL Server, đồng hồ giả (`FakeClock`), email sink ghi lại/giả lập lỗi (`FakeEmailSender`). Job nền tắt; test gọi trực tiếp đúng service mà job gọi (`DoseGenerationService` với phạm vi `[now − 24 h, now + 48 h]`, `AlertEvaluationService`, `EmailDeliveryWorker`). Người dùng đi qua API thật: đăng ký → xác minh bằng token trong email → đăng nhập + CSRF → ghép hộp → mời/chấp nhận.
- **Simulator thiết bị** (`DeviceSimulator.cs`): **không phải phần cứng thật**. Mô phỏng RTC riêng (lệch ±), lịch cục bộ lấy từ `GET /api/device/schedule-sync`, 3 nắp chỉ sinh event ở cạnh đóng→mở/mở→đóng, khởi động lại khi nắp đang mở không sinh cạnh giả, hàng đợi bền với `EventId` ổn định, gửi lô khi online, gửi lại/xáo trộn lô, heartbeat (tự chỉnh RTC khi lệch > 2 giây), mất điện (hàng đợi + RTC giữ nguyên). Đèn/còi là logic thuần trên lịch cục bộ + RTC.
- Giờ trong kịch bản là giờ địa phương `Asia/Ho_Chi_Minh`.

## Kết quả

| AC | Test (`AcceptanceTests.`) | Kết quả | Khoảng trống chưa kiểm chứng |
|---|---|---|---|
| AC-01 | `AC01_ReminderAtScheduledTime_OnlyScheduledSlotLedAndBuzzer` | PASS | Đèn/còi/log là **mô phỏng**; chưa kiểm chứng trên thiết bị thật (HW-02). |
| AC-02 | `AC02_ConfirmationWindow_EarlyAndWrongSlotRejected_InsideAndBoundaryConfirm` | PASS | Phía Server đầy đủ (a)–(d); event do simulator tạo. |
| AC-03 | `AC03_LidAlreadyOpen_NotConfirmedUntilClosedAndReopened` | PASS | Phát hiện cạnh + khởi động lại khi nắp mở là **mô phỏng firmware**; cần bench test cảm biến thật (HW-03). |
| AC-04 | `AC04_OfflineAtReminder_StillRemindsQueuesAndUploadsOnceOnReconnect` | PASS | Mất Wi‑Fi, mất điện, RTC là **mô phỏng**; chưa kiểm chứng trên thiết bị thật (HW-02…HW-04). |
| AC-05 | `AC05_SameBatchSentThreeTimes_OneResultAndDuplicates` | PASS | — |
| AC-06 | `AC06_OfflineAtDeadline_UnknownWithCannotConfirmWording` | PASS | Kiểm API dashboard, email và HTML `/dashboard`, `/alerts` (không có "quên uống"/"bỏ liều"/"đã uống"). Nội dung do JS hiển thị trên trình duyệt kiểm ở UI-04/UI-05. |
| AC-07 | `AC07_RelativeReadsButCannotWriteOrSeeImages_OwnerCan` | PASS | Phía API; UI kiểm riêng ở UI track. |
| AC-08 | `AC08_PairOnce_RescanRejected_NoKeyLeak_ConcurrentOneWins` | PASS | Payload QR kiểm ở mức chuỗi; chưa quét QR thật bằng camera, chưa in nhãn (HW-05, D-20). |
| AC-09 | `AC09_OverdueOnline_ThreeRecipients_RetryOnlyFailedAddress` | PASS | Email qua sink giả, chưa gửi SMTP thật. |
| AC-10 | `AC10_EditScheduleAfterGeneration_HistoryAndSnapshotUnchanged` | PASS | — |
| AC-11 | `AC11_PrescriptionUploadRules` | PASS | Phía API; upload trên trình duyệt chưa kiểm (UI-06). |
| AC-12 | `AC12_AccountChain_RegisterVerifyLoginForgotReset` | PASS | Phía API; luồng UI kiểm ở UI-01. |

## Ghi chú

- Không phát hiện lỗi production nào trong lần chạy này; không sửa code production.
- Các phần phần cứng (đèn, còi, cảm biến nắp, RTC, Wi‑Fi, nguồn) mới chỉ được kiểm chứng bằng simulator: **chưa kiểm chứng trên thiết bị thật**. P7-02c (UI browser test) và kiểm thử bench phần cứng vẫn còn.
