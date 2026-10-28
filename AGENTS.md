# AGENTS.md — Hộp thuốc thông minh (MVP)

> Phiên bản: 1.2 · Ngày: 2026-10-29 · Trạng thái: **Đang triển khai (P0-01 DONE)**
>
> Bản 1.2 chốt D-01 (Razor trong project Api) và D-02 (cookie session + CSRF); cập nhật cấu trúc `src/` + `tests/`.
>
> Tài liệu này ràng buộc mọi AI Coding Agent và thành viên tham gia codebase.
> Nguồn yêu cầu gốc: `Hop-thuoc-thong-minh.html` (bản đặc tả MVP). Ba tài liệu đi kèm: [PLAN.md](PLAN.md), [TASKS.md](TASKS.md), [API_CONTRACT.md](API_CONTRACT.md).

---

## 1. Vai trò và trách nhiệm của Agent

Bạn là **Senior .NET Engineer + Technical Lead** cho dự án Hộp thuốc thông minh, phụ trách:

| Trách nhiệm | Mô tả |
|---|---|
| Đọc hiểu đặc tả | HTML là nguồn yêu cầu chính. Không suy diễn yêu cầu nghiệp vụ ngoài văn bản. |
| Lập kế hoạch | Chia việc theo [PLAN.md](PLAN.md), cập nhật [TASKS.md](TASKS.md) trước và sau khi làm. |
| Triển khai | Code đúng phạm vi MVP, đúng hợp đồng API, đúng ràng buộc dữ liệu/bảo mật. |
| Kiểm thử | Viết test cho mọi hành vi nghiệp vụ quan trọng; không báo DONE khi chưa có bằng chứng. |
| Báo cáo | Báo cáo tiến độ ngắn gọn, trung thực, có Task ID và bằng chứng. |
| Bảo vệ phạm vi | Từ chối/tách các đề xuất ngoài MVP; ghi nhận vào mục "Sau MVP". |

### 1.1. Thứ tự ưu tiên khi có xung đột tài liệu

1. `Hop-thuoc-thong-minh.html` — yêu cầu nghiệp vụ gốc (bất biến trong MVP).
2. `AGENTS.md` — quy tắc làm việc, bảo mật, phạm vi không được thay đổi.
3. `API_CONTRACT.md` — hợp đồng API đã chốt (được phép điều chỉnh tên endpoint có ghi chú, không được bỏ kiểm tra quyền/chức năng).
4. `PLAN.md` — kế hoạch và thứ tự triển khai.
5. `TASKS.md` — trạng thái thực tế.

Nếu phát hiện mâu thuẫn giữa các tài liệu: **dừng phần việc bị ảnh hưởng**, ghi vào báo cáo, đề xuất cách xử lý; không tự chọn một bên rồi tiếp tục.

### 1.2. Quy ước nhãn trong tài liệu

- **[BẮT BUỘC]** — yêu cầu đã chốt từ HTML, không được thay đổi.
- **[ĐỀ XUẤT KỸ THUẬT]** — lựa chọn kỹ thuật do Agent/tài liệu đề xuất, được phép điều chỉnh nếu không vi phạm [BẮT BUỘC].
- **[TBD]** — chưa xác định; phải giữ nguyên nhãn, không được tự chọn giá trị rồi coi là đã chốt.

---

## 2. Phạm vi MVP bất biến

### 2.1. Sáu quyết định đã chốt — không được thay đổi

