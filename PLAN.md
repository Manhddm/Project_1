# PLAN.md — Kế hoạch triển khai MVP Hộp thuốc thông minh

> Phiên bản: 1.2 · Ngày: 2026-10-29 · Nguồn yêu cầu: `Hop-thuoc-thong-minh.html`
>
> Bản 1.2 chốt D-01/D-02 tại P0-01 và cập nhật phạm vi bảng dữ liệu (bỏ `RefreshTokens`).
>
> Tài liệu liên quan: [AGENTS.md](AGENTS.md) · [TASKS.md](TASKS.md) · [API_CONTRACT.md](API_CONTRACT.md)

---

## 1. Nguyên tắc lập kế hoạch

- **Nguồn yêu cầu chính là HTML.** PLAN không thêm nghiệp vụ mới; mọi lựa chọn kỹ thuật ngoài HTML được đánh dấu **[ĐỀ XUẤT KỸ THUẬT]**, mọi khoảng trống được đánh dấu **[TBD]** và gắn mã quyết định `D-xx`.
- Triển khai **theo luồng dọc chạy được** (thiết bị → API → Web), không hoàn thiện từng khối rồi ghép cuối.
- Mỗi task có **một ID duy nhất**, đầu vào/đầu ra rõ, dependency tường minh và tiêu chí hoàn thành kiểm thử được.
- **Không bắt đầu code ứng dụng khi tài liệu chưa được duyệt** (giai đoạn hiện tại chỉ lập kế hoạch).
- Không task nào được DONE nếu thiếu bằng chứng test (xem [TASKS.md](TASKS.md)).

Ký hiệu:

| Nhãn | Ý nghĩa |
|---|---|
| **[BẮT BUỘC]** | Quyết định/yêu cầu đã chốt từ HTML |
| **[ĐỀ XUẤT]** | Lựa chọn kỹ thuật có thể điều chỉnh |
| **[TBD]** | Chưa xác định — cần quyết định trước khi code phần liên quan |

---

## 2. Lộ trình và mốc

<a id="milestones"></a>

| Mốc | Tên | Phạm vi task | Kết quả kiểm chứng |
|---|---|---|---|
| **M1** | Nền móng | `P0-*`, `HW-01` | Repo có cấu trúc chốt, migration đầu, hợp đồng API, sơ đồ khối phần cứng, quản lý secret |
| **M2** | Tài khoản & ghép hộp | `P1-*`, `P2-*`, `UI-01`, `UI-02` | Auth đầy đủ; provision thiết bị; QR một lần; heartbeat `Unpaired/Paired` |
| **M3** | Lịch & nhắc tại chỗ | `P3-*`, `HW-02`–`HW-04`, `UI-03` | CRUD lịch; sinh dose + snapshot; ESP32 sync lịch; RTC + đèn/còi + cảm biến nắp |
| **M4** | Đồng bộ & cảnh báo — **MỐC MVP lõi (theo HTML)** | `P4-*`, `P5-*`, `UI-04`, `UI-05` | Queue offline; event batch idempotent; dashboard; email nền cho chủ + người thân |
| **M5** | Hoàn thiện demo | `P6-*`, `P7-*`, `UI-06`, `UI-07`, `HW-05` | Ảnh đơn thuốc an toàn; test xuyên suốt; log; tài liệu lắp ráp; kịch bản demo |

### 2.1. Định nghĩa MVP hoàn chỉnh

<a id="mvp"></a>

HTML đặt badge **"MỐC MVP"** tại mốc 4 nhưng danh sách 12 tiêu chí nghiệm thu (bao gồm ảnh đơn thuốc) lại là điều kiện "MVP chỉ hoàn thành khi...". Để nhất quán, PLAN định nghĩa:

- **MVP lõi (M4)** = toàn bộ luồng thiết bị → đồng bộ → cảnh báo hoạt động; đạt AC-01…AC-10, AC-12.
- **MVP hoàn chỉnh (sign-off)** = M1–M5 hoàn tất **và đủ 12/12 tiêu chí nghiệm thu** (bao gồm AC-11 ảnh đơn thuốc), demo chạy được theo kịch bản.
- Điểm mâu thuẫn HTML được ghi nhận tại mục 11 (Câu hỏi mở) — nếu chủ dự án chốt khác, cập nhật mục này và `TASKS.md` trước khi code.

---

## 3. Decision log (TBD & đề xuất)

<a id="decision-log"></a>

| Mã | Vấn đề | Trạng thái | Ảnh hưởng task |
|---|---|---|---|
| **D-01** | Web UI dùng Razor/MVC trong project API hay frontend riêng (SPA) | **[ĐÃ CHỐT 2026-10-29]** — Razor/MVC trong `SmartMedBox.Api`, cùng origin, không SPA | P0-01, UI-* |
| **D-02** | Xác thực người dùng: cookie session hay JWT + refresh token | **[ĐÃ CHỐT 2026-10-29]** — cookie session + CSRF; không dùng JWT, bỏ bảng `RefreshTokens` | P1-03 |
| **D-03** | Nhà cung cấp email (SMTP nội bộ / dịch vụ) | **[TBD]** | P0-04, P5-03 |
| **D-04** | Backend lưu file ảnh (local private / blob storage) | **[TBD]** | P6-01 |
| **D-05** | Công cụ provision thiết bị (CLI nội bộ hay endpoint admin) | **[TBD]** — HTML chỉ nói "Server biết..." | P2-02 |
| **D-06** | Firmware ESP32 nằm trong repo này hay repo riêng; ai sở hữu | **[TBD]** | HW-* |
| **D-07** | Định dạng xác thực thiết bị trên HTTP | **[ĐỀ XUẤT]** header `X-Device-Id` + `X-Device-Api-Key` | P2-06 |
| **D-08** | Định dạng/độ dài/hạn dùng pairing code và payload QR | **[TBD]** (đề xuất payload ở API contract) | P2-02, P2-03 |
| **D-09** | Giá trị enum: `DoseStatus`, `AlertType`, `DeviceEventType`, `PairingState` | **[ĐỀ XUẤT]** tại API_CONTRACT §7; cần xác nhận | P0-03 |
| **D-10** | Nguồn `TimeZoneId` cho lịch; một hay nhiều timezone trên một hộp; dashboard "hôm nay" nhóm theo timezone nào | **[TBD]** | P3-01, P3-06 |
| **D-11** | Tầm sinh trước (horizon) và tần suất job sinh dose | **[TBD]** | P3-03 |
| **D-12** | Ngưỡng coi thiết bị offline (dựa trên `LastSeenAt`) | **[TBD]** | P4-04 |
| **D-13** | Chính sách retry email (số lần, backoff) | **[TBD]** | P5-03 |
| **D-14** | Chính sách mật khẩu, TTL token, ngưỡng rate limit, hạn lời mời | **[TBD]** | P1-04, P1-06, P2-09 |
| **D-15** | Xác nhận muộn sau khi đã `Overdue` xử lý thế nào | **[ĐÃ GỘP → D-24]** — giữ mã để truy vết | P3-05, P4-03 |
| **D-16** | Upload ảnh trùng lặp và thời gian lưu file sau xóa mềm | **[TBD]** | P6-02, P6-04 |
| **D-17** | Chính sách ẩn danh hóa khi xóa tài khoản | **[TBD]** — chưa có endpoint trong HTML | P7-06 |
| **D-18** | Hồ sơ người dùng: ngoài email có trường nào và có endpoint cập nhật không | **[TBD]** — HTML chỉ ghi nhóm "Auth & profile" | P1-03 |
| **D-19** | Retention: log ứng dụng, audit log, file ảnh sau xóa mềm | **[TBD]** — HTML nhắc backup/retention nhưng không nêu thời hạn | P6-05, P7-01, P7-04, P7-06 |
| **D-20** | Hình thức quét QR trên Web (camera, tải ảnh, hay nhập mã) | **[TBD]** — HTML chỉ nói "quét QR trên Web" | P2-03, UI-02 |
| **D-21** | Độ bền hàng đợi sự kiện khi mất điện và giới hạn dung lượng queue | **[TBD]** — HTML không quy định | HW-03 |
| **D-22** | Sửa/xóa lịch tác động thế nào đến các `DoseInstances`/snapshot **đã sinh nhưng chưa tới**, trong khi ESP32 đã tải lịch mới | **[TBD]** — HTML cấm đổi lịch sử đã sinh nhưng chưa nói xử lý dose tương lai; chốt trước P3-02 | P3-02, P3-03, P3-04, P4-03 |
| **D-23** | Hai lịch cùng ngăn trùng giờ hoặc chồng lấn khung ngày | **[TBD]** — HTML không quy định; đề xuất chặn trùng tuyệt đối | P3-02, P3-03 |
| **D-24** | Vòng đời cảnh báo & xác nhận muộn: `Overdue`/`Unknown` có được chuyển `Confirmed`; ý nghĩa `ResolvedAt`; người nhận đã tạo alert nhưng bị thu hồi trước khi gửi | **[TBD]** — HTML chỉ nói "quá hạn tạo cảnh báo" | P3-05, P4-03, P5-02 |
| **D-25** | Cách ESP32 nhận cấu hình Wi‑Fi và base URL server khi onboarding | **[TBD]** — HTML nêu Wi‑Fi nhưng không nêu phương thức | HW-01, HW-04, P4-05 |
| **D-26** | Chính sách đồng bộ RTC/clock skew: ngưỡng lệch chấp nhận, tần suất sync, xử lý event cũ/tương lai | **[TBD]** — HTML yêu cầu RTC nhưng không nêu dung sai | HW-02, HW-04, P4-02 |

---

## 4. Tổng quan dependency

