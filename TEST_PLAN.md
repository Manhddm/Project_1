# TEST_PLAN.md — Kế hoạch kiểm thử MVP Hộp thuốc thông minh

> Phiên bản: 1.0 · Ngày: 2026-10-29 · Người lập: QA Automation
>
> Nguồn: `Hop-thuoc-thong-minh.html` (yêu cầu gốc), [AGENTS.md](AGENTS.md), [PLAN.md](PLAN.md), [TASKS.md](TASKS.md), [API_CONTRACT.md](API_CONTRACT.md) v1.3.
>
> Báo cáo kết quả: [TEST_REPORT.md](TEST_REPORT.md).

---

## 1. Mục tiêu và phạm vi

### 1.1. Mục tiêu

- Xác minh 12 tiêu chí nghiệm thu AC-01…AC-12 và các quyết định bất biến của MVP.
- Kiểm tra các rủi ro cao: offline, chống trùng sự kiện, ghép QR một lần, phân quyền chủ/người thân, snapshot bất biến, phân biệt "mở nắp" với "đã uống", email cảnh báo/retry, Unknown không bị kết luận sai.
- Tự động hóa tối đa ở tầng API và UI; phần cứng dùng bench/simulator và ghi rõ phần chưa kiểm chứng.

### 1.2. Trong phạm vi

- Auth, phân quyền, ghép thiết bị, lịch uống, lần uống/snapshot, đồng bộ sự kiện ESP32, offline/online, chống trùng, email cảnh báo, ảnh đơn thuốc, thuật ngữ UI.
- API Web (cookie session), API thiết bị (header khóa), background jobs.

### 1.3. Ngoài phạm vi

- App di động, push/SMS, OCR, chia sẻ ảnh, OTA, tích hợp y tế (AGENTS §2.3).
- Đo hiệu năng tải lớn, pen-test chuyên sâu, kiểm định pháp lý.
- Chất lượng cảm biến/độ bền cơ khí (chỉ kiểm tra logic firmware qua bench/simulator).

---

## 2. Quy ước trạng thái

| Trạng thái | Nghĩa |
|---|---|
| `PASS` | Đã chạy, kết quả đúng kỳ vọng, có bằng chứng |
| `FAIL` | Đã chạy, kết quả sai kỳ vọng; phải có log/actual |
| `BLOCKED` | Không thể chạy do thiếu quyết định nghiệp vụ, thiết bị, dịch vụ hoặc dependency |
| `NOT_RUN` | Chưa chạy (chưa triển khai hoặc chưa tới lượt); **không** được tính là đạt |

Quy tắc: không điền `PASS` khi chưa có bằng chứng chạy thực tế. Test phụ thuộc TBD phải ghi mã quyết định.

---

## 3. Môi trường và dữ liệu kiểm thử

### 3.1. Môi trường đích `QA-ENV-1`

| Thành phần | Yêu cầu |
|---|---|
| OS | Windows 10/11 hoặc Linux CI |
| .NET | SDK 10.0.x |
| CSDL | SQL Server thật (Testcontainers hoặc instance riêng), **không** EF InMemory cho ràng buộc |
| Email | Dev sink (`IEmailSender` ghi file/inbox) hoặc provider theo D-03 |
| Lưu ảnh | Thư mục private tạm, ngoài `wwwroot` (D-04) |
| Thời gian | `IClock` có thể điều khiển; test job chạy muộn/bù |
| Thiết bị | ESP32 thật hoặc simulator hỗ trợ offline queue, gửi lặp, RTC lệch, nắp mở sẵn (D-06) |

### 3.2. Bộ dữ liệu `QA-DATA-1`

| Mã | Nội dung |
|---|---|
| `FD-1` | Owner đã xác minh email, chưa sở hữu hộp |
| `FD-2` | Owner + thiết bị đã ghép, 3 ngăn slot 1–3, chưa có lịch |
| `FD-3` | `FD-2` + lịch 07:00 ngăn 1, grace 15, thuốc A; dose đã sinh cho hôm nay |
| `FD-4` | `FD-2` + relative A `Accepted`, relative B `Accepted`, relative C `Revoked`, outsider D (không quan hệ) |
| `FD-5` | Thiết bị offline, có N sự kiện trong queue với `eventId` ổn định |
| `FD-6` | File ảnh: JPEG/PNG/WebP hợp lệ; JPEG có EXIF GPS; file > 5 MB; file EXE đổi tên `.jpg`; ảnh hỏng |
| `FD-7` | Cấu hình ngưỡng offline, horizon, retry, rate limit có thể chỉnh cho test |

