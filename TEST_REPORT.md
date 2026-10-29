# TEST_REPORT.md — Báo cáo thực thi kiểm thử MVP Hộp thuốc thông minh

> Phiên bản: 1.0 · Ngày: 2026-10-29 · Người thực hiện: QA Automation
>
> Kế hoạch nguồn: [TEST_PLAN.md](TEST_PLAN.md) · Tiến độ: [TASKS.md](TASKS.md) · Hợp đồng: [API_CONTRACT.md](API_CONTRACT.md) v1.3
>
> Môi trường: `QA-ENV-1` (một phần) — Windows 10.0.19045, .NET SDK 10.0.402, commit nền `0f1bb2a` + thay đổi chưa commit.

---

## 1. Kết luận điều hành

**MVP CHƯA THỂ NGHIỆM THU (NOT ACCEPTED).**

- Chỉ **1/82** test case đã chạy được và đạt (`TC-SMOKE-001`); **0 FAIL** vì các test còn lại chưa thể chạy, không phải vì chúng đạt.
- Toàn bộ API nghiệp vụ chưa tồn tại: `GET /api/users/me` và `GET /api/schedules` trả **404**, không phải `401`/`200` như hợp đồng. Không có schema/migration, không có xác thực, ghép hộp, lịch, lần uống, đồng bộ sự kiện, cảnh báo email hay ảnh.
- **8 test bị `BLOCKED`** do quyết định nghiệp vụ chưa chốt (D-22, D-23, D-24, D-26) hoặc thiếu thiết bị/simulator (D-06); **73 test `NOT_RUN`** do dependency chưa triển khai.
- **0/12 Acceptance Criteria** có bằng chứng đạt. Không được tick cổng M1–M5, không được sign-off.
- Không có `PASS` nào được điền cho test chưa chạy; các test chưa chạy đều ghi rõ lý do.

> Báo cáo này chỉ phản ánh trạng thái tại thời điểm chạy. Khi các module được triển khai, phải chạy lại theo [TEST_PLAN.md](TEST_PLAN.md) và cập nhật cả hai tài liệu.

---

## 2. Phạm vi và môi trường thực thi

### 2.1. Những gì đã có trong repo

| Thành phần | Trạng thái thực tế |
|---|---|
| Solution 5 project (`Api`, `Domain`, `Infrastructure`, `UnitTests`, `IntegrationTests`) | Có, build sạch |
| Razor UI | Chỉ trang chủ placeholder (`Home/Index`) |
| API nghiệp vụ | **Không có** controller/endpoint nào ngoài `Home` |
| DbContext / entity / migration | **Không có** (P0-02 `TODO`) |
| Xác thực người dùng, CSRF | **Không có** (P1-03 `TODO`) |
| Xác thực thiết bị, ghép QR | **Không có** (P2-* `TODO`) |
| Lịch, lần uống, snapshot, sự kiện, cảnh báo, email, ảnh | **Không có** (P3–P6 `TODO`) |
| Test tự động | 1 integration test (`HomePageTests`); unit test project rỗng |
| CI / SQL Server test DB / email sink / clock giả / simulator | **Không có** |

### 2.2. Khoảng trống môi trường `QA-ENV-1`

| Hạng mục | Trạng thái | Ảnh hưởng |
|---|---|---|
| SQL Server thật (Testcontainers/instance) | Chưa cấu hình; chưa có connection string hay migration | Không kiểm chứng được unique/check/append-only/idempotency |
| `IEmailSender` + dev sink | Chưa tồn tại (D-03) | Không kiểm chứng AC-09, retry email |
| `IClock` điều khiển được | Chưa tồn tại | Không mô phỏng deadline/job chạy muộn |
| Vùng lưu ảnh private | Chưa tồn tại (D-04) | Không kiểm chứng AC-11 |
| ESP32 thật / simulator | Chưa có (D-06) | AC-01/03/04/05 không kiểm chứng được |
| Playwright/browser harness | Chưa có | Toàn bộ test UI `NOT_RUN` |

---

## 3. Tổng hợp kết quả

### 3.1. Số liệu tổng