```
P0-01 ─▶ P0-02 ─▶ P0-03 ─────────────────────────────┐
   │        │                                        │
   ├─▶ P0-04│                                        │
   └─▶ P0-05│                                        │
            ▼                                        │
          P1-01 ─▶ P1-02 / P1-03 / P1-04 / P1-05 / P1-06
                     │        │
                     ▼        ▼
          P2-01 ─▶ P2-02 ─▶ P2-03 / P2-06 / P2-07 / P2-04 / P2-05
                     │
                     ├─▶ P2-08 ─▶ P2-09 ─▶ P2-10
                     │
                     ▼
          P3-01 ─▶ P3-02 / P3-03 ─▶ P3-04 / P3-05 ─▶ P3-06
                                                      │
          P4-01 ─▶ P4-02 ─▶ P4-03 ─▶ P4-04 ───────────┤
                                                      ▼
          P5-01 ─▶ P5-02 ─▶ P5-03 ─▶ P5-04 / P5-05
          P6-01 ─▶ P6-02 ─▶ P6-03 ─▶ P6-04 / P6-05
                                          │
                                          ▼
          P7-01 / P7-02 / P7-03 / P7-04 / P7-05 / P7-06

HW-01 ─▶ HW-02 ─▶ HW-03 ─▶ HW-04
P2-02/P2-03 ─▶ HW-05
UI-01 ─▶ UI-02 ─▶ UI-03 ─▶ UI-04 ─▶ UI-05 ─▶ UI-06 ─▶ UI-07
```

---

## 5. Phase 0 — Nền móng & hợp đồng

<a id="phase-0"></a>

### <a id="p0-01"></a>P0-01 · Chốt cấu trúc repository & dựng skeleton

- **Mục tiêu:** Chốt D-01, tái cấu trúc scaffold MVC hiện tại thành cấu trúc phục vụ Web API + Web UI; build xanh.
- **Đầu vào:** HTML §Kiến trúc; scaffold `Project_1` (net10.0, MVC); [AGENTS.md](AGENTS.md).
- **Đầu ra:** Ghi quyết định D-01 vào decision log; cấu trúc `src/` + `tests/` (theo AGENTS §5.2) hoặc lý do giữ nguyên; README hướng dẫn chạy; `dotnet build` pass.
- **Phụ thuộc:** —
- **Acceptance / DoD:** Build pass trên máy sạch; không còn code scaffold không dùng; cấu trúc khớp tài liệu; không commit `bin/obj`.
- **Ghi chú:** [ĐỀ XUẤT] Tách `Api`, `Domain`, `Infrastructure`; test project riêng. Không đổi tên repo.

### <a id="p0-02"></a>P0-02 · Mô hình dữ liệu & migration đầu tiên

- **Mục tiêu:** Hiện thực hóa toàn bộ schema trong HTML §CSDL thành entity + EF Core migration.
- **Đầu vào:** HTML bảng/ERD; D-09 sơ bộ.
- **Đầu ra:** Entities cho 17 bảng (18 bảng HTML trừ `RefreshTokens` — D-02 dùng cookie); `DbContext`; migration `InitialSchema`; unique/check/filtered index; quan hệ 1–n, n–n; index đường đọc nóng đúng HTML: `DoseInstances(Status, DeadlineAt)`, `DeviceEvents(DeviceId, OccurredAt)`, `EmailDeliveries(Status, NextAttemptAt)`, `MedicationSchedules(DeviceId, IsDeleted)`; tài liệu ERD đối chiếu.
- **Phụ thuộc:** P0-01.
- **Acceptance / DoD:** Migration apply lên SQL Server trống thành công; test kiểm tra unique `(DeviceId, SlotNumber)`, check slot 1–3, unique `(DeviceId, EventId)`, `(AlertId, RecipientUserId, Channel)`, filtered index owner; đủ 4 index đường đọc nóng; `Devices.LastSeenAt` cho phép NULL; không lưu binary ảnh trong DB.
- **Ghi chú:** [ĐỀ XUẤT] Snapshot schema bằng file SQL trong PR review; không dùng `EnsureCreated`. Kiểm tra thêm check constraint cho enum `nvarchar` (DoseStatus, AlertType, EventType, trạng thái lời mời/quan hệ) để dữ liệu sai không lọt vào DB.

### <a id="p0-03"></a>P0-03 · Chốt hợp đồng API & enum dùng chung

- **Mục tiêu:** Loại bỏ TBD trong [API_CONTRACT.md](API_CONTRACT.md) ở mức đủ để code (D-07, D-08, D-09); chốt format lỗi và enum.
- **Đầu vào:** HTML §API; P0-02; API_CONTRACT bản nháp.
- **Đầu ra:** API_CONTRACT cập nhật trạng thái "chốt cho phase 1–2"; enum `DoseStatus`, `AlertType`, `DeviceEventType`, `PairingState`; catalog error code; quy ước ProblemDetails.
- **Phụ thuộc:** P0-02.
- **Acceptance / DoD:** Không endpoint nào ngoài hợp đồng; enum khớp schema `nvarchar`; review chéo với PLAN; TBD còn lại được giữ nhãn.
- **Ghi chú:** Đổi tên endpoint được phép nhưng phải cập nhật cả 4 tài liệu (HTML cho phép tên gợi ý).

### <a id="p0-04"></a>P0-04 · Hạ tầng cắt ngang

- **Mục tiêu:** Chuẩn bị các thành phần dùng chung trước khi làm tính năng.
- **Đầu vào:** P0-01; D-03; D-04.
- **Đầu ra:** Middleware `ProblemDetails` + `traceId`; `IClock` (UTC) có fake cho test; `IEmailSender` (dev sink ghi file/inbox test); `IFileStorage` (thư mục private); cấu hình logging; health check [ĐỀ XUẤT].
- **Phụ thuộc:** P0-01.
- **Acceptance / DoD:** Test integration xác nhận shape lỗi; test clock/timezone; email dev sink nhận được thư; không log secret.
- **Ghi chú:** Không thêm thư viện ngoài nhu cầu; provider email chốt sau (D-03).

### <a id="p0-05"></a>P0-05 · Quản lý secret & vệ sinh repo

- **Mục tiêu:** Đảm bảo repo không chứa secret và chạy được từ clone sạch.
- **Đầu vào:** P0-01; [AGENTS.md](AGENTS.md) §7.
- **Đầu ra:** Hướng dẫn user-secrets/env; `appsettings.Development.sample.json`; rà `.gitignore`; README bước chạy; quét secret.
- **Phụ thuộc:** P0-01.
- **Acceptance / DoD:** Không có secret/chuỗi kết nối thật trong git; clone mới + đọc README chạy được API; DB connection lấy từ env/user-secrets.
- **Ghi chú:** [ĐỀ XUẤT] Lưu bản sao HTML vào `docs/requirements/` để truy vết.

---

## 6. Phase 1 — Tài khoản & phiên đăng nhập

<a id="phase-1"></a>

### <a id="p1-01"></a>P1-01 · Entity người dùng & hash mật khẩu

- **Mục tiêu:** Có mô hình tài khoản đúng schema, email unique chuẩn hóa.
- **Đầu vào:** P0-02; HTML bảng `ApplicationUsers`.
- **Đầu ra:** Entity + cấu hình EF; hash mật khẩu; migration; chuẩn hóa email (trim/lowercase).
- **Phụ thuộc:** P0-02.
- **Acceptance / DoD:** Test trùng email (khác hoa/thường) bị chặn; hash verify đúng; không lưu mật khẩu rõ.
- **Ghi chú:** [ĐỀ XUẤT] Dùng `PasswordHasher<ApplicationUser>` của ASP.NET Core, không cần full Identity.

### <a id="p1-02"></a>P1-02 · Đăng ký & xác minh email

- **Mục tiêu:** Hoàn tất đăng ký email/mật khẩu và xác minh email.
- **Đầu vào:** P1-01; P0-04; API_CONTRACT §Auth.
- **Đầu ra:** `POST /api/auth/register`, `POST /api/auth/verify-email`, [ĐỀ XUẤT] `POST /api/auth/resend-verification`; token hash + hạn dùng; email template xác minh.
- **Phụ thuộc:** P1-01, P0-04.
- **Acceptance / DoD:** Test đăng ký → bắt mail → xác minh → `EmailVerified=true`; email trùng 409; token dùng lại bị từ chối; không lộ việc email đã tồn tại qua thông báo phụ.
- **Ghi chú:** [TBD-D14] TTL token xác minh; chính sách mật khẩu.

### <a id="p1-03"></a>P1-03 · Đăng nhập, đăng xuất, hồ sơ

- **Mục tiêu:** Phiên đăng nhập hoạt động theo D-02; chặn đăng nhập khi email chưa xác minh.
- **Đầu vào:** P1-01; P0-03; API_CONTRACT §Auth.
- **Đầu ra:** `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/users/me`; cookie phiên + CSRF; gating email verified.
- **Phụ thuộc:** P1-01, P0-03.
- **Acceptance / DoD:** Test sai mật khẩu 401; chưa xác minh 403; cookie hết hạn bị từ chối; logout thu hồi phiên và xóa cookie; request ghi thiếu CSRF bị 400.
- **Ghi chú:** **D-02 đã chốt**: cookie session + CSRF, không có endpoint refresh/JWT. Không trả `PasswordHash` ra API.

### <a id="p1-04"></a>P1-04 · Quên & đặt lại mật khẩu