### 3.3. Tiền điều kiện chung

- `QA-ENV-1` sẵn sàng, DB được tạo từ migration (không `EnsureCreated`).
- Mỗi test độc lập dữ liệu (transaction rollback hoặc DB tái tạo).
- Không dùng secret thật; khóa thiết bị sinh riêng cho test.

---

## 4. Chiến lược automation

| Tầng | Công cụ | Phạm vi |
|---|---|---|
| Unit | xUnit | Quy tắc cửa sổ xác nhận, state machine, snapshot mapping, idempotency key |
| API integration | xUnit + `WebApplicationFactory` + SQL Server thật | Toàn bộ endpoint, quyền, lỗi, transaction, job |
| Background job | Hosted service trigger trực tiếp + 2 instance song song | Sinh dose, đánh giá quá hạn, email worker |
| UI | Playwright (desktop 1280px + mobile 360px) | Luồng chính, thuật ngữ, ẩn/hiện hành động theo vai trò |
| Firmware | Simulator/bench | Offline queue, RTC, nắp mở sẵn, gửi lặp batch |
| Static/security | Script + test | QR payload, không lộ secret, không URL ảnh công khai, header cookie |

Thứ tự chạy đề xuất: unit → API integration → job → UI → bench. CI chặn merge nếu test P0/P1 fail.

---

## 5. Truy vết Acceptance Criteria

| AC | Test case chính |
|---|---|
| AC-01 | TC-SYNC-001, TC-DOSE-003 |
| AC-02 | TC-DOSE-003, TC-DOSE-004, TC-DOSE-005, TC-DOSE-009 |
| AC-03 | TC-DOSE-006 |
| AC-04 | TC-SYNC-001, TC-SYNC-002 |
| AC-05 | TC-SYNC-003, TC-SYNC-004, TC-SYNC-012 |
| AC-06 | TC-ALERT-002, TC-ALERT-008, TC-UI-002 |
| AC-07 | TC-AUTHZ-002…TC-AUTHZ-006 |
| AC-08 | TC-PAIR-005, TC-PAIR-007, TC-PAIR-009 |
| AC-09 | TC-ALERT-003, TC-ALERT-006 |
| AC-10 | TC-SCHED-006, TC-DOSE-001, TC-DOSE-008 |
| AC-11 | TC-IMG-002…TC-IMG-007 |
| AC-12 | TC-AUTH-001, TC-AUTH-004, TC-AUTH-005, TC-AUTH-006, TC-AUTH-007 |

---

## 6. Test cases

> Cột **Actual** chỉ điền khi đã chạy. `—` = chưa có kết quả thực tế.

### 6.1. Authentication (8)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-AUTH-001 | `FD-1`, mail sink rỗng | 1) POST `/api/auth/register` email hợp lệ mới 2) kiểm tra DB + sink | `201` kèm `userId`, `email`, `emailVerificationRequired=true`; email chuẩn hóa trim/lowercase; đúng 1 user; có email xác minh | — | `NOT_RUN` | API | P1-02 |
| TC-AUTH-002 | `FD-1`, đã đăng ký `owner@example.com` | 1) đăng ký lại cùng email 2) thử biến thể hoa/thường và khoảng trắng | `409 EMAIL_TAKEN`; không tạo user thứ hai | — | `NOT_RUN` | API | P1-02 |
| TC-AUTH-003 | `FD-1` | 1) email sai định dạng 2) email > 320 ký tự 3) mật khẩu rỗng/ngắn 4) thiếu trường | `400 VALIDATION_ERROR` + `errors` theo trường; không tạo user (chính sách mật khẩu theo D-14) | — | `NOT_RUN` | API | P1-02 |
| TC-AUTH-004 | User đã đăng ký, token xác minh trong sink | 1) verify token 2) dùng lại token 3) verify token hết hạn | Lần 1 `200 {emailVerified:true}`; lần 2 `400 TOKEN_INVALID`; token hết hạn `410 TOKEN_EXPIRED` | — | `NOT_RUN` | API | P1-02 |
| TC-AUTH-005 | User verified + user chưa verify | 1) login user chưa verify 2) login user verified 3) kiểm tra `Set-Cookie` và body | Chưa verify `403 EMAIL_NOT_VERIFIED`; verified `200`, cookie `HttpOnly`, `Secure`, `SameSite=Lax`, body chỉ có `user`, **không** có token | — | `NOT_RUN` | API | P1-03 |
| TC-AUTH-006 | Đã đăng nhập | 1) POST logout 2) dùng lại cookie cũ 3) logout lần 2 | `204`; cookie bị xóa/thu hồi; request sau `401`; logout lặp vẫn `204` | — | `NOT_RUN` | API | P1-03 |
| TC-AUTH-007 | User verified | 1) forgot với email tồn tại 2) forgot với email không tồn tại 3) reset bằng token 4) dùng lại token 5) login mật khẩu cũ | Cả hai forgot trả `202` cùng message; reset `200`; token dùng lại `400`; mật khẩu cũ `401`; phiên cũ bị thu hồi | — | `NOT_RUN` | API | P1-04 |
| TC-AUTH-008 | `FD-7`, endpoint auth bật rate limit | 1) gửi burst login/register/forgot vượt ngưỡng 2) đọc header | `429 RATE_LIMITED` + `Retry-After`; không lộ email tồn tại (ngưỡng theo D-14) | — | `NOT_RUN` | API | P1-06 |