| Chỉ số | Giá trị |
|---|---|
| Tổng test case | **82** |
| Đã chạy | **1** (1,2%) |
| `PASS` | **1** |
| `FAIL` | **0** |
| `BLOCKED` | **8** |
| `NOT_RUN` | **73** |
| Acceptance Criteria đạt | **0/12** |

### 3.2. Theo module

| Module | Tổng | PASS | FAIL | BLOCKED | NOT_RUN | Ghi chú |
|---|---|---|---|---|---|---|
| Authentication | 8 | 0 | 0 | 0 | 8 | Không có endpoint auth |
| Authorization | 10 | 0 | 0 | 0 | 10 | Không có endpoint để phân quyền |
| Device Pairing | 9 | 0 | 0 | 0 | 9 | Không có pair/provision |
| Medication Schedule | 9 | 0 | 0 | 2 | 7 | 2 blocked: D-22, D-23 |
| Dose Instance & Snapshot | 11 | 0 | 0 | 1 | 10 | 1 blocked: D-24 |
| ESP32 Event Sync | 12 | 0 | 0 | 4 | 8 | 4 blocked: D-06 (2), D-26 (2) |
| Email Alerts | 10 | 0 | 0 | 1 | 9 | 1 blocked: D-24 |
| Prescription Images | 8 | 0 | 0 | 0 | 8 | Không có upload/storage |
| UI & thuật ngữ | 4 | 0 | 0 | 0 | 4 | UI chỉ là placeholder |
| Smoke | 1 | 1 | 0 | 0 | 0 | Đã chạy |
| **Tổng** | **82** | **1** | **0** | **8** | **73** | |

---

## 4. Test đã thực thi — bằng chứng

### 4.1. `TC-SMOKE-001` — PASS

| Trường | Nội dung |
|---|---|
| Module | Smoke / P0-01 |
| Preconditions | Solution build được, `P0-01` đã `DONE` |
| Steps | 1) `dotnet test Project_1.sln` 2) chạy Kestrel 3) `GET /` 4) `GET /css/site.css` |
| Expected | App khởi động; `/` trả `200 text/html` chứa "Hộp thuốc thông minh"; CSS `200`; test `Get_Root_ReturnsHtmlPage` pass |
| Actual | `dotnet --version` → `10.0.402`; build `0 Warning / 0 Error`; `dotnet test` → `Passed! - Failed: 0, Passed: 1, Skipped: 0, Total: 1, Duration: 634 ms`; `GET /` → `200 text/html; charset=utf-8` có chuỗi "Hộp thuốc thông minh"; `GET /css/site.css` → `200 text/css` |
| Status | `PASS` |
| Task | P0-01 |

Lệnh tái lập:

```bash
dotnet build Project_1.sln
dotnet test Project_1.sln
dotnet run --project src/SmartMedBox.Api --urls http://127.0.0.1:5199
curl -i http://127.0.0.1:5199/
curl -i http://127.0.0.1:5199/css/site.css
```

### 4.2. Bằng chứng bổ sung (không phải test case, dùng để xác định blocker)

| Phép thử | Kỳ vọng theo hợp đồng | Thực tế |
|---|---|---|
| `GET /api/users/me` không cookie | `401` (ProblemDetails) | **`404`** |
| `GET /api/schedules` không cookie | `401` (ProblemDetails) | **`404`** |
| Log khởi động Kestrel | Không cảnh báo cấu hình | `warn: HttpsRedirectionMiddleware — Failed to determine the https port for redirect.` |

Các phép thử này chứng minh endpoint **không tồn tại**, không phải "bảo mật tốt". Vì vậy không được tính là `PASS` cho `TC-AUTHZ-001`.

---

## 5. Kết quả theo Acceptance Criteria