- **Mục tiêu:** Trọn luồng khôi phục tài khoản, không liệt kê tài khoản.
- **Đầu vào:** P1-01, P0-04; API_CONTRACT §Auth.
- **Đầu ra:** `POST /api/auth/forgot-password`, `POST /api/auth/reset-password`; token hash một lần; thu hồi phiên cũ sau đổi mật khẩu.
- **Phụ thuộc:** P1-01, P0-04.
- **Acceptance / DoD:** Test email lạ vẫn trả 202; token dùng lại/hết hạn bị từ chối; đăng nhập bằng mật khẩu mới OK.
- **Ghi chú:** [TBD-D14] TTL token đặt lại.

### <a id="p1-05"></a>P1-05 · Phân quyền theo từng hộp (owner / relative)

- **Mục tiêu:** Xây dựng lớp kiểm tra quyền dùng chung cho mọi endpoint theo hộp.
- **Đầu vào:** P1-03; HTML §Phân quyền; API_CONTRACT §9.
- **Đầu ra:** Resolver xác định vai trò (Owner / Accepted Relative / None) theo `deviceId`; policy/authorization handler; ma trận quyền; test matrix.
- **Phụ thuộc:** P1-03.
- **Acceptance / DoD:** Test ma trận: owner được sửa lịch; relative bị 403 khi sửa; relative xem được dashboard; người ngoài 403/404; không tin `userId` do client gửi.
- **Ghi chú:** Owner/None hoàn tất ngay; nhánh Relative chỉ kiểm chứng đầy đủ sau P2-08 (`CareRelationships`). Không chặn Phase 2; P2-04 chỉ dùng nhánh Owner. Owner luôn bắt buộc cho ảnh.

### <a id="p1-06"></a>P1-06 · Rate limiting & chống lạm dụng

- **Mục tiêu:** Bảo vệ endpoint auth và lời mời khỏi spam/brute force.
- **Đầu vào:** P1-03; HTML "rate-limit chống spam" cho lời mời.
- **Đầu ra:** Rate limiter cho auth [ĐỀ XUẤT] và invitation [BẮT BUỘC]; phản hồi 429 + `Retry-After`; cấu hình ngưỡng.
- **Phụ thuộc:** P1-03.
- **Acceptance / DoD:** Test vượt ngưỡng trả 429; không ảnh hưởng endpoint khác; ngưỡng cấu hình được.
- **Ghi chú:** [TBD-D14] Ngưỡng cụ thể. Invitation limiter nối vào P2-09.

---

## 7. Phase 2 — Thiết bị, ghép QR, người thân

<a id="phase-2"></a>

### <a id="p2-01"></a>P2-01 · Entity Devices / DevicePairingCodes / Compartments

- **Mục tiêu:** Có danh tính thiết bị, mã ghép hash, 3 ngăn cố định.
- **Đầu vào:** P0-02; HTML §CSDL.
- **Đầu ra:** Entities + migration; filtered unique owner; unique `(DeviceId, SlotNumber)`; check 1–3; logic provision seed 3 ngăn trong transaction.
- **Phụ thuộc:** P0-02.
- **Acceptance / DoD:** Test: mỗi thiết bị đúng 3 ngăn; không 2 chủ; hộp chưa ghép `OwnerUserId` NULL hợp lệ; chặn slot ngoài 1–3.
- **Ghi chú:** `DeviceApiKeyHash` không bao giờ trả ra API.

### <a id="p2-02"></a>P2-02 · Quy trình provision & sinh khóa thiết bị

- **Mục tiêu:** Có cách tạo thiết bị với `DeviceApiKey` và pairing code an toàn.
- **Đầu vào:** P2-01, P0-05; D-05; D-08.
- **Đầu ra:** Công cụ provision (CLI/endpoint theo D-05); sinh `DeviceApiKey` ngẫu nhiên (hiển thị một lần), lưu hash; sinh pairing code + hạn (hash); audit.
- **Phụ thuộc:** P2-01, P0-05.
- **Acceptance / DoD:** Không log/lưu khóa rõ; test provision tạo đủ 3 ngăn; pairing code hết hạn bị từ chối; không thể lấy lại khóa.
- **Ghi chú:** [TBD-D05, D-08] Định dạng mã, hạn dùng, quy trình nhà máy.

### <a id="p2-03"></a>P2-03 · Payload QR & in nhãn

- **Mục tiêu:** QR chỉ chứa pairing code dùng một lần.
- **Đầu vào:** P2-02; API_CONTRACT §Pairing.
- **Đầu ra:** Đặc tả payload QR; generator tạo QR; mẫu nhãn; tài liệu quy trình in.
- **Phụ thuộc:** P2-02.
- **Acceptance / DoD:** Giải mã QR chỉ thấy pairing code + phiên bản; tuyệt đối không có `DeviceApiKey`, serial nhạy cảm hay URL chứa token; test scan mẫu.
- **Ghi chú:** [ĐỀ XUẤT] Payload: `{ "v":1, "code":"<code>" }` hoặc URI nội bộ; chờ D-08.

### <a id="p2-04"></a>P2-04 · API ghép hộp (atomic, một lần)

- **Mục tiêu:** Ghép quyền sở hữu trong một transaction, vô hiệu mã ngay.
- **Đầu vào:** P2-01–P2-03; P1-02; P1-05; API_CONTRACT.
- **Đầu ra:** `POST /api/devices/pair`; kiểm tra mã tồn tại/chưa dùng/chưa hết hạn; user đã xác minh email; user chưa sở hữu hộp; thiết bị chưa có chủ; gán owner + `UsedAt` + `UsedByUserId`; audit.
- **Phụ thuộc:** P2-01, P2-02, P1-02, P1-05.
- **Acceptance / DoD:** Test thành công; quét lại 409; mã hết hạn 410; user đã có hộp 409; thiết bị đã có chủ 409; hai request đồng thời chỉ một thắng; response không chứa khóa thiết bị; brute-force mã bị rate-limit.
- **Ghi chú:** Dùng transaction + điều kiện cập nhật (concurrency token) để chống race. API ghép **không** phụ thuộc P2-03: QR chỉ là cách chuyển mã, không phải điều kiện nghiệp vụ. Pairing code lưu hash có pepper/HMAC (không chỉ SHA-256 trần) vì mã ngắn dễ bị dò offline.

### <a id="p2-05"></a>P2-05 · API trạng thái hộp của tôi

- **Mục tiêu:** Chủ hộp xem thiết bị, ngăn, kết nối, phiên bản lịch.
- **Đầu vào:** P2-04; API_CONTRACT §Devices.
- **Đầu ra:** `GET /api/devices/mine` trả device + 3 compartments + `isOnline`/`lastSeenAt` + `scheduleVersion`; 404 nếu chưa ghép.
- **Phụ thuộc:** P2-04.
- **Acceptance / DoD:** Test chủ thấy hộp mình; người khác không thấy; response không lộ hash/khóa.
- **Ghi chú:** Người thân dùng dashboard theo `deviceId`, không dùng endpoint này.

### <a id="p2-06"></a>P2-06 · Xác thực thiết bị

- **Mục tiêu:** Bảo vệ endpoint ESP32 bằng `DeviceId + DeviceApiKey`.
- **Đầu vào:** P2-02; D-07; API_CONTRACT §Auth thiết bị.
- **Đầu ra:** Authentication handler/scheme riêng; so sánh hash constant-time; mã lỗi 401 thống nhất; tách biệt hoàn toàn với auth người dùng.
- **Phụ thuộc:** P2-02.
- **Acceptance / DoD:** Test khóa sai/bị thiếu 401; khóa đúng OK; không thể dùng token người dùng gọi endpoint thiết bị và ngược lại; không log khóa.
- **Ghi chú:** [TBD-D07] Header vs bearer. Không hỗ trợ xoay khóa trong MVP (không có trong HTML) — ghi nhận ngoài phạm vi.

### <a id="p2-07"></a>P2-07 · Heartbeat Unpaired/Paired

- **Mục tiêu:** Thiết bị biết trạng thái ghép và thông tin đồng bộ.
- **Đầu vào:** P2-06; P2-05; API_CONTRACT §Device.
- **Đầu ra:** `POST /api/device/heartbeat` trả `pairingState`, `serverTimeUtc`, `scheduleVersion`, [ĐỀ XUẤT] `pendingHint`; cập nhật `LastSeenAt`; hoạt động cả khi Unpaired.
- **Phụ thuộc:** P2-06.
- **Acceptance / DoD:** Test Unpaired/Paired; `LastSeenAt` tăng; không trả dữ liệu lịch khi Unpaired (hoặc rỗng theo hợp đồng); idempotent.
- **Ghi chú:** `LastSeenAt` dùng cho trạng thái online/offline (D-12).

### <a id="p2-08"></a>P2-08 · Entity CareRelationships / RelativeInvitations

- **Mục tiêu:** Mô hình lời mời và quyền theo dõi đã chấp nhận.
- **Đầu vào:** P1-01, P2-01; HTML §CSDL.
- **Đầu ra:** Entities + migration; filtered unique cặp `(DeviceId, RelativeUserId)` đang hoạt động; filtered unique lời mời Pending theo hộp + email chuẩn hóa; `TokenHash`; trạng thái.
- **Phụ thuộc:** P2-01, P1-01.
- **Acceptance / DoD:** Test chặn 2 lời mời Pending trùng; chặn 2 quan hệ active trùng; `ExpiresAt` bắt buộc; revoke giữ lịch sử.
- **Ghi chú:** [TBD-D14] Hạn lời mời.

### <a id="p2-09"></a>P2-09 · API lời mời người thân