### 6.2. Authorization (10)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-AUTHZ-001 | Không có cookie | 1) gọi `/users/me`, `/schedules`, `/dashboard`, `/alerts`, `/prescriptions/images` 2) gọi 3 endpoint thiết bị bằng cookie người dùng | Tất cả `401`; không trả dữ liệu; thiết bị không chấp nhận auth người dùng | — | `NOT_RUN` | API | P1-05 |
| TC-AUTHZ-002 | `FD-2`, owner | 1) GET/POST/PUT/DELETE schedule 2) upload/xem/xóa ảnh 3) mời/thu hồi relative 4) xem dashboard/history/alerts | Owner thao tác hộp mình thành công đúng status hợp đồng | — | `NOT_RUN` | API | P1-05 |
| TC-AUTHZ-003 | `FD-4`, relative A | 1) GET schedules/dashboard/history/alerts của hộp theo dõi | `200`; chỉ dữ liệu hộp A theo dõi; không thấy hộp khác | — | `NOT_RUN` | API | P1-05 |
| TC-AUTHZ-004 | `FD-4`, relative A | 1) POST schedule 2) PUT schedule 3) DELETE schedule 4) đổi cấu hình ngăn | `403 FORBIDDEN` mọi thao tác ghi; dữ liệu không đổi | — | `NOT_RUN` | API | P1-05 |
| TC-AUTHZ-005 | `FD-4`, relative A | 1) tạo lời mời 2) list lời mời 3) revoke lời mời | `403 FORBIDDEN`; không tạo/thay đổi lời mời | — | `NOT_RUN` | API | P2-09 |
| TC-AUTHZ-006 | `FD-4`, relative A | 1) list ảnh 2) upload 3) tải content 4) xóa ảnh | `403 FORBIDDEN` cả 4; không lộ metadata/file | — | `NOT_RUN` | API | P6-03 |
| TC-AUTHZ-007 | `FD-4`, outsider D đã đăng nhập | 1) gọi mọi endpoint hộp của owner bằng `deviceId`/`id` 2) thử đoán GUID | `403` hoặc `404` thống nhất; không lộ tồn tại/dữ liệu | — | `NOT_RUN` | API | P7-02 |
| TC-AUTHZ-008 | `FD-4`, relative A và B | 1) A gọi `/relatives/relationships` 2) owner gọi cùng endpoint | A không thấy email của B; owner thấy danh sách đầy đủ; không lộ email ngoài phạm vi | — | `NOT_RUN` | API | P2-10 |
| TC-AUTHZ-009 | `FD-2` + owner cookie | 1) gọi `/device/heartbeat` bằng cookie 2) gọi `/api/schedules` bằng header thiết bị | Thiết bị `401 DEVICE_UNAUTHORIZED`; Web `401`; hai cơ chế tách biệt | — | `NOT_RUN` | API | P2-06 |
| TC-AUTHZ-010 | `FD-4`, relative A đang theo dõi | 1) A tự DELETE relationship 2) A gọi lại schedules/dashboard 3) owner gọi dashboard | A bị `403/404` ngay; owner vẫn `200`; alert tương lai không gửi cho A | — | `NOT_RUN` | API | P2-10 |