> Quy ước trạng thái theo [TEST_PLAN §2](TEST_PLAN.md#2-quy-ước-trạng-thái). Không AC nào có bằng chứng đạt.

| AC | Test case chính | Trạng thái | Lý do / bằng chứng |
|---|---|---|---|
| AC-01 | TC-SYNC-001, TC-DOSE-003 | `BLOCKED` | Không có firmware/simulator (D-06) và không có API lần uống |
| AC-02 | TC-DOSE-003…005, 009 | `NOT_RUN` | Không có endpoint sự kiện/lần uống (P3-05, P4-03) |
| AC-03 | TC-DOSE-006 | `BLOCKED` | Cần hành vi firmware phát hiện cạnh + reboot khi nắp mở (HW-03) |
| AC-04 | TC-SYNC-001, 002 | `BLOCKED` | Cần thiết bị/simulator offline queue (D-06) |
| AC-05 | TC-SYNC-003, 004, 012 | `NOT_RUN` | Không có `POST /api/device/events/batch` (P4-02) |
| AC-06 | TC-ALERT-002, 008, TC-UI-002 | `NOT_RUN` | Không có job đánh giá quá hạn/dashboard (P4-04, P5-02) |
| AC-07 | TC-AUTHZ-002…006 | `NOT_RUN` | Không có endpoint lịch/ảnh/lời mời để kiểm tra quyền |
| AC-08 | TC-PAIR-005, 007, 009 | `NOT_RUN` | Không có pair API, QR hay provision (P2-02…P2-04) |
| AC-09 | TC-ALERT-003, 006 | `NOT_RUN` | Không có alert/email worker (P5-02, P5-03) |
| AC-10 | TC-SCHED-006, TC-DOSE-001, 008 | `NOT_RUN` | Không có schema/dose generation/snapshot (P0-02, P3-03) |
| AC-11 | TC-IMG-002…007 | `NOT_RUN` | Không có upload/validation/storage (P6-*) |
| AC-12 | TC-AUTH-001, 004…007 | `NOT_RUN` | Không có endpoint đăng ký/xác minh/đăng nhập/reset (P1-02…P1-04) |

**Kết luận:** `0/12` AC đạt; `3/12` bị chặn bởi phần cứng/quyết định (AC-01, AC-03, AC-04); `9/12` chưa chạy do chưa triển khai.

---

## 6. Danh sách test `BLOCKED`

| Test ID | Module | Điều kiện chặn | Cần ai/quyết định gì |
|---|---|---|---|
| TC-SCHED-007 | Schedule | D-22 chưa chốt: sửa lịch tác động dose tương lai thế nào | Chủ sản phẩm + Tech Lead chốt D-22 |
| TC-SCHED-009 | Schedule | D-23 chưa chốt: lịch trùng/chồng lấn cùng ngăn | Chốt D-23 |
| TC-DOSE-011 | Dose | D-24 chưa chốt: xác nhận muộn sau `Overdue`/`Unknown` | Chốt D-24 |
| TC-SYNC-001 | Sync | Không có ESP32/simulator (D-06) | Cấp thiết bị hoặc duyệt simulator |
| TC-SYNC-002 | Sync | Không có ESP32/simulator (D-06) | Cấp thiết bị hoặc duyệt simulator |
| TC-SYNC-006 | Sync | D-26 chưa chốt: ngưỡng lệch giờ, event tương lai | Chốt D-26 |
| TC-SYNC-011 | Sync | D-26 + thiết bị thật cho RTC lệch | Chốt D-26, cấp thiết bị |
| TC-ALERT-009 | Alerts | D-24: recipient bị thu hồi trước khi gửi | Chốt D-24 |

---

## 7. Defect log (lỗi cần sửa)

> Phân loại theo mức độ ảnh hưởng tới nghiệm thu MVP. "Fix" là task PLAN tương ứng, không phải yêu cầu mới ngoài phạm vi.

### 7.1. Critical

| ID | Mô tả | Bằng chứng | Task sửa |
|---|---|---|---|
| DEF-01 | Không tồn tại API nghiệp vụ nào (`/api/auth/*`, `/api/users/me`, `/api/devices/pair`, `/api/schedules`, `/api/device/events/batch`, `/api/alerts`, ảnh…). Mọi request trả `404` | `GET /api/users/me` → 404; `GET /api/schedules` → 404 | P1-02…P6-04 |
| DEF-02 | Không có DbContext/entity/migration; không có CSDL để kiểm chứng unique, check, filtered index, append-only, snapshot | P0-02 `TODO`; repo không có `Migrations/` | P0-02 |

### 7.2. High

| ID | Mô tả | Bằng chứng | Task sửa |
|---|---|---|---|
| DEF-03 | Không có `ProblemDetails`/exception handler toàn cục; lỗi trả HTML mặc định, thiếu `code`/`traceId` | 404 không phải JSON ProblemDetails | P0-04 |
| DEF-04 | Không có cookie session + CSRF; không thể kiểm tra `HttpOnly/Secure/SameSite` | Không có `AddAuthentication`/`AddAntiforgery` trong `Program.cs` | P1-03, P7-03 |
| DEF-05 | Không có xác thực thiết bị `X-Device-Id` + `X-Device-Api-Key`; không có `401 DEVICE_UNAUTHORIZED` | Không có middleware/endpoint thiết bị | P2-06, P2-07 |
| DEF-06 | Không có ghép QR một lần, atomic, chống brute-force → AC-08 không kiểm chứng được | P2-02…P2-04 `TODO` | P2-02…P2-04 |
| DEF-07 | Không có batch sự kiện idempotent theo `(DeviceId, EventId)` → AC-05 không kiểm chứng được | P4-02 `TODO` | P4-02 |
| DEF-08 | Không có sinh dose/snapshot/state machine/ghép sự kiện → AC-02, AC-06, AC-10 không kiểm chứng được | P3-03, P3-05, P4-03, P4-04 `TODO` | P3-03…P4-04 |
| DEF-09 | Không có job cảnh báo + email worker + retry độc lập → AC-09 không kiểm chứng được | P5-02, P5-03 `TODO`; D-03 chưa chốt | P5-02, P5-03 |
| DEF-10 | Không có pipeline ảnh (5 MB, magic bytes, EXIF, private storage, phân quyền) → AC-11 không kiểm chứng được | P6-01…P6-04 `TODO`; D-04 chưa chốt | P6-01…P6-04 |
| DEF-11 | Không có UI nghiệp vụ; chỉ trang chủ placeholder → AC-06/07/11/12 không kiểm chứng được trên UI | `Views/Home/Index.cshtml` là placeholder | UI-01…UI-07 |

### 7.3. Medium

| ID | Mô tả | Bằng chứng | Task sửa |
|---|---|---|---|
| DEF-12 | `SmartMedBox.UnitTests` không có test nào; `dotnet test` báo "No test is available" | output `dotnet test` | P7-02 + test theo từng task |
| DEF-13 | Không có hạ tầng test: SQL Server thật/Testcontainers, email sink, `IClock`, simulator | Chưa có cấu hình/abstraction | P0-04, P7-02 |
| DEF-14 | `UseHttpsRedirection()` không xác định được cổng HTTPS khi chạy dev → cảnh báo, không chuyển hướng | log Kestrel | P0-04 / cấu hình môi trường |
| DEF-15 | 8 test bị chặn bởi TBD chưa chốt (D-22, D-23, D-24, D-26) | §6 | Chốt quyết định trước P3-02/P3-05/P4-02/HW-04 |

---

## 8. Các trường hợp đặc biệt bắt buộc — đối chiếu

| Yêu cầu của đề bài | Test ID | Trạng thái | Ghi chú |
|---|---|---|---|
| Mất mạng trước giờ nhắc | TC-SYNC-001 | `BLOCKED` | Cần thiết bị/simulator (D-06) |
| Mất mạng trong lúc nhắc | TC-SYNC-002 | `BLOCKED` | Cần thiết bị/simulator (D-06) |
| Mở sai ngăn | TC-DOSE-005 | `NOT_RUN` | Chưa có API sự kiện |
| Nắp đã mở sẵn | TC-DOSE-006 | `NOT_RUN` | Cần bench firmware + logic ghép cạnh |
| Gửi cùng `EventId` nhiều lần | TC-SYNC-003, TC-SYNC-004, TC-SYNC-012 | `NOT_RUN` | Chưa có batch API/schema |
| Sửa lịch sau khi Dose Instance được tạo | TC-SCHED-006, TC-DOSE-008 (D-22 → TC-SCHED-007 `BLOCKED`) | `NOT_RUN`/`BLOCKED` | Chưa có lịch/dose; D-22 chưa chốt |
| Người thân cố sửa lịch | TC-AUTHZ-004 (UI: TC-UI-004) | `NOT_RUN` | Chưa có endpoint phân quyền |
| QR bị sử dụng lại | TC-PAIR-005 (thêm TC-PAIR-007, 009) | `NOT_RUN` | Chưa có pair API |
| Email gửi thất bại rồi retry | TC-ALERT-006 | `NOT_RUN` | Chưa có worker/sink (D-03, D-13) |

---

## 9. Điều kiện cần để có thể nghiệm thu MVP

Thứ tự thực hiện đề xuất (không thay đổi phạm vi MVP):

1. **Chốt quyết định chặn QA:** D-08 (TTL/định dạng mã ghép), D-09 (enum), D-10 (timezone), D-11 (horizon), D-12 (ngưỡng offline), D-13 (retry), D-14 (mật khẩu/rate limit), **D-22, D-23, D-24, D-26**; chọn D-03 (email), D-04 (storage); xác nhận D-06 (firmware/simulator).
2. **Dựng `QA-ENV-1` thật:** SQL Server + migration, email sink, `IClock`, thư mục ảnh private, simulator thiết bị. Không dùng EF InMemory cho ràng buộc.
3. **P0-02…P0-05:** schema + 4 index nóng + check/unique; ProblemDetails + mã lỗi; secret vệ sinh.
4. **P1 → P2:** auth cookie/CSRF, xác minh email, reset mật khẩu, rate limit; ghép QR atomic, xác thực thiết bị.
5. **P3 → P4:** lịch + `ScheduleVersion`, sinh dose + snapshot, state machine, batch sự kiện idempotent, đánh giá online/offline tại `DeadlineAt`.
6. **P5 → P6:** alert + recipient transaction, email worker claim/lease/retry; ảnh private + magic bytes + EXIF + quyền.
7. **UI-01…UI-07, HW-01…HW-05**, sau đó chạy lại toàn bộ [TEST_PLAN.md](TEST_PLAN.md) và cập nhật báo cáo này.

**Điều kiện sign-off (theo [TEST_PLAN §7.2](TEST_PLAN.md#72-exit-criteria)):**

- 100% test P0/P1 (Authentication, Authorization, Pairing) `PASS`.
- 12/12 AC có bằng chứng `PASS`; phần cứng có bench/simulator hoặc ghi rõ chưa kiểm chứng.
- 0 defect Critical/High mở; 0 test `BLOCKED` do quyết định nghiệp vụ chưa chốt.
- Không còn chuỗi "đã uống"/"quên uống"/"bỏ liều" trong UI/email.

---

## 10. Rủi ro và lưu ý cho lần chạy tiếp theo

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Test chạy trên EF InMemory | Bỏ sót lỗi unique/check/idempotency thật | Bắt buộc SQL Server thật |
| Job chạy song song không tất định | Kết quả test chập chờn | Test 2 instance + kiểm tra `LockedUntil` |
| Đồng hồ hệ thống thay đổi giữa test | Deadline/biên inclusive sai | Dùng `IClock` cố định + dịch chuyển có kiểm soát |
| D-22 chưa chốt | Sửa lịch có thể làm lệch dose server ↔ thiết bị | Chặn `DONE` P3-02/P3-03/P3-04 |
| Thiếu thiết bị thật | AC-01/03/04/05 chỉ là logic, không phải hành vi phần cứng | Ghi rõ "chưa kiểm chứng trên phần cứng" |
| Test UI chưa có harness | Không phát hiện lỗi thuật ngữ/phân quyền UI | Thêm Playwright trước khi nghiệm thu |

---

## 11. Phụ lục — lệnh và artifact

| Mục | Lệnh / đường dẫn |
|---|---|
| Build | `dotnet build Project_1.sln` → 0W/0E |
| Test | `dotnet test Project_1.sln` → 1/1 pass (`SmartMedBox.IntegrationTests`) |
| Smoke HTTP | `dotnet run --project src/SmartMedBox.Api --urls http://127.0.0.1:5199` + `curl` |
| Test plan | [TEST_PLAN.md](TEST_PLAN.md) |
| Tiến độ | [TASKS.md](TASKS.md) §13, §14 |
| Hợp đồng API | [API_CONTRACT.md](API_CONTRACT.md) v1.3 |