- **Mục tiêu:** Mời / liệt kê / thu hồi / chấp nhận lời mời.
- **Đầu vào:** P2-08; P1-06; P0-04; API_CONTRACT §Relatives.
- **Đầu ra:** `POST /api/relatives/invitations`, `GET /api/relatives/invitations` [ĐỀ XUẤT], `POST /api/relatives/invitations/{id}/revoke`, `POST /api/relatives/invitations/accept`; email mời; rate limit; kiểm tra email người nhận khớp; audit.
- **Phụ thuộc:** P2-08, P1-06, P0-04.
- **Acceptance / DoD:** Test chủ mời OK; người thân không mời được 403; trùng Pending 409; chấp nhận sai email 403; token hết hạn 410; dùng lại token bị từ chối; quá tần suất 429.
- **Ghi chú:** Chỉ chủ hộp mời/thu hồi. Người nhận phải đăng nhập bằng đúng email được mời. Email mời chứa token một lần; người chưa có tài khoản đăng ký + xác minh đúng email rồi mới `accept`; hệ thống **không** tự tạo tài khoản từ lời mời.

### <a id="p2-10"></a>P2-10 · API quan hệ & ngừng theo dõi

- **Mục tiêu:** Liệt kê quan hệ; chủ thu hồi hoặc người thân tự ngừng theo dõi.
- **Đầu vào:** P2-09; P1-05; API_CONTRACT §Relatives.
- **Đầu ra:** `GET /api/relatives/relationships`, `DELETE /api/relatives/relationships/{id}`; set `RevokedAt`; hiệu lực tức thì.
- **Phụ thuộc:** P2-09, P1-05.
- **Acceptance / DoD:** Test owner thu hồi được; relative tự rời được; sau thu hồi relative mất quyền xem ngay; người ngoài không thao tác được.
- **Ghi chú:** Email quá hạn chỉ gửi cho quan hệ `Accepted` tại thời điểm đánh giá.

---

## 8. Phase 3 — Lịch, lần uống, snapshot

<a id="phase-3"></a>

### <a id="p3-01"></a>P3-01 · Entity lịch & thuốc

- **Mục tiêu:** Hiện thực quy tắc lặp + danh sách thuốc đúng schema.
- **Đầu vào:** P2-01; HTML §CSDL, §Lịch uống.
- **Đầu ra:** `MedicationSchedules`, `ScheduleDays` (PK kép), `ScheduleItems`; check ngày 1–7; grace 1–120; `StartDate`/`EndDate`; `TimeZoneId`; migration.
- **Phụ thuộc:** P2-01.
- **Acceptance / DoD:** Test composite PK; check grace ngoài 1–120 bị chặn; ít nhất một ngày; `EndDate >= StartDate`; xóa mềm.
- **Ghi chú:** [TBD-D10] Nguồn timezone; danh sách timezone hợp lệ; một hay nhiều timezone trên một hộp. Nếu nhiều, dashboard phải nhóm "hôm nay" theo từng lịch/dose chứ không dùng một timezone chung.

### <a id="p3-02"></a>P3-02 · CRUD lịch & ScheduleVersion

- **Mục tiêu:** Chủ hộp quản lý lịch; mỗi thay đổi tăng phiên bản.
- **Đầu vào:** P3-01; P1-05; API_CONTRACT §Schedules.
- **Đầu ra:** `GET /api/schedules`, `GET /api/schedules/{id}`, `POST`, `PUT`, `DELETE` (xóa mềm); chỉ chủ; tăng `ScheduleVersion` trong transaction; audit; validation đầy đủ; chặn lịch trùng tuyệt đối (cùng ngăn + giờ + khung ngày) [ĐỀ XUẤT]; không sửa dose/snapshot đã sinh.
- **Phụ thuộc:** P3-01, P1-05.
- **Acceptance / DoD:** Test relative 403 cho POST/PUT/DELETE; owner OK; version tăng mỗi thay đổi (kể cả DELETE); xóa mềm không mất lịch sử; validate ngăn thuộc đúng thiết bị; PUT/DELETE không làm đổi dose/snapshot đã sinh.
- **Ghi chú:** [ĐỀ XUẤT] Hỗ trợ `If-Match`/`expectedVersion` để chống ghi đè. **[TBD-D22]** Chưa chốt xử lý các `DoseInstances` `Pending` đã sinh trong tương lai khi sửa/xóa lịch — không tự hủy/tái sinh. **[TBD-D23]** Chưa chốt lịch chồng lấn cùng ngăn.

### <a id="p3-03"></a>P3-03 · Sinh DoseInstances + snapshot

- **Mục tiêu:** Sinh lần uống từ lịch theo timezone, bất biến sau khi sinh.
- **Đầu vào:** P3-01; P0-04; HTML §Snapshot; D-11.
- **Đầu ra:** Background job sinh `DoseInstances` (`ScheduledAt`, `DeadlineAt = +grace`, `Status=Pending`) + `DoseItemSnapshots` (copy thuốc + grace); sinh bù các mốc bị lỡ trong horizon khi job downtime; chống sinh trùng; horizon/cadence theo D-11.
- **Phụ thuộc:** P3-01, P0-04.
- **Acceptance / DoD:** Test: đúng ngày trong tuần, đúng timezone; không sinh trùng khi job chạy lặp/song song; chạy bù sau downtime không nhân đôi; sửa lịch sau đó không đổi dose/snapshot đã sinh (AC-10); `DeadlineAt` đúng.
- **Ghi chú:** [TBD-D11] Sinh trước bao nhiêu ngày, chạy bao lâu/lần. Horizon phải đủ dài so với thời gian offline tối đa hỗ trợ, nếu không event offline về muộn sẽ không có dose để ghép. [ĐỀ XUẤT] Unique `(ScheduleId, ScheduledAt)` để chống trùng. **[TBD-D22]** Không tự quyết định hủy/tái sinh dose `Pending` tương lai khi lịch đổi.

### <a id="p3-04"></a>P3-04 · Đồng bộ lịch cho thiết bị

- **Mục tiêu:** ESP32 tải lịch theo phiên bản, nhận gói đầy đủ.
- **Đầu vào:** P3-01, P3-02, P2-07; API_CONTRACT §Device.
- **Đầu ra:** `GET /api/device/schedule-sync?version=`; trả gói đầy đủ khi version cũ hơn; 204 khi không đổi; chỉ thiết bị đã ghép; payload đủ để chạy offline, **`timeZoneId` nằm trong từng lịch**, kèm `serverTimeUtc` để đồng bộ RTC; version tăng khi tạo/sửa/xóa mềm lịch.
- **Phụ thuộc:** P3-01, P3-02, P2-07.
- **Acceptance / DoD:** Test đúng/không đổi version; Unpaired bị từ chối; payload chứa giờ, ngày, grace, items, timezone từng lịch; xóa lịch làm version tăng; không lộ dữ liệu hộp khác; tải lỗi giữa chừng không ghi đè lịch tốt.
- **Ghi chú:** Thiết bị chỉ thay lịch cục bộ khi áp dụng trọn gói (firmware HW-04). **[TBD-D22]** Khi sửa lịch, gói đồng bộ mới phải nhất quán với các dose server sẽ dùng để ghép; chốt D-22 trước khi hoàn tất task này.

### <a id="p3-05"></a>P3-05 · Máy trạng thái lần uống

- **Mục tiêu:** Tập trung mọi chuyển trạng thái vào một service duy nhất.
- **Đầu vào:** P3-03; HTML §Trạng thái.
- **Đầu ra:** Service chuyển `Pending → Confirmed | Overdue | Unknown`; quy tắc dựa trên event + deadline + kết nối **tại thời điểm `DeadlineAt`**; `Confirmed` là trạng thái cuối; `Overdue`/`Unknown` chưa có chuyển tiếp cho tới khi D-24 chốt; không nơi khác ghi trực tiếp `Status`.
- **Phụ thuộc:** P3-03.
- **Acceptance / DoD:** Unit test từng nhánh; test hết hạn có/không có kết nối; test mở đúng cửa sổ; không cho chuyển trạng thái không hợp lệ; job chạy muộn vẫn đánh giá theo trạng thái kết nối tại `DeadlineAt`, không theo lúc job chạy.
- **Ghi chú:** **[TBD-D24]** Xác nhận muộn sau `Overdue`/`Unknown` và ý nghĩa `ResolvedAt` — cần chốt trước P4-03. Không tự thêm trạng thái mới.

### <a id="p3-06"></a>P3-06 · API dashboard & lịch sử

- **Mục tiêu:** Web hiển thị đúng trạng thái cho owner/relative, không suy diễn.
- **Đầu vào:** P3-05, P1-05; API_CONTRACT §Dashboard.
- **Đầu ra:** `GET /api/dashboard`, `GET /api/dose-history`; lọc theo hộp/quyền; trả snapshot; phân trang; `Unknown` khi thiếu dữ liệu.
- **Phụ thuộc:** P3-05, P1-05.
- **Acceptance / DoD:** Test owner/relative xem được; người ngoài 403; lịch sử không đổi khi sửa lịch; không tồn tại field "đã uống"; phân trang đúng.
- **Ghi chú:** Dashboard gộp trạng thái kết nối thiết bị (D-12).

---

## 9. Phase 4 — Sự kiện thiết bị & đồng bộ offline

<a id="phase-4"></a>

### <a id="p4-01"></a>P4-01 · Entity DeviceEvents

- **Mục tiêu:** Lưu sự kiện vật lý append-only, chống trùng theo khóa nghiệp vụ.
- **Đầu vào:** P2-01; HTML §CSDL.
- **Đầu ra:** Entity + migration; `UNIQUE (DeviceId, EventId)`; FK compartment; `MatchedDoseInstanceId` NULL; index `(DeviceId, OccurredAt)`.
- **Phụ thuộc:** P2-01.
- **Acceptance / DoD:** Test insert trùng bị chặn; không update/xóa sự kiện qua API; `ReceivedAt` server-side.
- **Ghi chú:** [TBD-D09] Giá trị `EventType` (đề xuất `LidOpened`, `LidClosed`).