### 6.3. Device Pairing (9)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-PAIR-001 | `FD-1`, mã hợp lệ chưa dùng | 1) POST `/api/devices/pair` 2) kiểm tra response + DB | `200`; gán `OwnerUserId`; 3 compartment; `scheduleVersion=0`; **không** có `DeviceApiKey`/hash trong response; audit ghi nhận | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-002 | User chưa xác minh email, mã hợp lệ | 1) pair | `403 EMAIL_NOT_VERIFIED`; mã **không** bị đánh dấu đã dùng; thiết bị chưa có chủ | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-003 | `FD-1` | 1) pair mã sai định dạng 2) pair mã không tồn tại | Cả hai `400 PAIRING_CODE_INVALID`, cùng body; không tiết lộ thiết bị/mã có tồn tại | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-004 | Mã đã hết hạn (D-08) | 1) pair mã hết hạn | `410 PAIRING_CODE_EXPIRED`; không gán chủ | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-005 | `FD-1`, mã đã dùng thành công | 1) pair lại cùng mã 2) kiểm tra chủ thiết bị | `409 PAIRING_CODE_USED`; chủ không đổi; không tạo quan hệ mới (AC-08) | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-006 | User đã sở hữu hộp; thiết bị đã có chủ | 1) user sở hữu pair hộp khác 2) user khác pair thiết bị đã có chủ | `409 USER_ALREADY_OWNS_DEVICE` và `409 DEVICE_ALREADY_PAIRED`; không đổi chủ | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-007 | `FD-1`, một mã hợp lệ | 1) gửi 2 request pair đồng thời | Đúng 1 `200`, 1 `409`; đúng 1 chủ; `UsedAt`/audit một lần | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-008 | `FD-7`, pair rate limit bật | 1) gửi nhiều mã sai liên tiếp 2) đọc `Retry-After` | `429 RATE_LIMITED`; không bị bypass bằng cookie khác (ngưỡng D-14) | — | `NOT_RUN` | API | P2-04 |
| TC-PAIR-009 | Nhãn QR/provision đã in | 1) giải mã payload QR 2) grep secret trong ảnh/nhãn | Payload chỉ chứa pairing code; **không** có `DeviceApiKey`, hash, khóa Wi‑Fi hay URL nội bộ | — | `NOT_RUN` | Static | P2-03 |

### 6.4. Medication Schedule (9)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-SCHED-001 | `FD-2`, owner | 1) POST schedule hợp lệ 2) đọc `Location` + GET lại | `201`; `scheduleVersion` tăng; dữ liệu đúng; server lấy hộp từ tài khoản, không nhận `deviceId` trong body | — | `NOT_RUN` | API | P3-02 |
| TC-SCHED-002 | `FD-2`, owner | 1) slot 0 2) slot 4 3) slot thuộc hộp khác | `400 COMPARTMENT_INVALID`; không tạo lịch | — | `NOT_RUN` | API | P3-02 |
| TC-SCHED-003 | `FD-2`, owner | 1) `timeOfDay=24:00` 2) `daysOfWeek` rỗng/trùng/0/8 3) `endDate < startDate` 4) grace 0/121 5) items rỗng 6) quantity ≤ 0 7) timezone sai | `400 VALIDATION_ERROR` hoặc `SCHEDULE_INVALID_TIMEZONE`; không tạo lịch | — | `NOT_RUN` | API | P3-02 |
| TC-SCHED-004 | Owner verified chưa ghép hộp | 1) POST schedule | `409 DEVICE_NOT_PAIRED`; không tạo lịch | — | `NOT_RUN` | API | P3-02 |
| TC-SCHED-005 | `FD-3`, owner + relative A | 1) owner GET 2) relative GET 3) GET lịch đã xóa mềm 4) relative khác hộp | Owner/relative đúng hộp `200`; lịch xóa mềm không trả; relative hộp khác `403` | — | `NOT_RUN` | API | P3-02 |
| TC-SCHED-006 | `FD-3` đã sinh dose hôm nay | 1) PUT đổi giờ/thuốc/grace 2) GET dose cũ + history | `200`; version tăng; `DoseInstances`/`DoseItemSnapshots` **đã sinh không đổi** (AC-10) | — | `NOT_RUN` | API | P3-02 |
| TC-SCHED-007 | `FD-3`, còn dose `Pending` tương lai | 1) PUT đổi giờ 2) GET dose tương lai 3) GET schedule-sync | Kỳ vọng phụ thuộc **D-22** (giữ/hủy/tái sinh + đồng bộ thiết bị); chưa có quy tắc chốt | — | `BLOCKED` | API | P3-02 |
| TC-SCHED-008 | `FD-3` | 1) DELETE lịch 2) GET lịch 3) GET history 4) DELETE lần 2 | `204`; xóa mềm + version tăng; history/dose giữ nguyên; GET `404`; lần 2 `404`; audit | — | `NOT_RUN` | API | P3-02 |
| TC-SCHED-009 | `FD-2`, đã có lịch 07:00 slot 1 | 1) tạo lịch trùng tuyệt đối 2) tạo lịch chồng lấn một phần | Trùng tuyệt đối bị từ chối (đề xuất); chồng lấn phụ thuộc **D-23** | — | `BLOCKED` | API | P3-02 |