1. **Ba ngăn độc lập**: Sáng (slot 1), Trưa (slot 2), Tối (slot 3); mỗi ngăn có đèn và cảm biến mở nắp; đúng giờ chỉ đúng ngăn được nhắc.
2. **Offline vẫn hoạt động**: lịch lưu cục bộ cùng RTC; sự kiện xếp hàng, gửi lại khi có mạng; chống ghi trùng.
3. **Stack thống nhất**: ASP.NET Core Web API + Entity Framework Core + SQL Server; ESP32 giao tiếp REST/JSON.
4. **Một chủ, nhiều người thân**: mỗi tài khoản sở hữu tối đa 1 hộp; mỗi hộp chỉ 1 chủ; người thân chỉ xem.
5. **Ghép hộp bằng QR an toàn**: QR chỉ chứa pairing code dùng một lần; `DeviceApiKey` không bao giờ nằm trong QR; Server chỉ lưu bản băm.
6. **Cảnh báo quá hạn bắt buộc**: email cho chủ và mọi người thân đã chấp nhận; chạy nền, retry riêng, chống gửi trùng.

### 2.2. Nguyên tắc diễn đạt bắt buộc

- Hệ thống **chỉ biết nắp đã được mở**. Mọi UI, email, báo cáo phải dùng **"đã mở/đã xác nhận"**.
- **Tuyệt đối không** khẳng định chắc chắn người dùng "đã uống" thuốc.
- Khi thiếu dữ liệu (thiết bị offline), hiển thị **Unknown / Offline** hoặc "không thể xác nhận do mất kết nối"; không kết luận quên uống.

### 2.3. Ngoài phạm vi MVP — không triển khai

- Ứng dụng di động, push notification, SMS.
- OCR đơn thuốc, tự sinh lịch từ ảnh, xác thực đơn thuốc, tư vấn/chẩn đoán y tế.
- Chia sẻ ảnh đơn thuốc cho người thân; URL công khai cho ảnh.
- Nhiều hộp trên một tài khoản (sở hữu), mô hình tổ chức/cơ sở chăm sóc.
- OTA firmware, thống kê nâng cao, tích hợp hệ thống y tế.

Các ý tưởng trên chỉ được ghi nhận, **không được đưa vào code, schema, API hay UI** của MVP.

---

## 3. Công nghệ và kiến trúc bắt buộc

### 3.1. Stack

| Thành phần | Công nghệ | Ghi chú |
|---|---|---|
| Thiết bị | ESP32 | 3 cảm biến nắp, 3 đèn, còi, RTC, Wi‑Fi, nguồn DC |
| Giao thức thiết bị | REST/JSON qua HTTPS | Xác thực `DeviceId + DeviceApiKey` |
| Backend | ASP.NET Core Web API (.NET 10 — scaffold hiện tại `net10.0`) | Controllers + services + background workers |
| ORM | Entity Framework Core | Migrations có review; **không** dùng `EnsureCreated` cho môi trường thật |
| CSDL | SQL Server | Quan hệ, trạng thái, lịch sử |
| Web UI | Razor/MVC trong `SmartMedBox.Api` | **D-01 đã chốt (2026-10-29)** — một project, cùng origin với API |
| Xác thực người dùng | Cookie session + CSRF | **D-02 đã chốt (2026-10-29)** — không dùng JWT, không có bảng `RefreshTokens` |
| Email | Provider **[TBD-D03]** (SMTP/ dịch vụ) | Trừu tượng hóa `IEmailSender`, có dev sink |
| Lưu file ảnh | Vùng private ngoài CSDL (**[TBD-D04]** local disk/blob) | CSDL chỉ giữ metadata + storage key |

### 3.2. Kiến trúc logic

```
ESP32  ──REST/JSON──▶  ASP.NET Core Web API  ──EF Core──▶  SQL Server
                            ▲      │
                            │      └── Background jobs: sinh dose, đánh giá quá hạn, gửi email, đối chiếu file
                            │
                     Web (chủ hộp / người thân)
```

Quy tắc:

- Server là nguồn sự thật cho nghiệp vụ (sinh lần uống, ghép sự kiện, trạng thái, cảnh báo).
- Thiết bị là nguồn sự thật cho việc nhắc tại chỗ khi offline (lịch cục bộ + RTC + hàng đợi sự kiện).
- Web/API không được suy diễn dữ liệu thiết bị chưa gửi lên.
- Hai loại người gọi API tách biệt: người dùng (Web) và thiết bị (ESP32). Không dùng chung cơ chế xác thực.
- Không đặt business rule trong controller hay UI; đặt trong service miền (dose generation, matching, state, alert).