### <a id="p4-02"></a>P4-02 · API nhận batch sự kiện (idempotent)

- **Mục tiêu:** Nhận lô sự kiện từ ESP32, trả kết quả từng sự kiện, chống trùng.
- **Đầu vào:** P4-01, P2-06; P0-03.
- **Đầu ra:** `POST /api/device/events/batch`; validate slot thuộc thiết bị, `occurredAt` hợp lệ; trả `Accepted/Duplicate/Rejected` từng event; xử lý event hợp lệ ngay cả khi phần tử khác bị `Rejected`; transaction; audit [ĐỀ XUẤT]; kiểm tra lệch giờ theo D-26.
- **Phụ thuộc:** P4-01, P2-06.
- **Acceptance / DoD:** Gửi lại cùng batch 3 lần → chỉ một hiệu ứng nghiệp vụ (AC-05); batch lẫn hợp lệ/không hợp lệ xử lý đúng; batch bị xáo thứ tự vẫn cho cùng kết quả; thiết bị khác không ghi được vào hộp này; 401 khi khóa sai; event tương lai vượt ngưỡng bị `Rejected`.
- **Ghi chú:** HTTP 200 với kết quả từng phần tử (không dùng 207) — chi tiết API_CONTRACT. **[TBD-D26]** Ngưỡng lệch giờ và chính sách event quá cũ; trước khi chốt, không tự "sửa" `occurredAt`.

### <a id="p4-03"></a>P4-03 · Ghép sự kiện với lần uống

- **Mục tiêu:** Xác nhận đúng cửa sổ, đúng ngăn; sai/không đủ điều kiện không xác nhận.
- **Đầu vào:** P4-02, P3-05; HTML §Luồng.
- **Đầu ra:** Matching service: xử lý event theo thứ tự `occurredAt` rồi `eventId`; tìm dose cùng thiết bị/ngăn trong `[ScheduledAt, DeadlineAt]`; **lần mở hợp lệ đầu tiên** xác nhận (các lần sau vẫn lưu event); gắn `MatchedDoseInstanceId`; `Confirmed` + `ConfirmedAt`; event ngoài cửa sổ/sai ngăn chỉ lưu; xử lý event trùng; concurrency-safe.
- **Phụ thuộc:** P4-02, P3-05.
- **Acceptance / DoD:** Test AC-02 (đúng cửa sổ), AC-03 (mở sớm/sai ngăn không xác nhận); 2 event mở cùng lúc/khác thứ tự không tạo 2 lần xác nhận; batch trộn online/offline cho cùng kết quả; event quá hạn xử lý theo D-24.
- **Ghi chú:** Server không tự tạo "cạnh" từ trạng thái nắp đang mở; firmware chịu trách nhiệm. Việc xác nhận muộn sau `Overdue`/`Unknown` **chưa được quyết định** (D-24) — mặc định không đổi trạng thái.

### <a id="p4-04"></a>P4-04 · Trạng thái kết nối & Unknown

- **Mục tiêu:** Web/email không kết luận sai khi thiết bị offline.
- **Đầu vào:** P3-05, P2-07, P4-03; D-12.
- **Đầu ra:** Xác định online/offline theo `LastSeenAt`; khi đánh giá một dose: online nếu `LastSeenAt >= DeadlineAt - ngưỡng`, chưa từng thấy thiết bị = offline; **không dùng thời điểm job chạy**; dose chuyển `Unknown` khi hết hạn mà thiếu dữ liệu; cờ kết nối trong dashboard; nội dung "không thể xác nhận".
- **Phụ thuộc:** P3-05, P2-07, P4-03.
- **Acceptance / DoD:** Test AC-06: offline tại deadline → `Unknown`, không `Overdue`/không "quên uống"; online tại deadline → `Overdue`; job chạy muộn sau khi thiết bị đã reconnect vẫn không đổi kết luận tại deadline; ngưỡng cấu hình; khi thiết bị gửi lại sự kiện muộn, hiển thị đúng theo D-24.
- **Ghi chú:** [TBD-D12] Ngưỡng, ví dụ 2–3 chu kỳ heartbeat — cần chốt. `LastSeenAt` NULL (chưa từng online) xử lý như offline.

### <a id="p4-05"></a>P4-05 · Đặc tả hợp đồng firmware

- **Mục tiêu:** Đảm bảo firmware và server hiểu giống nhau về sự kiện, lịch, retry.
- **Đầu vào:** P0-03; API_CONTRACT §Device; HTML §Offline.
- **Đầu ra:** Tài liệu: sơ đồ gọi API; `EventId` sinh ổn định; hàng đợi bền khi mất điện [TBD-D21]; batch + backoff; RTC sync và dung sai lệch giờ [TBD-D26]; quy tắc cạnh đóng→mở và nắp-mở-sẵn (kể cả trạng thái nắp lúc khởi động lại); áp dụng lịch theo version; định dạng thời gian UTC; quy trình cấu hình Wi‑Fi/server URL [TBD-D25].
- **Phụ thuộc:** P0-03; cập nhật sau P3-04/P4-02.
- **Acceptance / DoD:** Review chéo với API_CONTRACT; danh sách ca kiểm thử bench + simulator; bàn giao cho track firmware.
- **Ghi chú:** Nội dung tài liệu không thay đổi hợp đồng API; nếu cần đổi, quay lại P0-03.

---

## 10. Phase 5 — Cảnh báo & email quá hạn

<a id="phase-5"></a>

### <a id="p5-01"></a>P5-01 · Entity Alerts / AlertRecipients / EmailDeliveries

- **Mục tiêu:** Tách cảnh báo nghiệp vụ khỏi từng lần gửi để retry an toàn.
- **Đầu vào:** P3-01, P2-08; HTML §CSDL.
- **Đầu ra:** Entities + migration; `UNIQUE (AlertId, RecipientUserId, Channel)`; [ĐỀ XUẤT] `UNIQUE (DoseInstanceId, AlertType)`; `EmailDeliveries` giữ `AttemptNumber`, `NextAttemptAt`, `SentAt`, `ProviderMessageId` theo HTML; [ĐỀ XUẤT] thêm `LockedUntil`/`LockedBy` để claim an toàn đa instance và unique `(AlertRecipientId, AttemptNumber)`; index `EmailDeliveries(Status, NextAttemptAt)`.
- **Phụ thuộc:** P3-01, P2-08.
- **Acceptance / DoD:** Test chặn recipient trùng; chặn 2 alert cùng loại cho một dose; retry tạo `EmailDeliveries` mới, không tạo recipient mới; không thể tạo 2 attempt cùng số cho một recipient.
- **Ghi chú:** [TBD-D09] `AlertType`.

### <a id="p5-02"></a>P5-02 · Job đánh giá quá hạn & tạo cảnh báo

- **Mục tiêu:** Sinh cảnh báo đúng nghĩa, đúng người, idempotent.
- **Đầu vào:** P5-01, P3-05, P4-04, P2-08; HTML §Cảnh báo.
- **Đầu ra:** Background job: quét dose hết hạn → tạo `Alerts` + `AlertRecipients` trong **một transaction** cho chủ và mọi relative `Accepted` tại thời điểm đánh giá; online → quá hạn chưa xác nhận; offline → không thể xác nhận; chống chạy trùng; không block request; không tự set `ResolvedAt` khi chưa có D-24.
- **Phụ thuộc:** P5-01, P3-05, P4-04, P2-08.
- **Acceptance / DoD:** Test đủ người nhận (AC-09); chạy job lặp/song song không nhân đôi alert/recipient; không cảnh báo cho dose đã `Confirmed`; offline message đúng; relative bị revoke **trước** thời điểm đánh giá không nhận; alert + recipients luôn cùng tồn tại hoặc cùng rollback.
- **Ghi chú:** [TBD-D09] Có cảnh báo cho `Unknown` hay chỉ email? HTML: cảnh báo "không thể xác nhận" → AlertType riêng. **[TBD-D24]** Người nhận bị thu hồi sau khi alert đã tạo nhưng trước khi gửi, và việc resolve alert khi có xác nhận muộn — chưa chốt.

### <a id="p5-03"></a>P5-03 · Worker gửi email & retry độc lập

- **Mục tiêu:** Gửi email nền, mỗi người nhận độc lập, không gửi trùng.
- **Đầu vào:** P5-01, P0-04; D-03; D-13.
- **Đầu ra:** Worker xử lý `EmailDeliveries`: claim nguyên tử bằng `LockedUntil` (an toàn đa instance), attempt/status, `NextAttemptAt`, `SentAt`, `ProviderMessageId`; backoff theo D-13; cô lập lỗi từng recipient; không gửi lại recipient đã `Sent`; tối đa N lần → `Exhausted`.
- **Phụ thuộc:** P5-01, P0-04.
- **Acceptance / DoD:** Test một địa chỉ lỗi không ảnh hưởng người khác; retry không gửi trùng người đã nhận; hai worker chạy song song không cùng claim một delivery; max attempts → trạng thái cuối; provider lỗi không crash worker.
- **Ghi chú:** [TBD-D03, D-13] Provider và chính sách retry. Dùng `ProviderMessageId`/idempotency key nếu provider hỗ trợ. Gửi email là **at-least-once**: crash sau khi provider nhận nhưng trước khi lưu `Sent` có thể gửi lặp cho **cùng** người; nghiêm cấm lặp sang người khác. Ghi rõ giới hạn này trong runbook.

### <a id="p5-04"></a>P5-04 · Mẫu email & nội dung bắt buộc