### 6.5. Dose Instance & Snapshot (11)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-DOSE-001 | `FD-2` + lịch có 2 thuốc, grace 15 | 1) chạy job sinh dose 2) đọc `DoseInstances` + `DoseItemSnapshots` | Mỗi dose có snapshot đủ thuốc + `GracePeriodMinutes`; `DeadlineAt = ScheduledAt + grace`; unique `(ScheduleId, ScheduledAt)` | — | `NOT_RUN` | API | P3-03 |
| TC-DOSE-002 | `FD-2`, job đã chạy; sau đó dừng job 2 ngày | 1) chạy job bù 2) chạy lại job 3) đếm dose | Dose thiếu được sinh đúng một lần; chạy lặp không nhân đôi; phạm vi theo horizon (D-11) | — | `NOT_RUN` | API | P3-03 |
| TC-DOSE-003 | `FD-3`, dose 07:00–07:15 slot 1 | 1) gửi `LidOpened` slot 1 lúc 07:05 | `Pending → Confirmed`; `confirmedAt=07:05`; dashboard/history hiển thị "đã xác nhận" | — | `NOT_RUN` | API | P4-03 |
| TC-DOSE-004 | `FD-3` | 1) gửi mở slot 1 lúc 06:59 2) GET dose | Event được lưu; dose **không** `Confirmed`; vẫn `Pending` | — | `NOT_RUN` | API | P4-03 |
| TC-DOSE-005 | `FD-3` | 1) gửi mở slot 2 lúc 07:05 2) GET dose | Event được lưu; dose slot 1 **không** `Confirmed`; không tạo xác nhận sai ngăn (AC-02) | — | `NOT_RUN` | API | P4-03 |
| TC-DOSE-006 | Nắp slot 1 mở sẵn từ 06:50 | 1) 07:00 vẫn mở 2) đóng 07:06 3) mở lại 07:07 4) khởi động lại thiết bị khi nắp đang mở | Không tự xác nhận khi nắp mở sẵn; chỉ cạnh đóng→mở trong cửa sổ xác nhận (AC-03); firmware không sinh cạnh giả khi reboot | — | `NOT_RUN` | Bench | HW-03 |
| TC-DOSE-007 | `FD-3` | 1) mở đúng slot lúc 07:02 2) mở lại lúc 07:05 3) kiểm tra `ConfirmedAt` | Lần đầu xác nhận; lần sau vẫn ghi event nhưng không đổi `ConfirmedAt`/trạng thái | — | `NOT_RUN` | API | P4-03 |
| TC-DOSE-008 | `FD-3` | 1) sửa lịch sau khi dose sinh 2) GET history + dashboard | Dose cũ và snapshot giữ nguyên thuốc/giờ/grace; không đổi ngược lịch sử (AC-10) | — | `NOT_RUN` | API | P3-03 |
| TC-DOSE-009 | `FD-3` | 1) mở đúng slot đúng `ScheduledAt` 2) mở đúng `DeadlineAt` 3) mở trước 1 giây 4) mở sau 1 giây | Hai biên inclusive được xác nhận; trước/sau cửa sổ không xác nhận | — | `NOT_RUN` | API | P3-05 |
| TC-DOSE-010 | `FD-3`, job đánh giá quá hạn | 1) online tại deadline, không mở 2) offline tại deadline, không mở 3) job chạy muộn sau reconnect | Online → `Overdue`; offline → `Unknown`; job muộn **không** đổi kết luận tại deadline | — | `NOT_RUN` | API | P4-04 |
| TC-DOSE-011 | Dose đã `Overdue`/`Unknown` | 1) gửi event mở muộn 2) GET dose/alert | Kỳ vọng phụ thuộc **D-24** (có chuyển `Confirmed`/set `ResolvedAt` hay không); hiện hợp đồng giữ nguyên trạng thái | — | `BLOCKED` | API | P4-03 |