### 3.3. Quy tắc dữ liệu cốt lõi

- **Append-only**: `DeviceEvents`, `DoseInstances`, `Alerts`, `AlertRecipients`, `EmailDeliveries`, `AuditLogs` — không sửa/xóa vật lý để "đồng bộ theo lịch mới".
- **Snapshot**: khi sinh `DoseInstances` phải sao chép `GracePeriodMinutes` và toàn bộ thuốc vào `DoseItemSnapshots`. Sửa/xóa lịch sau đó **không** được làm đổi lịch sử đã sinh.
- **Idempotency**: `UNIQUE (DeviceId, EventId)`; pairing code dùng một lần; token xác minh/đặt lại mật khẩu dùng một lần; `UNIQUE (AlertId, RecipientUserId, Channel)`; một alert nghiệp vụ cho mỗi `(DoseInstanceId, AlertType)`; `UNIQUE (AlertRecipientId, AttemptNumber)` cho từng lần gửi email.
- **Đánh giá online/offline**: khi một dose hết hạn, dùng trạng thái kết nối **tại `DeadlineAt`** (`LastSeenAt >= DeadlineAt - ngưỡng`), không dùng thời điểm job chạy. `LastSeenAt = NULL` = offline/không thể xác nhận.
- **Thời gian**: mọi thời điểm thực tế lưu `datetimeoffset` chuẩn hóa UTC; lịch giữ `TimeOfDay`, `StartDate/EndDate`, `TimeZoneId` **của từng lịch** để sinh dose đúng giờ địa phương. `ScheduleVersion` là cấp thiết bị và tăng khi tạo/sửa/xóa mềm lịch.
- **Xóa mềm** cho lịch, ảnh, dữ liệu cấu hình (`IsDeleted`/`DeletedAt`); không xóa vật lý sự kiện/lần uống/cảnh báo/log; xóa tài khoản thì ẩn danh theo chính sách.
- **File ngoài CSDL**: `PrescriptionImages` chỉ metadata + storage key.
- **Ràng buộc**: `Devices.OwnerUserId` unique filtered khi khác NULL; `Compartments` unique `(DeviceId, SlotNumber)` + check 1–3; `CareRelationships` unique filtered cặp đang hoạt động; `RelativeInvitations` không có 2 lời mời Pending trùng hộp + email; check constraint cho enum trạng thái.
- **Index bắt buộc**: `DoseInstances(Status, DeadlineAt)`, `DeviceEvents(DeviceId, OccurredAt)`, `EmailDeliveries(Status, NextAttemptAt)`, `MedicationSchedules(DeviceId, IsDeleted)`.

---

## 4. Quy tắc nghiệp vụ Agent phải giữ đúng

### 4.1. Xác nhận mở nắp

- Chỉ chấp nhận cạnh chuyển **đóng → mở** của **đúng ngăn** trong khoảng `[ScheduledAt, DeadlineAt]` (`DeadlineAt = ScheduledAt + GracePeriodMinutes`).
- Nếu có nhiều lần mở hợp lệ, **lần mở hợp lệ đầu tiên** xác nhận; các lần sau vẫn lưu event nhưng không đổi `ConfirmedAt`.
- Mở sai ngăn hoặc mở trước giờ: ghi sự kiện nhưng **không** xác nhận lần uống.
- Nắp đã mở sẵn trước giờ: **không** tự xác nhận; phải đóng rồi mở lại (trách nhiệm phát hiện cạnh nằm ở firmware; Server không được "đoán"). Trạng thái nắp lúc khởi động lại phải được firmware khởi tạo đúng để không sinh cạnh giả.
- Trạng thái lần uống: `Pending` → `Confirmed` | `Overdue` | `Unknown` (xem [API_CONTRACT.md](API_CONTRACT.md) §10.4). `Confirmed` chỉ có nghĩa "nắp đúng ngăn đã mở trong cửa sổ", **không** phải "đã uống".
- Xác nhận muộn sau `Overdue`/`Unknown` và ý nghĩa `ResolvedAt` là **[TBD-D24]**: mặc định **không** đổi trạng thái, không tự set `ResolvedAt`.
- Không thêm trạng thái mới hoặc đổi nghĩa trạng thái khi chưa cập nhật đồng bộ cả 4 tài liệu và được duyệt.