- **Mục tiêu:** Email đúng bản chất, không cáo buộc sai.
- **Đầu vào:** P5-03; AGENTS §2.2; HTML §Cảnh báo.
- **Đầu ra:** Template quá hạn (online) và không thể xác nhận (offline); nêu tên thuốc từ snapshot, giờ, ngăn; không có nút tắt; wording "đã mở/đã xác nhận"; test snapshot nội dung.
- **Phụ thuộc:** P5-03.
- **Acceptance / DoD:** Review nội dung theo checklist; không chứa "đã uống"/"bỏ liều" khẳng định; offline template dùng "không thể xác nhận do mất kết nối"; thông tin đúng người/nhà thuốc.
- **Ghi chú:** [ĐỀ XUẤT] Tiếng Việt, HTML đơn giản, không bắt buộc chèn link.

### <a id="p5-05"></a>P5-05 · API cảnh báo & đánh dấu đã đọc

- **Mục tiêu:** Web hiển thị cảnh báo theo quyền, có trạng thái đọc.
- **Đầu vào:** P5-01, P5-02; API_CONTRACT §Alerts.
- **Đầu ra:** `GET /api/alerts`, `POST /api/alerts/{id}/read` [ĐỀ XUẤT dùng `ReadAt`]; lọc theo hộp, phân trang, unread count.
- **Phụ thuộc:** P5-01, P5-02, P1-05.
- **Acceptance / DoD:** Test owner/relative xem cảnh báo hộp mình; người ngoài 403; mark read chỉ ảnh hưởng người dùng hiện tại; không trả recipient người khác ngoài phạm vi; `resolvedAt` chỉ trả khi D-24 định nghĩa.
- **Ghi chú:** `ReadAt` nằm trên `AlertRecipients`; cần xác nhận endpoint mark-read (HTML chỉ có trường dữ liệu). **[TBD-D24]** Ý nghĩa `ResolvedAt` — hiện chỉ là trường ví dụ, chưa có quy tắc set.

---

## 11. Phase 6 — Ảnh đơn thuốc

<a id="phase-6"></a>

### <a id="p6-01"></a>P6-01 · Entity ảnh & lớp lưu trữ private

- **Mục tiêu:** Chỉ lưu metadata trong CSDL; file nằm ngoài, private.
- **Đầu vào:** P0-04, P1-05; D-04; HTML §Ảnh.
- **Đầu ra:** `PrescriptionImages` + migration; `IFileStorage` triển khai local private [ĐỀ XUẤT]; storage key ngẫu nhiên; cấu hình thư mục.
- **Phụ thuộc:** P0-04, P1-05.
- **Acceptance / DoD:** DB không có cột binary; file không nằm trong `wwwroot`; test ghi/đọc storage; không có URL tĩnh truy cập được.
- **Ghi chú:** [TBD-D04] Local disk vs blob; dung lượng/backup.

### <a id="p6-02"></a>P6-02 · Upload có kiểm soát

- **Mục tiêu:** Từ chối file sai, loại EXIF, giới hạn 5 MB.
- **Đầu vào:** P6-01; API_CONTRACT §Prescriptions.
- **Đầu ra:** `POST /api/prescriptions/images` multipart; whitelist JPEG/PNG/WebP theo magic bytes; ≤5 MB; strip EXIF; tên lưu ngẫu nhiên; ghi metadata + audit; giới hạn request body/multipart để không đọc hết file quá lớn.
- **Phụ thuộc:** P6-01.
- **Acceptance / DoD:** Test AC-11: .exe giả .jpg bị từ chối; >5 MB 413; EXIF (kể cả GPS) bị xóa; chỉ owner upload; relative 403; DB không chứa nội dung file; file không đọc được ngoài API.
- **Ghi chú:** [TBD-D16] Ảnh trùng/giới hạn số lượng ảnh.

### <a id="p6-03"></a>P6-03 · Xem/tải ảnh có kiểm tra quyền

- **Mục tiêu:** Chỉ chủ hộp đọc được nội dung ảnh.
- **Đầu vào:** P6-02; P1-05.
- **Đầu ra:** `GET /api/prescriptions/images`, `GET /api/prescriptions/images/{id}/content`; stream qua API; `Content-Disposition`; 401/403/404 đúng.
- **Phụ thuộc:** P6-02.
- **Acceptance / DoD:** Test relative/người ngoài 403; chưa đăng nhập 401; không lộ đường dẫn vật lý; không cache công khai.
- **Ghi chú:** Không trả URL công khai (AGENTS §2.3).

### <a id="p6-04"></a>P6-04 · Xóa mềm & audit ảnh

- **Mục tiêu:** Xóa an toàn, giữ dấu vết.
- **Đầu vào:** P6-03; D-16.
- **Đầu ra:** `DELETE /api/prescriptions/images/{id}` đánh dấu `IsDeleted`, xử lý file theo chính sách; audit; chặn truy cập sau xóa.
- **Phụ thuộc:** P6-03.
- **Acceptance / DoD:** Test xóa chỉ owner; sau xóa không tải được; audit ghi nhận; retry/idempotent.
- **Ghi chú:** [TBD-D16] Có xóa vật lý file ngay hay theo lịch.

### <a id="p6-05"></a>P6-05 · Đối chiếu file & runbook backup

- **Mục tiêu:** Phát hiện file thiếu/mồ côi; backup khôi phục được.
- **Đầu vào:** P6-01; HTML §Vận hành.
- **Đầu ra:** Job đối chiếu metadata ↔ file [ĐỀ XUẤT]; runbook backup/restore CSDL + vùng file, **mã hóa khi lưu và khi truyền**; biên bản test restore.
- **Phụ thuộc:** P6-01.
- **Acceptance / DoD:** Chạy job phát hiện được file thiếu/mồ côi giả lập; runbook có bước kiểm tra restore + xác nhận bản backup mã hóa; kết quả test lưu vào TASKS.
- **Ghi chú:** [TBD-D19] Tần suất job, retention.

---

## 12. Phase 7 — Hoàn thiện, bảo mật, nghiệm thu

<a id="phase-7"></a>

### <a id="p7-01"></a>P7-01 · Phủ audit cho thao tác quan trọng

- **Mục tiêu:** Có dấu vết kiểm tra cho hành động nhạy cảm.
- **Đầu vào:** P2-04, P2-09, P3-02, P5-02, P6-04; HTML `AuditLogs`.
- **Đầu ra:** Ghi audit thống nhất (ghép, sửa/xóa lịch, mời/thu hồi, upload/xóa ảnh, cảnh báo); append-only; không chứa secret/PII thừa.
- **Phụ thuộc:** P2-04, P2-09, P3-02, P5-02, P6-04.
- **Acceptance / DoD:** Checklist hành động → test tương ứng; không sửa/xóa audit qua API; review nội dung không lộ dữ liệu nhạy cảm.
- **Ghi chú:** [TBD-D19] Retention audit.

### <a id="p7-02"></a>P7-02 · Bộ test nghiệm thu đầu-cuối AC-01…AC-12