### 6.6. ESP32 Event Sync (12)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-SYNC-001 | `FD-5`, mất Wi‑Fi trước 07:00 | 1) 07:00 thiết bị nhắc bằng RTC 2) mở đúng ngăn 3) reconnect 4) gửi batch | Đèn/còi đúng ngăn 1; event vào queue bền qua reboot; batch được nhận; server ghi 1 event (AC-01, AC-04) | — | `BLOCKED` | Bench | HW-04 |
| TC-SYNC-002 | `FD-2`, mất Wi‑Fi đúng lúc nhắc | 1) ngắt mạng tại 07:00 2) mở nắp trong cửa sổ 3) reconnect trong cửa sổ 4) gửi batch | Nhắc cục bộ vẫn chạy; event gửi sau vẫn ghép đúng dose nếu `occurredAt` trong cửa sổ; `Confirmed` (AC-04) | — | `BLOCKED` | Bench | HW-04 |
| TC-SYNC-003 | `FD-3`, batch gồm 1 event hợp lệ | 1) gửi batch lần 1 2) lần 2 3) lần 3 4) đếm DB/alert | Đúng 1 `DeviceEvents`/`eventId`, 1 `Confirmed`, không thêm alert/recipient; lần 2–3 trả `Duplicate` (AC-05) | — | `NOT_RUN` | API | P4-02 |
| TC-SYNC-004 | `FD-3` | 1) gửi 1 batch chứa cùng `eventId` hai lần | Phần tử đầu `Accepted`; phần tử sau `Duplicate`; response đủ kết quả cho từng phần tử; 1 bản ghi | — | `NOT_RUN` | API | P4-02 |
| TC-SYNC-005 | `FD-3` | 1) gửi batch đảo thứ tự 2 lần với seed khác nhau 2) so sánh trạng thái cuối | Xử lý theo `occurredAt` tăng dần, tie-break `eventId`; kết quả cuối giống nhau | — | `NOT_RUN` | API | P4-03 |
| TC-SYNC-006 | `FD-7`, ngưỡng lệch giờ cấu hình | 1) gửi event `occurredAt` tương lai vượt ngưỡng 2) gửi event cũ trong cửa sổ dose | Tương lai bị `Rejected EVENT_TIME_INVALID`; event cũ vẫn ghi/ghép nếu đúng cửa sổ; ngưỡng theo **D-26** | — | `BLOCKED` | API | P4-02 |
| TC-SYNC-007 | `FD-3` | 1) gửi batch trộn event hợp lệ + slot sai + type sai | Event hợp lệ được ghi; phần tử lỗi `Rejected` kèm code; **không** rollback phần tử hợp lệ; đủ kết quả mỗi `eventId` | — | `NOT_RUN` | API | P4-02 |
| TC-SYNC-008 | `FD-2` | 1) gửi batch với khóa sai 2) gửi khi thiết bị chưa ghép | `401 DEVICE_UNAUTHORIZED` và `409 DEVICE_NOT_PAIRED`; không ghi event | — | `NOT_RUN` | API | P4-02 |
| TC-SYNC-009 | `FD-2` + thiết bị Unpaired | 1) heartbeat 2) lặp heartbeat 3) heartbeat khi Unpaired | `200`; cập nhật `LastSeenAt` kể cả Unpaired; trả `serverTimeUtc`; lặp không tạo dữ liệu nghiệp vụ | — | `NOT_RUN` | API | P2-07 |
| TC-SYNC-010 | `FD-3`, schedule version hiện tại | 1) sync cùng version 2) sync version cũ 3) kiểm tra gói | Cùng version `204`; khác version trả gói đầy đủ, `timeZoneId` từng lịch, loại lịch xóa mềm; lỗi tải giữ lịch cũ (firmware) | — | `NOT_RUN` | API | P3-04 |
| TC-SYNC-011 | `FD-7`, RTC lệch ± | 1) heartbeat nhận `serverTimeUtc` 2) gửi event với `occurredAt` lệch 3) kiểm tra event lưu | Server không viết lại `occurredAt`; event trong cửa sổ vẫn ghép; ngưỡng theo **D-26** | — | `BLOCKED` | Bench | HW-04 |
| TC-SYNC-012 | `FD-5`, queue có event đã gửi trước đó | 1) reconnect gửi batch A 2) gửi lại batch A sau reboot 3) kiểm tra dose/alert | Không nhân đôi event/dose/alert; lịch sử chỉ 1 bản ghi; `Duplicate` cho lần sau | — | `NOT_RUN` | API | P4-02 |