### 4.2. Offline

- Thiết bị vẫn nhắc theo lịch + RTC khi mất Wi‑Fi; sự kiện có `EventId` ổn định, xếp hàng, gửi lại theo lô.
- Server chống trùng bằng `(DeviceId, EventId)`; gửi lại nhiều lần chỉ tạo một kết quả nghiệp vụ. Xử lý event theo `occurredAt` tăng dần, không theo thứ tự mảng.
- Khi hết hạn chờ mà thiết bị không đủ dữ liệu trực tuyến: trạng thái `Unknown`, cảnh báo dùng nội dung "không thể xác nhận do mất kết nối". Kết luận dựa trên kết nối tại `DeadlineAt`, không phải lúc job chạy.
- Đồng bộ lịch có **phiên bản cấp thiết bị** (`ScheduleVersion`); thiết bị chỉ thay lịch cục bộ khi nhận đủ gói hợp lệ và giữ lịch cũ nếu tải/parse lỗi.
- Heartbeat trả `Unpaired`/`Paired`, `serverTimeUtc` (chỉnh RTC) và thông tin đồng bộ cần thiết. Dung sai lệch giờ **[TBD-D26]**; không tự sửa `occurredAt` do thiết bị gửi.
- Wi‑Fi credentials và server base URL được cấu hình theo quy trình **[TBD-D25]**; không tự thêm màn hình cấu hình thiết bị vào Web khi chưa chốt.

### 4.3. Phân quyền

- Chủ hộp: tối đa 1 hộp; toàn quyền lịch/cấu hình, ảnh, mời/thu hồi người thân; nhận email quá hạn (bắt buộc).
- Người thân (chỉ sau khi chấp nhận lời mời): xem lịch/trạng thái/lịch sử/cảnh báo; **không** sửa lịch/cấu hình; **không** xem/tải ảnh đơn; được ngừng theo dõi; nhận email quá hạn (bắt buộc).
- Mọi endpoint phải kiểm tra quyền phía Server theo từng hộp; không tin ID do client gửi.
- Không trả email của người thân này cho người thân khác; chỉ chủ hộp thấy danh sách đầy đủ.
- Không tạo URL công khai cho ảnh hoặc dữ liệu sức khỏe.
- Lời mời và chấp nhận lời mời phải rate-limit; token sai/không tồn tại trả cùng một lỗi để tránh dò.

### 4.4. Cảnh báo email

- Không có tùy chọn tắt email quá hạn. Muốn dừng phải thu hồi/ngừng theo dõi.
- Gửi cho chủ + mọi người thân `Accepted` tại thời điểm đánh giá.
- Chạy nền, không block request; retry độc lập từng người; chống gửi trùng ở mức nghiệp vụ.
- Tạo `Alerts` + `AlertRecipients` trong một transaction; retry chỉ tạo `EmailDeliveries`, không thay đổi người nhận.
- Worker phải claim delivery nguyên tử (lease/`LockedUntil`), an toàn khi nhiều instance; tối đa N lần rồi `Exhausted`.
- Gửi email là at-least-once: crash sau khi provider nhận có thể gửi lặp cho **cùng** một người; tuyệt đối không lặp sang người khác. Dùng `ProviderMessageId`/idempotency key nếu có.
- Nội dung phải phân biệt: quá hạn có dữ liệu trực tuyến vs không thể xác nhận do mất kết nối.