- **Mục tiêu:** Chứng minh MVP chạy xuyên suốt, không chỉ "có code".
- **Đầu vào:** Toàn bộ P1–P6, P7-01; HTML §Nghiệm thu.
- **Đầu ra:** Test tự động/bán tự động cho 12 tiêu chí (phần cứng dùng simulator/bench khi chưa có thiết bị); báo cáo kết quả; ghi rõ phần chưa kiểm chứng.
- **Phụ thuộc:** P1-* … P7-01, HW-02…HW-04, UI-01…UI-07.
- **Acceptance / DoD:** 12/12 pass hoặc danh sách gap có kế hoạch; test idempotency, snapshot, authz, ảnh, offline; mỗi AC có kịch bản cụ thể tại [TASKS.md §13](TASKS.md#acceptance).
- **Ghi chú:** Kịch bản offline → online là bắt buộc trong bộ test.

### <a id="p7-03"></a>P7-03 · Rà soát bảo mật & hardening

- **Mục tiêu:** Không còn lỗ hổng quyền/token/file trong phạm vi MVP.
- **Đầu vào:** P1–P6; [AGENTS.md](AGENTS.md) §7.
- **Đầu ra:** Ma trận quyền từng endpoint; HTTPS/HSTS; rate limit (auth, lời mời, **pair/accept token**); header bảo mật; CSRF/SameSite (D-02); không mở CORS vì Razor cùng origin (D-01); quét secret; rà dependency; kiểm tra log không lộ dữ liệu; xác nhận backup mã hóa.
- **Phụ thuộc:** P1-*, P2-*, P3-*, P5-*, P6-*.
- **Acceptance / DoD:** Checklist ký nhận; test authz toàn bộ; brute-force pair/accept bị 429; không secret trong repo/log; ảnh private; thiết bị không gọi được API người dùng.
- **Ghi chú:** Không thêm tính năng bảo mật ngoài MVP (MFA, SSO...) — ghi nhận sau MVP. Pairing code/API key/token dùng hash có pepper/HMAC, không SHA-256 trần.

### <a id="p7-04"></a>P7-04 · Observability & health

- **Mục tiêu:** Vận hành và debug được mà không lộ dữ liệu.
- **Đầu vào:** P0-04.
- **Đầu ra:** Structured logging + correlation ID; health checks DB/storage [ĐỀ XUẤT]; log level theo môi trường; không log token/PII.
- **Phụ thuộc:** P0-04.
- **Acceptance / DoD:** Mẫu log có trace; health check fail đúng khi mất DB; kiểm tra redaction.
- **Ghi chú:** [TBD-D19] Retention log, nền tảng log.

### <a id="p7-05"></a>P7-05 · Bộ demo & tài liệu bàn giao

- **Mục tiêu:** Demo được luồng chính, có dữ liệu mẫu và kịch bản.
- **Đầu vào:** P7-02; HTML mốc 5.
- **Đầu ra:** Seed dữ liệu demo (tách khỏi seed cấu trúc); kịch bản demo (đúng giờ, mở nắp, offline → online, quá hạn → email); README; tài liệu lắp ráp [ĐỀ XUẤT].
- **Phụ thuộc:** P7-02.
- **Acceptance / DoD:** Chạy thử kịch bản từ đầu đến cuối; dữ liệu mẫu không lẫn seed hệ thống; ghi lại thời gian/người chạy.
- **Ghi chú:** Không đưa tính năng sau MVP vào demo như thể đã có.

### <a id="p7-06"></a>P7-06 · Runbook vận hành & chính sách dữ liệu

- **Mục tiêu:** Có quy trình vận hành tối thiểu cho demo/pilot.
- **Đầu vào:** P6-05; HTML §Vận hành, §Xóa mềm.
- **Đầu ra:** Runbook: backup/restore, retention, xử lý worker email lỗi, đối chiếu file, quy trình ẩn danh hóa khi xóa tài khoản [TBD-D17].
- **Phụ thuộc:** P6-05.
- **Acceptance / DoD:** Tài liệu review chéo; các bước có lệnh/kiểm tra cụ thể; trạng thái TBD được giữ.
- **Ghi chú:** [TBD-D17] Chính sách ẩn danh chưa được đặc tả.

---

## 13. Web UI track

<a id="phase-ui"></a>

> Phụ thuộc D-01. Các task UI chỉ dùng endpoint đã chốt trong [API_CONTRACT.md](API_CONTRACT.md); không gọi API ngoài hợp đồng.

### <a id="ui-01"></a>UI-01 · Trang xác thực tài khoản

- **Mục tiêu:** Đăng ký, xác minh, đăng nhập, quên/đặt lại mật khẩu.
- **Đầu vào:** P1-02…P1-04; P0-01.
- **Đầu ra:** Form + validation + thông báo lỗi tiếng Việt; không liệt kê tài khoản; trạng thái loading/empty.
- **Phụ thuộc:** P1-02, P1-03, P1-04, P0-01.
- **Acceptance / DoD:** Đi trọn luồng AC-12 trên UI; lỗi 409/401/429 hiển thị đúng; responsive cơ bản.

### <a id="ui-02"></a>UI-02 · Ghép hộp & trạng thái thiết bị

- **Mục tiêu:** Nhập/quét pairing code, hiển thị trạng thái hộp.
- **Đầu vào:** P2-04, P2-05, P2-07; UI-01.
- **Đầu ra:** Màn hình ghép; xử lý mã dùng rồi/hết hạn/đã có hộp; hiển thị Paired/Unpaired, `lastSeenAt`.
- **Phụ thuộc:** P2-04, P2-05, P2-07, UI-01.
- **Acceptance / DoD:** Ghép thành công hiển thị 3 ngăn; lỗi mã hiển thị đúng; không hiển thị khóa thiết bị; hình thức quét theo [TBD-D20].

### <a id="ui-03"></a>UI-03 · Quản lý lịch uống

- **Mục tiêu:** Tạo/sửa/xóa lịch với ngày lặp, giờ, ngăn, grace, thuốc.
- **Đầu vào:** P3-02; UI-01.
- **Đầu ra:** Danh sách + form; validate 1–120 phút, ít nhất 1 ngày, ít nhất 1 thuốc; chế độ chỉ xem cho relative.
- **Phụ thuộc:** P3-02, UI-01.
- **Acceptance / DoD:** Owner thao tác được; relative không thấy nút sửa hoặc bị 403; lỗi validation hiển thị cạnh trường; dữ liệu hiển thị đúng timezone.

### <a id="ui-04"></a>UI-04 · Dashboard & lịch sử

- **Mục tiêu:** Hiển thị trạng thái hôm nay và lịch sử đúng ngữ nghĩa.
- **Đầu vào:** P3-06, P4-04; UI-02.
- **Đầu ra:** Thẻ trạng thái `Pending/Confirmed/Overdue/Unknown` với nhãn tiếng Việt đúng; danh sách lịch sử + snapshot thuốc; phân trang.
- **Phụ thuộc:** P3-06, P4-04, UI-02.
- **Acceptance / DoD:** Không xuất hiện "đã uống"; Unknown hiển thị "Không thể xác nhận"; owner/relative cùng xem đúng phạm vi; responsive.

### <a id="ui-05"></a>UI-05 · Người thân & cảnh báo

- **Mục tiêu:** Quản lý lời mời/quan hệ và xem cảnh báo.
- **Đầu vào:** P2-09, P2-10, P5-05; UI-01.
- **Đầu ra:** Mời theo email, danh sách trạng thái, thu hồi/ngừng theo dõi; danh sách cảnh báo + đánh dấu đã đọc.
- **Phụ thuộc:** P2-09, P2-10, P5-05, UI-01.
- **Acceptance / DoD:** Chủ mời/thu hồi được; relative tự rời được; lỗi trùng/rate-limit hiển thị đúng; cảnh báo chỉ trong hộp có quyền.

### <a id="ui-06"></a>UI-06 · Ảnh đơn thuốc

- **Mục tiêu:** Chủ hộp upload/xem/xóa ảnh; không chia sẻ.
- **Đầu vào:** P6-02…P6-04; UI-01.
- **Đầu ra:** Trang upload (giới hạn 5 MB, JPEG/PNG/WebP), danh sách, xem, xóa; thông báo lỗi file.
- **Phụ thuộc:** P6-02, P6-03, P6-04, UI-01.
- **Acceptance / DoD:** Relative không thấy màn hình/không tải được; file sai bị từ chối với thông báo rõ; không hiển thị URL trực tiếp; không OCR.

### <a id="ui-07"></a>UI-07 · Rà soát responsive & thuật ngữ

- **Mục tiêu:** Đồng nhất ngôn ngữ và trải nghiệm trên các màn hình.
- **Đầu vào:** UI-01…UI-06; AGENTS §2.2.
- **Đầu ra:** Checklist responsive; bảng thuật ngữ (Sáng/Trưa/Tối, Đã mở/Đã xác nhận, Không thể xác nhận...); trạng thái rỗng/lỗi; ảnh chụp màn hình.
- **Phụ thuộc:** UI-01…UI-06.
- **Acceptance / DoD:** Không còn chuỗi "đã uống"; nhất quán nhãn trạng thái; kiểm tra mobile 360px/desktop; ghi bằng chứng vào TASKS.

---

## 14. Track phần cứng / firmware

<a id="phase-hw"></a>

> [TBD-D06] Vị trí code firmware. Các task dưới đây có thể nằm ngoài repo C#; PLAN theo dõi để đảm bảo tích hợp.

### <a id="hw-01"></a>HW-01 · Sơ đồ khối, BOM, pin map, nguồn

- **Mục tiêu:** Chốt phần cứng đủ cho 3 ngăn độc lập.
- **Đầu vào:** HTML §Kiến trúc; mốc 1.
- **Đầu ra:** Sơ đồ khối; BOM; pin map ESP32; 3 cảm biến nắp, 3 đèn, còi, RTC, Wi‑Fi, nguồn DC; ghi chú an toàn điện.
- **Phụ thuộc:** —
- **Acceptance / DoD:** Review với yêu cầu 3 ngăn + offline; không thiếu linh kiện RTC/cảm biến; tài liệu lắp ráp khung.

### <a id="hw-02"></a>HW-02 · Firmware nhắc tại chỗ theo RTC

- **Mục tiêu:** Đúng giờ, đúng ngăn sáng đèn + còi; ngăn khác không sáng.
- **Đầu vào:** HW-01; P4-05 [ĐỀ XUẤT]; dữ liệu lịch theo payload P3-04.
- **Đầu ra:** Lưu lịch cục bộ; đọc RTC; vòng lặp nhắc; điều khiển LED/còi theo slot; hiển thị/ghi log trạng thái.
- **Phụ thuộc:** HW-01.
- **Acceptance / DoD:** Bench test AC-01; chạy không Wi‑Fi vẫn nhắc (AC-04 phần nhắc); không nhắc nhầm ngăn; grace cục bộ đúng.
- **Ghi chú:** [TBD-D06] Ngôn ngữ/framework firmware (Arduino/ESP-IDF).

### <a id="hw-03"></a>HW-03 · Phát hiện cạnh nắp & hàng đợi sự kiện

- **Mục tiêu:** Sinh sự kiện mở nắp chính xác, kể cả nắp mở sẵn; giữ sự kiện khi offline.
- **Đầu vào:** HW-02; P4-05.
- **Đầu ra:** Logic cạnh đóng→mở; quy tắc nắp mở sẵn phải đóng rồi mở lại; `EventId` ổn định; queue bền (flash/NVS) [TBD-D21]; giới hạn queue.
- **Phụ thuộc:** HW-02.
- **Acceptance / DoD:** Bench AC-02, AC-03; mất điện không mất sự kiện đã queue (nếu D-… chốt yêu cầu bền); không tạo event khi nắp đã mở trước giờ.
- **Ghi chú:** [TBD-D21] Yêu cầu bền qua mất điện chưa nêu trong HTML — đề xuất có.

### <a id="hw-04"></a>HW-04 · Kết nối, heartbeat, sync lịch, gửi batch

- **Mục tiêu:** Đồng bộ hai chiều với server, retry an toàn.
- **Đầu vào:** HW-03; P2-07; P3-04; P4-02.
- **Đầu ra:** Wi‑Fi reconnect; heartbeat định kỳ; tải lịch theo version và áp dụng trọn gói; gửi event batch; retry/backoff; xử lý phản hồi Duplicate.
- **Phụ thuộc:** HW-03, P2-07, P3-04, P4-02.
- **Acceptance / DoD:** Bench AC-05 (gửi lặp không nhân đôi); offline → online gửi đúng thứ tự; giờ server đồng bộ RTC; lịch hỏng không ghi đè lịch tốt.

### <a id="hw-05"></a>HW-05 · Quy trình in QR & provision nhãn

- **Mục tiêu:** Nhãn QR đúng một lần, không lộ khóa.
- **Đầu vào:** P2-02, P2-03.
- **Đầu ra:** Quy trình cấp mã, in nhãn, dán tem; mẫu; kiểm tra chéo serial.
- **Phụ thuộc:** P2-02, P2-03.
- **Acceptance / DoD:** Mẫu in quét được; mã chỉ dùng một lần; không có `DeviceApiKey`; quy trình có bước thu hồi mã dán nhầm.

---

## 15. Song song hóa

| Giai đoạn | Có thể chạy song song | Phải tuần tự |
|---|---|---|
| Nền móng | P0-04 ∥ P0-05 ∥ HW-01 | P0-01 → P0-02 → P0-03 |
| Tài khoản & thiết bị | P1-02 ∥ P1-04 ∥ P2-01 | P1-01 → P1-03 → P1-05; P2-01 → P2-02 → P2-04 |
| Lịch & sự kiện | P3-01 → (P3-02 ∥ P3-03); P4-01 song song sau P2-01 | P3-03 → P3-05 → P4-03; P2-07 → P3-04 |
| Cảnh báo & ảnh | P5-* ∥ P6-* (sau P3-05 / P0-04) | P5-01 → P5-02 → P5-03 → P5-04 |
| Firmware | HW-02 ∥ track backend; HW-05 ∥ P2-03 | HW-02 → HW-03 → HW-04 |
| Web UI | UI-01 → UI-02 → UI-03; UI-06 ∥ UI-05 | UI-07 cuối cùng |
| Nghiệm thu | P7-04 ∥ P7-06 | P7-01 → P7-02 → P7-03 → P7-05 |

Quy tắc: chỉ bắt đầu task khi **mọi dependency ở trạng thái DONE** hoặc có quyết định chấp nhận rủi ro được ghi trong TASKS. P3-02 và P3-03 có thể code song song nhưng **không được DONE** trước khi D-22/D-23 được chốt vì cùng ảnh hưởng tập dose tương lai. Mỗi mốc M1–M5 phải có **dữ liệu mẫu chung + một kịch bản tích hợp firmware ↔ backend** để hai nhánh kiểm tra được nhau (HTML §12).

---

## 16. Traceability — 12 tiêu chí nghiệm thu

<a id="traceability"></a>

| AC | Tiêu chí (HTML) | Task chính | Lớp kiểm thử |
|---|---|---|---|
| AC-01 | Nhắc đúng ngăn, đúng giờ, hai ngăn kia không sáng | P3-03, P3-04, HW-02 | Unit (lịch) + bench |
| AC-02 | Xác nhận đúng cửa sổ; sớm/sai ngăn không xác nhận | P4-03, HW-03 | Integration + benchmark |
| AC-03 | Nắp mở sẵn không tự xác nhận; đóng rồi mở lại hợp lệ | P4-03, HW-03 | Integration + bench |
| AC-04 | Offline vẫn nhắc theo RTC và giữ sự kiện | HW-02, HW-03, HW-04, P3-04 | Bench + simulator |
| AC-05 | Gửi lại event batch không tạo kết quả trùng | P4-01, P4-02, P4-03 | Integration (idempotency) |
| AC-06 | Offline: Web/email dùng Unknown/"không thể xác nhận" | P3-05, P4-04, P5-04, UI-04 | Unit + E2E |
| AC-07 | Người thân xem được nhưng bị chặn sửa cấu hình/ảnh | P1-05, P2-10, P3-02, P3-06, P6-03 | Authorization matrix |
| AC-08 | QR dùng một lần; quét lại bị từ chối; không lộ DeviceApiKey | P2-02, P2-03, P2-04 | Integration + manual |
| AC-09 | Email quá hạn đủ chủ + mọi relative Accepted; retry không nhân đôi | P5-01, P5-02, P5-03, P5-04 | Integration + email sink |
| AC-10 | Snapshot không đổi ngược khi sửa lịch/thuốc | P3-02, P3-03 | Integration |
| AC-11 | Ảnh sai loại/quá 5 MB bị từ chối; loại EXIF; file ngoài DB | P6-01, P6-02, P6-03 | Integration + security |
| AC-12 | Trọn luồng đăng ký → xác minh → đăng nhập → quên/đặt lại | P1-02, P1-03, P1-04, UI-01 | E2E |

---

## 17. Definition of Done cấp dự án

1. M1–M5 hoàn tất; mọi task trong [TASKS.md](TASKS.md) `DONE` kèm bằng chứng.
2. 12/12 tiêu chí nghiệm thu pass trên môi trường demo (phần cứng thật hoặc bench, ghi rõ).
3. Không có TBD ảnh hưởng vận hành bị bỏ ngỏ; danh sách TBD còn lại được chấp nhận có chủ đích.
4. Hợp đồng API khớp code; không endpoint ngoài hợp đồng.
5. Checklist bảo mật P7-03 hoàn tất; backup/restore đã thử.
6. Kịch bản demo chạy được từ đầu đến cuối, bao gồm offline → online.
7. Tài liệu bàn giao: README, runbook, hướng dẫn lắp ráp [nếu có].
8. Không có tính năng ngoài MVP bị đưa vào code/UI/schema.

---

## 18. Rủi ro & giả định

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Mâu thuẫn "MỐC MVP" (M4) vs 12 AC gồm ảnh (M5) | Lệch kỳ vọng nghiệm thu | Định nghĩa MVP lõi/hoàn chỉnh tại §2.1; chủ dự án xác nhận |
| Thay đổi D-01/D-02 sau khi đã code | Đổi cấu trúc/auth giữa chừng, phải sửa schema và UI | Đã chốt 2026-10-29; mọi thay đổi phải cập nhật đồng bộ 4 tài liệu trước khi code |
| Firmware ngoài repo (D-06) | Tích hợp muộn, lệch hợp đồng | P4-05 chốt sớm; test bằng simulator; bench định kỳ |
| Ngưỡng offline (D-12) chưa chốt | Sai trạng thái Overdue/Unknown | Chốt cùng P4-04; test biên |
| Job sinh dose/alert chạy trùng | Dữ liệu nhân đôi | Unique key + transaction + test chạy song song |
| Sửa lịch làm lệch dose server ↔ lịch trên thiết bị (D-22) | Nhắc giờ mới nhưng server chờ giờ cũ → không xác nhận | Chốt D-22 trước P3-02; test sửa lịch trong lúc thiết bị offline/online |
| Hai lịch chồng lấn cùng ngăn (D-23) | Một lần mở khớp nhiều dose, nhắc trùng | Chốt quy tắc; chặn trùng tuyệt đối; test biên |
| Job alert chạy muộn dùng `LastSeenAt` hiện tại | Online/Offline sai, kết luận sai | Đánh giá theo trạng thái tại `DeadlineAt`; test reconnect sau deadline |
| Xác nhận muộn sau Overdue/Unknown (D-24) | Trạng thái/alert không nhất quán | Chốt D-24; hiện mặc định không đổi trạng thái |
| RTC lệch giờ (D-26) | Event rơi ngoài cửa sổ hoặc bị reject oan | Heartbeat trả `serverTimeUtc`; ngưỡng lệch; test lệch ± |
| Horizon sinh dose < thời gian offline | Event offline về muộn không có dose để ghép | D-11 phải nêu thời gian offline tối đa hỗ trợ; test offline dài |
| Email crash sau khi provider nhận | Gửi lặp cho cùng một người | Ghi nhận at-least-once; provider idempotency key; không lặp sang người khác |
| Pairing code ngắn bị brute-force | Chiếm hộp | Rate limit pair; hash có pepper; test 429 |
| Email provider/rate limit | Email không tới, spam | Provider abstraction, dev sink, retry độc lập, chốt D-03/D-13 |
| Timezone sai | Dose sai giờ | Lưu UTC + `TimeZoneId`, test DST [ĐỀ XUẤT] |
| Thiếu thiết bị thật khi demo | Không chứng minh AC phần cứng | Simulator + ghi rõ phần chưa kiểm chứng; mượn thiết bị cho bench |

Giả định:

- Mỗi hộp có đúng 3 ngăn, cố định slot 1–3 trong suốt MVP.
- Một tài khoản có thể vừa là chủ 1 hộp vừa là người thân của nhiều hộp khác (HTML cho phép qua bảng nối).
- Người thân không có giới hạn số lượng (chỉ rate-limit lời mời).
- Người thân phải có tài khoản đã xác minh email để chấp nhận lời mời (HTML yêu cầu xác minh email cho luồng ghép; áp dụng nhất quán cho lời mời — nếu chủ dự án chốt khác, cập nhật P2-09).
- Thời gian nghiệp vụ tính theo `TimeZoneId` của lịch; hiển thị cho người dùng theo timezone đó [TBD-D10].
- Không có tích hợp thiết bị y tế hoặc tư vấn y tế trong MVP.
- Firmware có thể được cấu hình Wi‑Fi/server URL khi sản xuất hoặc bằng quy trình onboarding riêng; phương thức cụ thể **[TBD-D25]**, không tự thêm màn hình cấu hình vào Web nếu chưa chốt.
- "Đã xác nhận" trong toàn bộ tài liệu chỉ có nghĩa "nắp đúng ngăn đã mở trong cửa sổ", không phải đã uống thuốc.