### 6.7. Email Alerts (10)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-ALERT-001 | `FD-3`, online tại deadline, không mở | 1) chạy job đánh giá 2) đọc dose + alert | Dose `Overdue`; có alert `OverdueUnconfirmed`; nội dung "chưa được xác nhận", không "đã uống" | — | `NOT_RUN` | API | P5-02 |
| TC-ALERT-002 | `FD-3`, offline tại deadline | 1) chạy job 2) đọc dose + alert + email | Dose `Unknown`; alert `UnconfirmedOffline`; nội dung "Không thể xác nhận do mất kết nối"; không "quên uống"/"bỏ liều" (AC-06) | — | `NOT_RUN` | API | P5-02 |
| TC-ALERT-003 | `FD-4` (A/B Accepted, C Revoked) | 1) chạy job 2) đếm `AlertRecipients` + sink | Đúng owner + A + B; **không** C; không recipient trùng; snapshot tại thời điểm đánh giá (AC-09) | — | `NOT_RUN` | API | P5-02 |
| TC-ALERT-004 | `FD-4`, giả lập lỗi khi insert recipient | 1) chạy job trong transaction 2) kiểm tra DB | Alert + recipients tạo trọn hoặc không tạo; không có alert thiếu người nhận | — | `NOT_RUN` | API | P5-02 |
| TC-ALERT-005 | `FD-4`, job đã tạo alert | 1) chạy job lần 2 2) chạy 2 job song song | Đúng 1 alert cho `(DoseInstanceId, AlertType)`; không thêm recipient; không gửi lặp | — | `NOT_RUN` | API | P5-02 |
| TC-ALERT-006 | `FD-4`, sink cấu hình 1 địa chỉ lỗi | 1) chạy worker 2) địa chỉ A lỗi, B thành công 3) retry A 4) đọc `EmailDeliveries` + inbox | B nhận đúng 1; A retry độc lập; retry A không gửi lại B; quá N lần → `Exhausted` (D-13) | — | `NOT_RUN` | API | P5-03 |
| TC-ALERT-007 | 1 alert có 2 delivery `Pending` | 1) chạy 2 worker đồng thời 2) đọc `LockedUntil`/attempt | Mỗi delivery được một worker claim; không gửi trùng; `UNIQUE (AlertRecipientId, AttemptNumber)` giữ | — | `NOT_RUN` | API | P5-03 |
| TC-ALERT-008 | `FD-3`, job chạy muộn sau khi thiết bị reconnect | 1) deadline offline 2) reconnect trước khi job chạy 3) chạy job | Kết luận `Unknown` theo `LastSeenAt` tại `DeadlineAt`, không theo trạng thái lúc job chạy (AC-06) | — | `NOT_RUN` | API | P5-02 |
| TC-ALERT-009 | Alert đã tạo, recipient C bị thu hồi trước khi gửi | 1) revoke C 2) chạy worker 3) kiểm tra delivery của C | Kỳ vọng phụ thuộc **D-24** (hiện không tự hủy delivery đã xếp hàng) | — | `BLOCKED` | API | P5-03 |
| TC-ALERT-010 | `FD-4`, alert có owner + A | 1) A mark read 2) A mark read lần 2 3) owner đọc alert 4) outsider mark read | `readAt` của A set một lần; owner không đổi trạng thái đọc của A; outsider `403/404` | — | `NOT_RUN` | API | P5-05 |

### 6.8. Prescription Image Security (8)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-IMG-001 | `FD-2`, owner, `FD-6` ảnh hợp lệ | 1) upload JPEG 2) upload PNG/WebP 3) kiểm tra DB + storage | `201`; metadata đúng; tên lưu trữ ngẫu nhiên; file ngoài `wwwroot`; DB **không** có binary | — | `NOT_RUN` | API | P6-02 |
| TC-IMG-002 | Owner, file 5,242,881 bytes | 1) upload | `413 FILE_TOO_LARGE`; không lưu file/metadata | — | `NOT_RUN` | API | P6-02 |
| TC-IMG-003 | Owner, EXE/PDF đổi tên `.jpg`, `Content-Type: image/jpeg` | 1) upload | `415 FILE_TYPE_NOT_ALLOWED` do magic bytes; không lưu file | — | `NOT_RUN` | API | P6-02 |
| TC-IMG-004 | Owner, file JPEG hỏng (magic đúng, không giải mã được) | 1) upload | `400 IMAGE_INVALID`; không lưu file | — | `NOT_RUN` | API | P6-02 |
| TC-IMG-005 | Owner, JPEG có EXIF GPS + orientation | 1) upload 2) tải lại và đọc metadata | EXIF (kể cả GPS) bị loại; ảnh vẫn hiển thị đúng chiều | — | `NOT_RUN` | API | P6-02 |
| TC-IMG-006 | `FD-4`, ảnh của owner | 1) owner tải content 2) relative/outsider tải 3) thử URL tĩnh 4) anonymous tải | Owner `200` với `Cache-Control: private, no-store`; relative/outsider `403`; anonymous `401`; không có URL công khai (AC-11) | — | `NOT_RUN` | API | P6-03 |
| TC-IMG-007 | Ảnh đã upload | 1) DELETE 2) GET content 3) DELETE lần 2 4) kiểm tra audit | `204`; xóa mềm; content `404`; lần 2 `404`; audit ghi nhận; file xử lý theo D-16 | — | `NOT_RUN` | API | P6-04 |
| TC-IMG-008 | `FD-4`, owner có ảnh | 1) owner list 2) relative list 3) kiểm tra phạm vi | Owner chỉ thấy ảnh hộp mình; relative `403`; không lộ tên file/hộp khác | — | `NOT_RUN` | API | P6-03 |