### 4.5. Ghép QR

- Pairing code: một lần, có hạn **[TBD-D08]**, server lưu hash có pepper/HMAC; dùng xong vô hiệu ngay.
- QR **không** chứa `DeviceApiKey`, không chứa thông tin nhạy cảm khác.
- Ghép trong một transaction: kiểm tra người dùng đã xác minh email, chưa sở hữu hộp, thiết bị chưa có chủ, mã hợp lệ → gán chủ + đánh dấu mã đã dùng.
- Endpoint ghép phải rate-limit chống brute-force mã ngắn; mã sai/không tồn tại trả cùng lỗi.
- Thiết bị xác thực bằng `DeviceId + DeviceApiKey`; so sánh hash theo cách constant-time; `DeviceApiKey` cũng lưu hash có pepper.

### 4.6. Ảnh đơn thuốc

- Chỉ chủ hộp upload/xem/xóa; JPEG/PNG/WebP; tối đa 5 MB; kiểm tra **magic bytes**, không chỉ phần mở rộng; loại bỏ EXIF; lưu ngoài CSDL trong vùng private.
- Không OCR, không sinh lịch, không chia sẻ, không URL công khai.

---

## 5. Cấu trúc repository

### 5.1. Hiện trạng

```
Project_1/
├─ Project_1.sln
├─ Project_1/                 # ASP.NET Core MVC scaffold, net10.0
│  ├─ Controllers/HomeController.cs
│  ├─ Views/...
│  ├─ wwwroot/...
│  └─ Program.cs              # AddControllersWithViews
├─ .gitignore
└─ 4 tài liệu kế hoạch (AGENTS.md, PLAN.md, TASKS.md, API_CONTRACT.md)
```

### 5.2. Cấu trúc đích — [ĐỀ XUẤT KỸ THUẬT], chốt tại `P0-01`

```
Project_1/
├─ src/
│  ├─ SmartMedBox.Api/            # Web API + Razor UI (D-01)
│  ├─ SmartMedBox.Domain/         # Entities, enums, invariants
│  └─ SmartMedBox.Infrastructure/ # EF Core, email, file storage, jobs
├─ tests/
│  ├─ SmartMedBox.UnitTests/
│  └─ SmartMedBox.IntegrationTests/
├─ firmware/                       # [TBD-D06] nếu firmware nằm chung repo
├─ docs/requirements/              # [ĐỀ XUẤT] lưu bản sao đặc tả HTML để truy vết
├─ AGENTS.md · PLAN.md · TASKS.md · API_CONTRACT.md
└─ Project_1.sln
```

Quy tắc cấu trúc:

- Không tạo project/thư viện mới ngoài cấu trúc đã chốt ở `P0-01` khi chưa cập nhật PLAN.
- Tên project/namespace dùng tiếng Anh; tên miền (`SmartMedBox`) giữ nhất quán.
- Không commit `bin/`, `obj/`, `.idea/`, `.vs/`, secret, file build.
- Không đổi tên solution/repo nếu chưa được duyệt.

---

## 6. Coding conventions

### 6.1. C#

- `Nullable` bật, `ImplicitUsings` bật; cảnh báo build phải sạch ở mức hợp lý.
- Async toàn tuyến với `CancellationToken`; không `.Result`/`.Wait()`.
- DTO/contract tách khỏi entity; không trả entity trực tiếp ra API.
- Enum nghiệp vụ map sang chuỗi trong DB (`Status nvarchar(30)`), giá trị lưu cố định bằng tiếng Anh; nhãn hiển thị tiếng Việt ở UI.
- Tiền/liều dùng `decimal`; thời gian dùng `DateTimeOffset` (UTC) và `TimeOnly`/`DateOnly` cho lịch.
- Không dùng magic string cho trạng thái/loại cảnh báo — dùng hằng số/enum đã chốt trong [API_CONTRACT.md](API_CONTRACT.md).
- Dependency injection qua constructor; `IOptions<T>` cho cấu hình.
- Một controller/service một trách nhiệm; không đặt business rule trong controller.
- Đặt tên test theo hành vi: `Method_Scenario_ExpectedResult`.

### 6.2. EF Core

- Cấu hình entity bằng `IEntityTypeConfiguration<T>`, không nhồi hết vào `OnModelCreating`.
- Migration đặt tên có nghĩa (`AddDevices`, `AddDoseSnapshots`...); review SQL trước khi apply; không sửa migration đã phát hành.
- Ràng buộc unique/check/filtered index phải nằm trong migration, không chỉ trong code.
- Mọi thay đổi schema song song cập nhật tài liệu ERD (nếu có) và ghi chú trong TASKS.

### 6.3. Git / commit (đề xuất)

- Commit nhỏ, một mục đích; message tiếng Anh dạng `feat(scope): ...`, `fix(scope): ...`, `test(scope): ...`.
- Không commit secret; không force-push lên `main`; không push khi chưa được yêu cầu rõ ràng.
- Mỗi task DONE tham chiếu được commit/tệp test trong TASKS.md.

---

## 7. Bảo mật

- Mật khẩu: hash bằng thuật toán chuẩn (ASP.NET Core `PasswordHasher` hoặc tương đương). Không lưu/không log mật khẩu, token, `DeviceApiKey`, pairing code dạng rõ.
- Token (verify email, reset password, refresh, invitation): lưu **hash**, có hạn, dùng một lần khi thuộc luồng bảo mật.
- `DeviceApiKey`: sinh ngẫu nhiên đủ mạnh, chỉ hiển thị một lần khi provision, server lưu hash, so sánh constant-time.
- QR chỉ chứa pairing code.
- Phân quyền kiểm tra ở **Server** cho mọi endpoint, theo từng hộp; trả 403 thay vì 404 gây lộ tồn tại khi phù hợp.
- Rate limit: lời mời người thân (bắt buộc), endpoint auth (đề xuất), **pair và accept token (bắt buộc)**; trả 429 kèm `Retry-After`. Ngưỡng cụ thể **[TBD-D14]**.
- Cookie session (D-02): `HttpOnly`, `Secure`, `SameSite`, CSRF token cho mọi request ghi. D-01 dùng Razor cùng origin nên không mở CORS; không dùng `Access-Control-Allow-Origin: *` cho endpoint có credential.
- Hash pairing code/token/`DeviceApiKey` bằng HMAC/pepper phía server, không SHA-256 trần; pepper trong secret store.
- `forgot-password`, `resend-verification`, `pair`, `accept` không được tiết lộ tài khoản/mã có tồn tại hay không.
- Backup CSDL + vùng file phải **mã hóa khi lưu và khi truyền**, có test restore định kỳ (HTML §Vận hành).
- File: whitelist MIME thực (magic bytes), giới hạn 5 MB, loại EXIF (kể cả GPS), tên lưu trữ ngẫu nhiên, vùng private, mọi lần đọc qua API có kiểm tra chủ sở hữu.
- HTTPS bắt buộc; HSTS ở môi trường thật.
- Không log PII/nội dung sức khỏe/email body; log có correlation ID.
- Secret chỉ trong user-secrets/env; `appsettings*.json` trong repo không chứa secret.

---

## 8. Xử lý lỗi và API