### 6.9. UI & thuật ngữ (4)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-UI-001 | UI đã dựng | 1) quét toàn bộ chuỗi UI/email mẫu 2) tìm "đã uống" | Không có chuỗi khẳng định "đã uống"; dùng "đã mở/đã xác nhận" | — | `NOT_RUN` | UI | UI-07 |
| TC-UI-002 | Dose `Unknown` | 1) xem dashboard/history/email 2) đọc nhãn trạng thái | Hiển thị "Không thể xác nhận"/Offline; không "quên uống"/"bỏ liều" (AC-06) | — | `NOT_RUN` | UI | UI-04 |
| TC-UI-003 | UI đã dựng | 1) mở 360px 2) mở 1280px 3) kiểm tra bảng/form | Không tràn ngang; nút/form dùng được; nội dung quan trọng không bị che | — | `NOT_RUN` | UI | UI-07 |
| TC-UI-004 | Đăng nhập relative | 1) mở trang lịch/ảnh 2) tìm nút sửa/upload/xóa 3) gọi API trực tiếp | UI ẩn hành động không được phép; API vẫn `403` (defense in depth) | — | `NOT_RUN` | UI | UI-05 |

### 6.10. Smoke (1)

| ID | Preconditions | Steps | Expected | Actual | Status | Auto | Task |
|---|---|---|---|---|---|---|---|
| TC-SMOKE-001 | Build `P0-01` | 1) `dotnet test` 2) chạy Kestrel 3) GET `/` và `/css/site.css` | App khởi động; `/` trả `200 text/html` có "Hộp thuốc thông minh"; CSS `200`; test `Get_Root_ReturnsHtmlPage` pass | `2026-10-29`: build 0W/0E; `Get_Root_ReturnsHtmlPage` 1/1 pass; HTTP 200 HTML + CSS 200 | `PASS` | API | P0-01 |

---

## 7. Điều kiện vào/ra

### 7.1. Entry criteria

- Module tương ứng đã `DONE` theo [TASKS.md](TASKS.md) và migration đã apply.
- `QA-ENV-1` sẵn sàng; fixture `QA-DATA-1` tạo được.
- TBD liên quan đã chốt hoặc test được đánh `BLOCKED` có lý do.

### 7.2. Exit criteria

- 100% test P0/P1 (AUTH, AUTHZ, PAIR) `PASS`.
- Không còn lỗi Critical/High mở.
- 12/12 AC có bằng chứng `PASS`; phần cứng có bench/simulator hoặc ghi rõ chưa kiểm chứng.
- Không có test `BLOCKED` do quyết định nghiệp vụ chưa chốt tại thời điểm sign-off.

---

## 8. Rủi ro kiểm thử

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Chưa có thiết bị/simulator (D-06) | AC-01/03/04/05 không kiểm chứng được | Ưu tiên simulator; ghi rõ test bench-only |
| TBD D-22/D-23/D-24 | Không xác định expected cho sửa lịch, chồng lấn, xác nhận muộn | Chặn DONE; test để `BLOCKED` |
| D-26 chưa chốt | Không test được biên thời gian/RTC | Cấu hình ngưỡng qua `FD-7` sau khi chốt |
| Email provider D-03 | Không kiểm chứng gửi thật | Dev sink cho logic; smoke provider trước sign-off |
| Test DB dùng InMemory | Bỏ sót lỗi unique/check | Bắt buộc SQL Server thật |
| Job chạy song song | Kết quả không tất định | Test 2 instance, claim/lease |