- Lỗi trả về `ProblemDetails` (RFC 7807) + trường `code` ổn định + `traceId`.
- Mã lỗi, HTTP status, validation: theo [API_CONTRACT.md](API_CONTRACT.md). Không tự thêm mã lỗi mới mà không cập nhật hợp đồng.
- Không trả stack trace/chi tiết nội bộ cho client.
- Validation ở biên hệ thống (request, file, event batch); không thêm validation cho tình huống bất khả thi.
- Mọi thao tác ghi quan trọng ghi `AuditLogs` (ghép, sửa lịch, mời/thu hồi, ảnh) — không ghi secret.
- Background job phải idempotent và an toàn khi chạy lặp/chạy song song.

---

## 9. Kiểm thử và Definition of Done

### 9.1. Bắt buộc có test cho

- Cửa sổ xác nhận: mở đúng ngăn/đúng giờ → `Confirmed`; sớm/sai ngăn → không xác nhận; nhiều lần mở chỉ lần đầu xác nhận.
- Nắp mở sẵn: không tự xác nhận (kiểm thử phía Server với dữ liệu cạnh; firmware kiểm thử bench, gồm cả khởi động lại khi nắp đang mở).
- Sinh dose + snapshot: sửa lịch sau đó không đổi lịch sử; job chạy bù sau downtime không nhân đôi; event offline về muộn vẫn ghép được nếu còn dose.
- Idempotency: gửi lại batch nhiều lần chỉ một kết quả; batch xáo thứ tự cho cùng kết quả; pairing code dùng một lần.
- Offline/Unknown: không sinh kết luận "quên uống"; job chạy muộn sau khi thiết bị reconnect vẫn dùng trạng thái kết nối tại `DeadlineAt`.
- Cảnh báo: đủ người nhận; chạy job song song không nhân đôi; alert + recipients transaction; retry một người không gửi trùng người khác; hai worker không cùng claim delivery.
- Phân quyền: ma trận chủ/người thân cho từng nhóm endpoint; ảnh chỉ chủ; relative không thấy email relative khác.
- Ảnh: sai loại/quá 5 MB bị từ chối; EXIF bị loại; không có binary trong DB; file không truy cập được ngoài API.
- Tài khoản: đăng ký → xác minh → đăng nhập → quên/đặt lại mật khẩu; lời mời cho người chưa có tài khoản → đăng ký → accept.
- Thời gian: timezone từng lịch, DST (nếu có), RTC lệch ±, event tương lai bị từ chối, event cũ vẫn ghép đúng cửa sổ.

### 9.2. Công cụ — [ĐỀ XUẤT KỸ THUẬT]

- xUnit cho unit test; `WebApplicationFactory` cho integration test.
- Test DB dùng SQL Server thật (Testcontainers hoặc instance cục bộ); **không** dùng EF InMemory cho ràng buộc quan hệ/unique.
- Test phần cứng bằng bench + simulator thay thế khi chưa có thiết bị; ghi rõ phần nào chưa kiểm chứng trên thiết bị thật. Simulator phải mô phỏng được: offline queue, gửi lặp batch, RTC lệch, nắp mở sẵn, mất điện.
- Background job phải có test chạy **hai instance song song** (hoặc hai lần trigger đồng thời) cho dose generation, alert và email worker.
- UI: kiểm thử browser thật (desktop + mobile) cho luồng chính; nếu chưa thể, ghi rõ "chưa kiểm chứng UI" thay vì coi là DONE.
- Test backup/restore: phục hồi CSDL + file vào môi trường sạch và xác nhận ảnh đọc được.

### 9.3. Definition of Done của một task

1. Code + migration (nếu có) build sạch.
2. Test tự động cho hành vi mới **pass**; không làm hỏng test cũ.
3. Đúng hợp đồng API và quy tắc bảo mật.
4. Tài liệu liên quan cập nhật (API_CONTRACT/PLAN nếu có thay đổi đã duyệt).
5. `TASKS.md` cập nhật trạng thái + bằng chứng (test name, commit, log).
6. Không có TBD mới bị "âm thầm" chốt; TBD mới được ghi vào báo cáo.

---

## 10. Báo cáo tiến độ

Sau mỗi task (hoặc khi kết thúc một phiên làm việc), báo cáo theo mẫu:

```
[P3-03] Sinh DoseInstances + snapshot — DONE
- Thay đổi: <2–4 gạch đầu dòng>
- Test: <tên test> (x/y pass), lệnh chạy
- Bằng chứng: <commit hash / đường dẫn file test / log>
- TBD mới: <nếu có, kèm mã D-xx>
- Tiếp theo: <task ID>
```

Quy tắc:

- Luôn dẫn Task ID của [PLAN.md](PLAN.md).
- Không mô tả "đã xong" nếu chưa chạy test hoặc chưa có bằng chứng; ghi rõ phần chưa kiểm chứng.
- Cập nhật `TASKS.md` cùng lúc với báo cáo.
- Nếu task chạm nhiều tài liệu, liệt kê các tài liệu đã cập nhật.

---

## 11. Khi bị BLOCKED

Mark task `BLOCKED` trong TASKS.md khi:

- Thiếu quyết định nghiệp vụ/kỹ thuật thuộc TBD mà không thể tiếp tục an toàn.
- Mâu thuẫn giữa HTML và tài liệu.
- Thiếu quyền truy cập/dịch vụ (DB, email provider, thiết bị thật).
- Test fail do nguyên nhân ngoài phạm vi task mà không tự sửa được.

Bắt buộc ghi:

1. Task ID + trạng thái.
2. Triệu chứng: mong đợi vs thực tế (kèm lỗi/log).
3. Đã thử những gì (≥2 hướng khác nhau).
4. Phương án + đánh đổi, cần ai quyết định.
5. Task khác có thể làm tiếp (không bị chặn).

Không được: tự bịa yêu cầu nghiệp vụ để gỡ chặn, giảm phạm vi, tắt test, bỏ kiểm tra quyền, hay commit code nửa vời rồi báo DONE.

---

## 12. Những điều KHÔNG được tự ý thay đổi

- 6 quyết định đã chốt (mục 2.1) và các loại trừ ngoài MVP (mục 2.3).
- Cách diễn đạt "đã mở/đã xác nhận"; cấm "đã uống".
- Ba ngăn cố định (Sáng/Trưa/Tối), slot 1–3.
- Quan hệ 1 chủ/hộp, 1 hộp/tài khoản; quyền của người thân và việc cấm xem ảnh.
- Tính bắt buộc của email quá hạn; retry độc lập; chống trùng.
- Snapshot bất biến; append-only sự kiện/lần uống/cảnh báo/log.
- Cơ chế QR một lần; không lộ `DeviceApiKey`; chỉ lưu hash.
- Chính sách ảnh: 5 MB, JPEG/PNG/WebP, magic bytes, bỏ EXIF, ngoài CSDL, không URL công khai, không OCR/chia sẻ.
- Chống trùng `(DeviceId, EventId)` và `(AlertId, RecipientUserId, Channel)`.
- Dùng `datetimeoffset` UTC + `TimeZoneId` cho lịch.
- Hợp đồng API: không bỏ endpoint/kiểm tra quyền đã có; đổi tên phải cập nhật tài liệu và ghi chú lý do.

Nếu cần thay đổi bất kỳ mục nào: **dừng**, đề xuất, chờ duyệt, cập nhật đồng bộ 4 tài liệu trước khi code.

---

## 13. Checklist trước khi báo DONE

- [ ] Task ID có trong PLAN/TASKS và dependency đã xong.
- [ ] Build + test pass; có bằng chứng cụ thể.
- [ ] Không vi phạm phạm vi MVP hoặc quy tắc bảo mật.
- [ ] Đúng hợp đồng API (status, error code, idempotency).
- [ ] Tài liệu cập nhật nếu có thay đổi được duyệt.
- [ ] TBD mới được ghi nhận, không tự chốt.
- [ ] TASKS.md đã cập nhật trạng thái + bằng chứng.
