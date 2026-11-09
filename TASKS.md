# TASKS.md — Checklist theo dõi tiến độ MVP Hộp thuốc thông minh

## Implementation update — 2026-11-10

- `main` includes prescription UI (`UI-06`), device pairing/status UI (`UI-02`), file reconciliation/runbook (`P6-05`), dose generation wiring, and dose state-machine support (`P3-05`).
- Verification: `dotnet build Project_1.sln --no-restore /p:UseSharedCompilation=false` passes with 0 warnings and 0 errors; unit tests pass `154/154`.
- Integration tests compile, but SQL Server LocalDB is unavailable in this environment (`Cannot create an automatic instance`), so integration/runtime acceptance remains unverified.
- `P3-03`, `P4-03`, `P5-02`, `P5-03`, dashboard/history, firmware/simulator, and final MVP acceptance remain incomplete. No TBD decision was silently finalized.

> Phiên bản: 3.0 · Ngày: 2026-11-09 · Trạng thái: **P0-01…P0-05, P1-01…P1-06, P2-01, P2-02, P2-04…P2-10, P3-01, P4-01, P5-01, P6-01, P6-02, P7-04, UI-01 DONE; P2-03, P3-02, P4-02 IN_PROGRESS; QA baseline: 1 PASS / 0 FAIL / 8 BLOCKED / 73 NOT_RUN — MVP NOT ACCEPTED**
>
> Bản 3.0 ghi nhận đợt song song 2 (3 subagent): P2-04, P2-05, P4-01, P6-01, P6-02 DONE; P4-02 IN_PROGRESS (chặn bởi D-26). Ghi nhận lệch hợp đồng "ảnh giải mã được".
>
> Bản 2.9 ghi nhận đợt song song (5 subagent, merge vào `main`): P2-02, P2-06, P2-07, P2-09, P2-10, P7-04, UI-01 DONE; P2-03 và P3-02 IN_PROGRESS (P3-02 chặn bởi D-23). Ghi nhận **D-29**, **D-30**, mâu thuẫn khóa rate limit accept, và lệch schema timestamp của lịch.
>
> Bản 2.8 ghi nhận P1-06 hoàn thành: rate limit sliding window theo D-14 (login/pair/accept theo IP, invitation theo người dùng), 429 `RATE_LIMITED` + `Retry-After`, ngưỡng cấu hình được; ghi nhận **D-28** (ngưỡng các endpoint auth còn lại).
>
> Bản 2.7 ghi nhận P1-05 hoàn thành: resolver vai trò theo hộp (Owner / Relative Active / None) + policy Read/Manage; người ngoài và hộp không tồn tại cùng 403; quyền chỉ dựa trên phiên đăng nhập.
>
> Bản 2.6 ghi nhận P1-04 hoàn thành: quên/đặt lại mật khẩu dùng `AuthTokens` (`PasswordReset`, TTL 24 giờ), thu hồi toàn bộ phiên sau khi đổi; chuỗi AC-12 (API) có test đầu-cuối.
>
> Bản 2.5 ghi nhận P1-02 hoàn thành: đăng ký + xác minh email theo D-14, bảng `AuthTokens` (HMAC+pepper, dùng một lần, TTL 24 giờ), pepper bắt buộc qua `Security:TokenPepper`.
>
> Bản 2.4 chốt D-05 (CLI nội bộ), D-10 (IANA theo từng lịch), D-14 (gói chính sách mật khẩu/token/rate limit/hạn lời mời), D-22 (giữ nguyên dose đã sinh).
>
> Bản 1.2 ghi nhận P0-01 hoàn thành và chốt D-01/D-02. Bản 1.3 cập nhật kết quả QA thực tế. Bản 1.4 ghi nhận P0-02 (schema + migration `InitialSchema`) hoàn thành. Bản 1.5 ghi nhận P0-03 hoàn thành: chốt D-07/D-08/D-09, quy ước ProblemDetails và catalog lỗi. Bản 1.6 ghi nhận P0-04 hoàn thành: `IClock`, email dev sink, `IFileStorage` private, middleware ProblemDetails + `traceId`, cấu hình logging không ghi header/body; bổ sung mã `405 METHOD_NOT_ALLOWED` (API_CONTRACT 1.4.1). Bản 1.7 ghi nhận P0-05 hoàn thành: `UserSecretsId`, mẫu cấu hình, hướng dẫn user-secrets/env, rà `.gitignore`, quét secret và xác minh clone sạch chạy được API. Bản 1.8 ghi nhận P1-01 hoàn thành: chuẩn hóa email trong entity, `IPasswordHasher` + `PasswordHasherService` (ASP.NET Core Identity), test trùng email khác hoa/thường và hash mật khẩu. Bản 1.9 ghi nhận P1-03 hoàn thành: cookie auth + CSRF (`X-CSRF-TOKEN`), session server-side thu hồi được, `POST /api/auth/login`, `POST /api/auth/logout`, `GET /api/users/me`; ghi nhận **D-27** (session store in-memory + TTL 8 giờ là đề xuất, chưa chốt durable/multi-instance). Bản 2.0 ghi nhận P2-01 hoàn thành: `DeviceProvisioning` (Domain) + `DeviceProvisioningService` (Infrastructure) seed đúng 3 ngăn trong một transaction, test unit + integration. Bản 2.1 ghi nhận P2-08 hoàn thành: chuẩn hóa `InviteeEmail` trong entity, test filtered unique Pending theo email chuẩn hóa, revoked giữ lịch sử, `ExpiresAt` NOT NULL, quan hệ active/revoked. Bản 2.2 ghi nhận P3-01 hoàn thành: check `CK_MedicationSchedules_DateRange` + migration, `MedicationScheduleRules` (ít nhất 1 ngày, DayOfWeek 1–7, grace 1–120, timezone), test constraint/xóa mềm. Bản 2.3 ghi nhận P5-01 hoàn thành: xác nhận schema cảnh báo/recipient/delivery từ `InitialSchema`, test retry thêm attempt không nhân đôi recipient.
>
> Nguồn kế hoạch: [PLAN.md](PLAN.md) · Hợp đồng API: [API_CONTRACT.md](API_CONTRACT.md) · Quy tắc làm việc: [AGENTS.md](AGENTS.md) · Kế hoạch kiểm thử: [TEST_PLAN.md](TEST_PLAN.md) · Báo cáo kiểm thử: [TEST_REPORT.md](TEST_REPORT.md)

---

## 1. Cách dùng

### 1.1. Trạng thái hợp lệ

| Trạng thái | Nghĩa | Bắt buộc kèm |
|---|---|---|
| `TODO` | Chưa bắt đầu | — |
| `IN_PROGRESS` | Đang làm, đã có thay đổi cục bộ/commit nháp | Ngày bắt đầu, người/agent |
| `BLOCKED` | Không thể tiếp tục | Lý do, đã thử gì, quyết định cần ai (AGENTS §11) |
| `DONE` | Hoàn thành | **Bằng chứng**: test pass, commit hash, đường dẫn log/ảnh |

Quy tắc:

- **Không** đánh `DONE` khi chưa chạy test hoặc chưa có bằng chứng kiểm chứng.
- Không đổi trạng thái sang `DONE` nếu còn dependency ở trạng thái `TODO`/`BLOCKED`.
- Mỗi lần cập nhật: sửa đúng dòng, ghi bằng chứng ngắn trong cột cuối, thêm dòng vào §10 nếu là cột mốc lớn.
- Nếu phát hiện TBD mới: thêm vào [PLAN.md §3](PLAN.md#decision-log) và ghi lại trong báo cáo; **không tự chốt**.

### 1.2. Mẫu bằng chứng

```
test: <TênTest> (x/y pass) · cmd: dotnet test --filter <...>
commit: <hash>
artifacts: <đường dẫn test report / ảnh chụp màn hình / log>
ghi chú: <phần chưa kiểm chứng nếu có>
```

---

## 2. Cổng mốc (Milestone gates)

> Mỗi cổng chỉ được tick khi **mọi task thuộc mốc DONE** và có bằng chứng.

- [ ] **M1 — Nền móng**: `P0-01`…`P0-05`, `HW-01` DONE; build xanh; migration đầu chạy được; API contract chốt TBD nhóm P0.
- [ ] **M2 — Tài khoản & ghép hộp**: `P1-*`, `P2-*`, `UI-01`, `UI-02` DONE; AC-08, AC-12 (phần API) pass.
- [ ] **M3 — Lịch & nhắc tại chỗ**: `P3-*`, `HW-02`…`HW-04`, `UI-03` DONE; AC-01, AC-04, AC-10 pass (hoặc ghi rõ phần chờ thiết bị).
- [ ] **M4 — Đồng bộ & cảnh báo (MVP lõi)**: `P4-*`, `P5-*`, `UI-04`, `UI-05` DONE; AC-02, AC-03, AC-05, AC-06, AC-09 pass.
- [ ] **M5 — Hoàn thiện demo**: `P6-*`, `P7-*`, `UI-06`, `UI-07`, `HW-05` DONE; AC-07, AC-11 pass.
- [ ] **MVP SIGN-OFF**: M1–M5 hoàn tất + **12/12 tiêu chí nghiệm thu** + kịch bản demo chạy đầu-cuối (xem §13). _QA 2026-10-29: chưa đạt — 0/12 AC, xem §13.2._

**Cổng quyết định (chặn DONE, không chặn code thăm dò):**

- [ ] **D-23** chốt trước khi `P3-02`/`P3-03`/`P3-04` được đánh `DONE` (D-22 đã chốt 2026-10-29: giữ nguyên dose đã sinh).
- [ ] **D-24** chốt trước khi `P3-05`/`P4-03`/`P5-02`/`P5-05` được đánh `DONE`.
- [ ] **D-25 + D-26** chốt trước khi `HW-04`/`P4-05` được đánh `DONE`.
- [ ] **D-11 + D-12** chốt trước khi `P3-03`/`P4-04`/`P5-02` được đánh `DONE`.

---

## 3. Phase 0 — Nền móng & hợp đồng

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P0-01](PLAN.md#p0-01) | Chốt cấu trúc repo & skeleton | `DONE` | — | `dotnet build`: 0 warning/0 error · `HomePageTests.Get_Root_ReturnsHtmlPage` 1/1 pass · Kestrel: `GET /` 200 `text/html`, `GET /css/site.css` 200 | D-01 = Razor trong Api; D-02 = cookie + CSRF; scaffold MVC cũ đã xóa; xem §14 |
| [P0-02](PLAN.md#p0-02) | Mô hình dữ liệu & migration đầu | `DONE` | P0-01 | `20261029070908_InitialSchema` apply trên LocalDB trống · `SchemaConstraintTests` 21/21 pass · full suite 22/22 · build 0W/0E | 17 bảng (bỏ `RefreshTokens` do D-02) + 4 index nóng + check enum; ERD [docs/erd.md](docs/erd.md), SQL [docs/schema.sql](docs/schema.sql); D-09 đã chốt tại P0-03, giá trị enum không đổi nên không cần migration mới |
| [P0-03](PLAN.md#p0-03) | Chốt hợp đồng API & enum | `DONE` | P0-02 | API_CONTRACT v1.4 chốt D-07/D-08/D-09 + §1.4 ProblemDetails + catalog lỗi · `SharedEnumContractTests` 7/7 pass · full suite 29/29 · build 0W/0E · PLAN/AGENTS/README/docs/erd đồng bộ | D-07 = header + khóa 32 byte HMAC; D-08 = Crockford 8 ký tự/TTL 15 phút/QR `{v,code}`; D-09 = enum §10.2 + `daysOfWeek` ISO; các TBD khác giữ nhãn |
| [P0-04](PLAN.md#p0-04) | Hạ tầng cắt ngang (ProblemDetails, clock, email, storage) | `DONE` | P0-01 | `ProblemDetailsTests` 7/7 (404/405/400 validation/JSON hỏng/415/500 không lộ chi tiết/logging không ghi header-body-query) · `SystemClockTests` 1/1 · `FileEmailSenderTests` 2/2 · `LocalFileStorageTests` 11/11 · full suite 50/50 · build 0W/0E · API_CONTRACT 1.4.1 bổ sung `405 METHOD_NOT_ALLOWED` · Kestrel: `/` + `/css/site.css` 200, `/api/xyz` 404 `application/problem+json` | D-03/D-04 vẫn TBD: chỉ triển khai dev sink file + local storage private theo [ĐỀ XUẤT] của PLAN. Health check [ĐỀ XUẤT] hoãn sang P7-04. Chi tiết §17 |
| [P0-05](PLAN.md#p0-05) | Secret & vệ sinh repo | `DONE` | P0-01 | `<UserSecretsId>` cho `SmartMedBox.Api`; [appsettings.Development.sample.json](src/SmartMedBox.Api/appsettings.Development.sample.json) (LocalDB không credential); README mục “Cấu hình & secret” (user-secrets + `ConnectionStrings__Default`); `.gitignore` bổ sung `.env*`, `*.key`, `*.pem`; bản sao đặc tả [docs/requirements/Hop-thuoc-thong-minh.html](docs/requirements/Hop-thuoc-thong-minh.html); quét secret: không khớp mẫu credential thật (chỉ LocalDB không mật khẩu); export sạch 69 file → `dotnet build` 0W/0E → Kestrel `GET /` 200 HTML, CSS 200; full suite 50/50 | Không có secret/chuỗi kết nối thật trong git; DB runtime đọc `ConnectionStrings:Default` từ user-secrets/env (DbContext sẽ đăng ký tại P1-01). D-03/D-04 vẫn TBD, không ảnh hưởng task này |

---

## 4. Phase 1 — Tài khoản

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P1-01](PLAN.md#p1-01) | Entity người dùng & hash mật khẩu | `DONE` | P0-02 | `EmailNormalizerTests` 8/8 (trim/lowercase, rỗng/quá dài, setter entity) · `PasswordHasherServiceTests` 8/8 (salt khác nhau, verify đúng/sai, hash hỏng) · `ApplicationUserTests` 2/2 (trùng email khác hoa/thường bị chặn, lưu email chuẩn hóa + hash) · full suite 68/68 · build 0W/0E | Không cần migration mới: `ApplicationUsers` + unique `Email` đã có từ `InitialSchema`. `IPasswordHasher` đăng ký singleton trong API; chuẩn hóa email áp dụng ngay trong setter entity. D-14 đã chốt sau task (mật khẩu ≥ 8 chữ+số ≤ 128), chưa áp validation ở P1-01 |
| [P1-02](PLAN.md#p1-02) | Đăng ký & xác minh email | `DONE` | P1-01, P0-04 | `RegistrationApiTests` 12/12 (đăng ký tạo user chưa xác minh + email sink; trùng email khác hoa/thường → 409 `EMAIL_TAKEN`; mật khẩu yếu 3 case + email sai → 400 `VALIDATION_ERROR` kèm `errors`; token dùng lại/không tồn tại → 400 `TOKEN_INVALID`; hết hạn → 410 `TOKEN_EXPIRED`; resend ẩn danh → 202 không gửi mail; resend vô hiệu token cũ; response không chứa mật khẩu/token) · `PasswordPolicyTests` 12/12 · `SecureTokenTests` 2/2 · `HmacTokenHasherTests` 7/7 · `SchemaConstraintTests` 36/36 (thêm unique `TokenHash`, check `Purpose`) · migration `20261029092143_AddAuthTokens` · full suite 149/149 (83 unit + 66 integration) · build 0W/0E | Bảng `AuthTokens` [ĐỀ XUẤT KỸ THUẬT] (HTML không có); chỉ lưu `HMAC-SHA256(pepper, token)`, token 32 byte CSPRNG base64url dùng một lần. **Pepper bắt buộc** `Security:TokenPepper` ≥ 32 ký tự, không có mặc định — API dừng khởi động nếu thiếu. TTL 24 giờ theo D-14. Resend vô hiệu token cũ là [ĐỀ XUẤT]. Gửi email qua `IEmailSender` hiện có (D-03 vẫn TBD); rate limit login/resend thuộc P1-06 |
| [P1-03](PLAN.md#p1-03) | Đăng nhập/đăng xuất/hồ sơ | `DONE` | P1-01, P0-03 | `AuthApiTests` 6/6 (thiếu CSRF → 400 `CSRF_INVALID`; sai mật khẩu vs email không tồn tại cùng 401; chưa xác minh → 403; login đặt cookie `Secure`/`HttpOnly`/`SameSite=Lax` + `GET /api/users/me` 200; logout thu hồi session phía server, cookie cũ hết hiệu lực; ẩn danh → 401) · `InMemoryUserSessionStoreTests` 6/6 · full suite 80/80 (43 unit + 37 integration) · build 0W/0E | D-02: cookie + CSRF, không refresh/JWT. Production chưa có endpoint phát CSRF token cho API client — UI-01 (Razor) sẽ render token trong form. **D-27 mới**: session lưu in-memory + TTL 8 giờ là [ĐỀ XUẤT], chưa chốt durable/multi-instance |
| [P1-04](PLAN.md#p1-04) | Quên & đặt lại mật khẩu | `DONE` | P1-01, P0-04 | `PasswordResetApiTests` 10/10 (forgot email lạ/không hợp lệ → 202 không gửi; token đúng purpose/TTL 24h; forgot lần 2 vô hiệu token cũ; reset đúng → mật khẩu cũ hết hiệu lực, mật khẩu mới đăng nhập được, phiên cũ bị thu hồi, token dùng lại → `TOKEN_INVALID`; mật khẩu yếu → `VALIDATION_ERROR` và token còn dùng được; token lạ → 400; token hết hạn → 410; chuỗi AC-12 đăng ký→xác minh→đăng nhập→quên→đặt lại→đăng nhập) · `InMemoryUserSessionStoreTests` thêm 1 test `RevokeAllForUser` 7/7 · full suite 160/160 (84 unit + 76 integration) · build 0W/0E | Dùng lại `AuthTokens` (`Purpose=PasswordReset`), không cần migration mới. Chỉ token mới nhất còn hiệu lực; đổi mật khẩu tiêu thụ token trong transaction + thu hồi toàn bộ session (thêm `IUserSessionStore.RevokeAllForUser`). Forgot luôn 202 kể cả email sai định dạng (không liệt kê tài khoản). Rate limit thuộc P1-06 |
| [P1-05](PLAN.md#p1-05) | Phân quyền theo hộp (owner/relative) | `DONE` | P1-03 | `DeviceAccessPolicyTests` 6/6 (ma trận Owner/Relative/None × Read/Manage) · `DeviceAccessTests` 5/5 (resolver trên SQL Server: owner, relative Active, relative Revoked, người ngoài, hộp khác/chưa ghép/không tồn tại → None; HTTP: owner Read+Manage 200, relative Read 200/Manage 403, revoked/người ngoài 403 `FORBIDDEN`, ẩn danh 401; `userId` trong query bị bỏ qua; hộp không tồn tại và hộp người khác cùng 403) · full suite 171/171 (90 unit + 81 integration) · build 0W/0E | `DeviceAccessPolicy` (Domain) + `DeviceAccessService` (Infrastructure, scoped) + `CurrentUser.TryGetUserId` (Api). Không cần migration. Người ngoài/hộp không tồn tại/hộp chưa ghép đều 403 thống nhất (API_CONTRACT §11). Endpoint thật gắn kiểm tra khi triển khai (P2-05, P2-09, P3-02, P3-06, P5-05, P6-03); kiểm thử ma trận hiện qua probe controller trong test. [ĐỀ XUẤT] dùng service gọi trực tiếp thay vì ASP.NET authorization handler |
| [P1-06](PLAN.md#p1-06) | Rate limiting & chống lạm dụng | `DONE` | P1-03 | `RateLimitingTests` 5/5 (login lần 11 trong 15 phút → 429 `RATE_LIMITED` + `Retry-After: 900` + `traceId`, mọi lần thử đều bị đếm; login bị chặn không ảnh hưởng `/api/users/me`/csrf; ngưỡng pair đọc từ cấu hình; invitation đếm theo người dùng, không theo IP; ngưỡng ≤ 0 → API dừng khởi động) · full suite 176/176 (90 unit + 86 integration) · build 0W/0E | Rate limiter có sẵn của ASP.NET Core, sliding window (6 segment), policy `login`/`pair`/`invitation-create`/`invitation-accept`, ngưỡng mặc định D-14, ghi đè qua section `RateLimiting:<Policy>:PermitLimit|WindowSeconds`. `Retry-After` = cả cửa sổ (sliding window không trả metadata; giá trị an toàn, không báo thử lại quá sớm). Mới gắn vào `login`; pair/accept/invitation gắn khi làm P2-04/P2-09. Giới hạn: bộ đếm trong bộ nhớ từng process; sau reverse proxy cần ForwardedHeaders. **D-28 mới**: ngưỡng cho register/forgot/resend/verify/reset chưa có trong hợp đồng — chưa áp |

---

## 5. Phase 2 — Thiết bị, ghép QR, người thân

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P2-01](PLAN.md#p2-01) | Entity Devices/PairingCodes/Compartments | `DONE` | P0-02 | `DeviceProvisioningTests` unit 7/7 (đúng 3 ngăn slot 1–3 tên Sáng/Trưa/Tối, owner NULL, trim, chặn rỗng/quá dài) · integration 2/2 (lưu device + đúng 3 ngăn; `DeviceId` trùng → không tạo ngăn cho lần lỗi) · full suite 89/89 · build 0W/0E | Entities + migration có từ `InitialSchema` (không cần migration mới). Bổ sung `DeviceProvisioning` (Domain) + `DeviceProvisioningService` (Infrastructure, một `SaveChanges` = một transaction), DI scoped. `DeviceApiKeyHash` chưa lộ ra API (chưa có endpoint) |
| [P2-02](PLAN.md#p2-02) | Provision & sinh khóa thiết bị | `DONE` | P2-01, P0-05 | `PairingCodeTests` 17/17 · `DeviceApiKeyTests` 13/13 · `DeviceOnboardingTests` 4/4 (chỉ lưu hash, đúng 3 ngăn, serial trùng không để lại dữ liệu dở, mã mới làm hết hạn mã cũ, hộp đã ghép/không tồn tại → null) · smoke CLI trên LocalDB tạm (provision, issue-code, serial trùng exit 1, usage exit 2) · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit 1af6891 | CLI `tools/SmartMedBox.Provisioning` (`provision <serial>`, `issue-code <deviceId>`) qua `DeviceOnboardingService`; khóa 32 byte in một lần, chỉ lưu HMAC (`Security:TokenPepper`); mã Crockford 8 ký tự TTL 15 phút, chuẩn hóa I/L→1, O→0 [ĐỀ XUẤT]; audit không chứa secret. Chưa có test tự động cho CLI. **D-29 mới**: TTL 15 phút vs nhãn in sẵn |
| [P2-03](PLAN.md#p2-03) | Payload QR & in nhãn | `IN_PROGRESS` | P2-02 | `PairingCode.QrPayload` → `{"v":1,"code":...}` + định dạng nhãn `XXXX-XXXX` (trong `PairingCodeTests`) · commit 1af6891 | Còn: sinh ảnh QR (cần chọn thư viện), mẫu nhãn, quy trình in, test quét mẫu; phụ thuộc D-20, D-29 |
| [P2-04](PLAN.md#p2-04) | API ghép hộp atomic, một lần | `DONE` | P2-01, P2-02, P1-02, P1-05 | `DevicePairingApiTests` 11/11 (thành công + audit; quét lại 409 `PAIRING_CODE_USED`; hết hạn 410; mã lạ/hỏng cùng 400; email chưa xác minh 403; đã có hộp 409; hộp đã có chủ 409; 4 request đồng thời cùng mã → đúng 1 thành công; một người ghép 3 hộp đồng thời → chỉ sở hữu 1; 429 + `Retry-After`; ẩn danh 401) · full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E · commit bae066f | `DeviceOwnershipService.PairAsync`: một transaction, cập nhật có điều kiện mã (`UsedAt IS NULL AND ExpiresAt > now`) + chủ (`OwnerUserId IS NULL`) + bắt unique violation; audit `DevicePaired` không chứa mã/hash; response không có key/hash/DeviceId. [ĐỀ XUẤT] thứ tự lỗi: EMAIL_NOT_VERIFIED → INVALID → USED → EXPIRED → USER_ALREADY_OWNS_DEVICE → DEVICE_ALREADY_PAIRED; `code` rỗng → `VALIDATION_ERROR` |
| [P2-05](PLAN.md#p2-05) | API trạng thái hộp của tôi | `DONE` | P2-04 | `MyDeviceApiTests` 3/3 (owner thấy hộp + 3 ngăn, không có secret; relative/người ngoài 404 `DEVICE_NOT_FOUND`; ẩn danh 401) · full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E · commit c2f9984 | `isOnline` luôn `null` cho tới khi chốt D-12; trả `lastSeenAt` |
| [P2-06](PLAN.md#p2-06) | Xác thực thiết bị | `DONE` | P2-02 | `DeviceAuthenticationTests` 4/4 (header hợp lệ; 7 ca thiếu/sai/không tồn tại cùng 401 `DEVICE_UNAUTHORIZED`; cookie người dùng không vào được endpoint thiết bị; header thiết bị không vào được endpoint người dùng) · `DeviceApiKeyTests` · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit 3a98f65 | Scheme `Device` riêng, so sánh constant-time, vẫn tính HMAC khi thiết bị không tồn tại; không log header. Chưa có test bắt log chứng minh header không bị ghi |
| [P2-07](PLAN.md#p2-07) | Heartbeat Unpaired/Paired | `DONE` | P2-06 | `HeartbeatApiTests` 4/4 (Unpaired + cập nhật `LastSeenAt`; Paired kèm `scheduleVersion`; gọi lặp chỉ đổi `LastSeenAt`; sai khóa 401 không đụng `LastSeenAt`) · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit b5c20d2 | `POST /api/device/heartbeat` trả `pairingState`/`serverTimeUtc`/`scheduleVersion` (0 khi Unpaired)/`nextHeartbeatSeconds` = 60 (shortcut, gắn D-12/D-26). `firmwareVersion` chưa lưu (không có cột); `rtcTimeUtc` chưa dùng (D-26) |
| [P2-08](PLAN.md#p2-08) | Entity CareRelationships/RelativeInvitations | `DONE` | P2-01, P1-01 | `RelativeInvitationTests` 3/3 (email chuẩn hóa trim/lowercase, chặn rỗng) · `SchemaConstraintTests` bổ sung: pending trùng sau chuẩn hóa bị chặn; revoked cho phép mời lại + giữ lịch sử; `ExpiresAt` NOT NULL; relationship revoke giữ lịch sử + tạo active mới · full suite 96/96 · build 0W/0E | Entities/migration có từ `InitialSchema`; bổ sung setter chuẩn hóa `InviteeEmail` trong entity. D-14 đã chốt sau task: hạn lời mời 7 ngày — áp `ExpiresAt` khi tạo invitation ở P2-09 |
| [P2-09](PLAN.md#p2-09) | API lời mời người thân | `DONE` | P2-08, P1-06, P0-04 | `RelativeInvitationApiTests` 17/17 (owner tạo 201 + hash + email + audit; relative/người ngoài 403; ẩn danh 401; Pending trùng 409; đã là người thân 409; email của mình/sai 400; accept + dùng lại 409; sai email 403; hết hạn 410; token lạ/hỏng cùng 404; người chưa có tài khoản đăng ký→xác minh→accept; accept đồng thời đúng 1 thành công; create/accept 429) · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit 10ebde0 | `RelativeInvitationService`; token `SecureToken` + HMAC, hạn 7 ngày (D-14); accept một transaction (cập nhật có điều kiện) + audit. [ĐỀ XUẤT]: lời mời Pending quá hạn chuyển `Expired` khi mời lại; list hiển thị quá hạn là `Expired`; email chỉ chứa mã (chưa có link — D-30). **Mâu thuẫn tài liệu**: khóa rate limit accept §1.6 (IP) vs §6.4 (token + người gọi) — đang theo §1.6, cần chốt |
| [P2-10](PLAN.md#p2-10) | API quan hệ & ngừng theo dõi | `DONE` | P2-09, P1-05 | `CareRelationshipApiTests` 6/6 (owner thấy đủ + email; relative chỉ thấy quan hệ của mình, không lộ email người khác; người ngoài/đã thu hồi 403; owner thu hồi giữ lịch sử + mất quyền ngay; relative tự ngừng theo dõi; người khác 403, không tồn tại 404) · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit 4e42976 | `CareRelationshipService`; Active→Revoked có điều kiện, không xóa vật lý. [ĐỀ XUẤT]: owner thấy cả quan hệ Revoked; DELETE lặp → 404 |

---

## 6. Phase 3 — Lịch, lần uống, snapshot

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P3-01](PLAN.md#p3-01) | Entity lịch & thuốc | `DONE` | P2-01 | `MedicationScheduleRulesTests` 9/9 (ít nhất 1 ngày, `EndDate >= StartDate`, DayOfWeek 1–7 không trùng, grace 1–120, timezone bắt buộc) · `SchemaConstraintTests` thêm 8 test (grace 0/121, `EndDate` trước `StartDate`, DayOfWeek 0/8, Quantity ≤ 0, xóa mềm giữ hàng) · migration `20261029081552_AddScheduleDateRangeCheck` · full suite 113/113 · build 0W/0E | Entities + composite PK có từ `InitialSchema`; bổ sung check `CK_MedicationSchedules_DateRange` + `MedicationScheduleRules` (Domain). D-10 đã chốt sau task: `TimeZoneId` IANA theo từng lịch, mặc định `Asia/Ho_Chi_Minh` — cần áp khi làm P3-02 (validation IANA). D-22 đã chốt, D-23 còn TBD |
| [P3-02](PLAN.md#p3-02) | CRUD lịch & ScheduleVersion | `IN_PROGRESS` | P3-01, P1-05 | `SchedulesApiTests` 20/20 (owner CRUD + version 1→4; relative GET 200 / ghi 403; người ngoài 403; POST không có hộp 409 `DEVICE_NOT_PAIRED`; timezone không IANA 400; slot 0/4/hộp khác 400 `COMPARTMENT_INVALID`; 11 ca `VALIDATION_ERROR`; xóa mềm; audit không chứa dữ liệu thuốc; PUT/DELETE không đổi `DoseInstances`/snapshot — AC-10/D-22) · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit 3fc26d3 | Code + test xong; **chặn DONE bởi D-23** (chưa có luật chồng lấn). `ScheduleVersion` tăng nguyên tử bằng `ExecuteUpdate` cùng transaction với audit. Chưa có `If-Match` [ĐỀ XUẤT]. **Lệch schema**: `MedicationSchedules` thiếu `DeletedAt`/`UpdatedAt`/`CreatedAt` so với AGENTS §3.3 / API_CONTRACT §5 — cần duyệt migration `AddScheduleTimestamps` |
| [P3-03](PLAN.md#p3-03) | Sinh DoseInstances + snapshot | `TODO` | P3-01, P0-04 | | AC-10; D-22 đã chốt; còn D-11, D-23 |
| [P3-04](PLAN.md#p3-04) | Đồng bộ lịch cho thiết bị | `TODO` | P3-01, P3-02, P2-07 | | timezone từng lịch (D-10 đã chốt), D-22 đã chốt |
| [P3-05](PLAN.md#p3-05) | Máy trạng thái lần uống | `TODO` | P3-03 | | D-24 |
| [P3-06](PLAN.md#p3-06) | API dashboard & lịch sử | `TODO` | P3-05, P1-05 | | AC-06 |

---

## 7. Phase 4 — Sự kiện & đồng bộ offline

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P4-01](PLAN.md#p4-01) | Entity DeviceEvents | `DONE` | P2-01 | Schema có từ `InitialSchema` (UNIQUE `(DeviceId, EventId)`, IX `(DeviceId, OccurredAt)`, CK `EventType`) + `SchemaConstraintTests` sẵn có · `DeviceEventRulesTests` 12/12 · full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E · commit 03a8f66 | `DeviceEventRules`: eventId không rỗng ≤ 100 ký tự, eventType đúng `LidOpened`/`LidClosed`, batch ≤ 100. Không cần migration |
| [P4-02](PLAN.md#p4-02) | API batch sự kiện idempotent | `IN_PROGRESS` | P4-01, P2-06 | `DeviceEventsBatchApiTests` 9/9 (gửi 3 lần → 1 hàng/eventId, lần sau `Duplicate`; batch xáo cho cùng kết quả, giữ `occurredAt`; lẫn hợp lệ/không hợp lệ → hợp lệ được lưu, lỗi báo từng phần tử; cookie/thiếu header 401; hộp chưa ghép 409; 0/101 phần tử 400; 2 batch đồng thời giống nhau → không trùng, không 500) · full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E · commit 009744d | Code + test xong; **chặn DONE bởi D-26** (chưa có ngưỡng từ chối event tương lai/quá cũ — đánh dấu `[TBD-D26]` trong code). Không ghép với dose (P4-03), không cập nhật `LastSeenAt` (chỉ heartbeat). [ĐỀ XUẤT]: thiếu `occurredAt`/sai slot → lỗi từng phần tử; `batchId` nhận nhưng không lưu; không audit |
| [P4-03](PLAN.md#p4-03) | Ghép sự kiện với lần uống | `TODO` | P4-02, P3-05 | | AC-02, AC-03, D-24 |
| [P4-04](PLAN.md#p4-04) | Kết nối & trạng thái Unknown | `TODO` | P3-05, P2-07, P4-03 | | AC-06, D-12; đánh giá tại DeadlineAt |
| [P4-05](PLAN.md#p4-05) | Đặc tả hợp đồng firmware | `TODO` | P0-03, P3-04, P4-02 | | Bàn giao HW track, D-25, D-26 |

---

## 8. Phase 5 — Cảnh báo & email

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P5-01](PLAN.md#p5-01) | Entity Alerts/AlertRecipients/EmailDeliveries | `DONE` | P3-01, P2-08 | `SchemaConstraintTests` 4 test cảnh báo (chặn recipient trùng `(AlertId, RecipientUserId, Channel)`, chặn 2 alert cùng loại/dose, chặn attempt trùng số, retry thêm attempt không thêm recipient) · full suite 114/114 · build 0W/0E | Entities + migration + index `EmailDeliveries(Status, NextAttemptAt)` có từ `InitialSchema`; D-09 đã chốt. [ĐỀ XUẤT] `LockedBy` chưa thêm — P5-03 sẽ claim bằng `LockedUntil` + điều kiện nguyên tử, bổ sung nếu cần |
| [P5-02](PLAN.md#p5-02) | Job đánh giá quá hạn & tạo cảnh báo | `TODO` | P5-01, P3-05, P4-04, P2-08 | | AC-09, D-24; transaction alert+recipients |
| [P5-03](PLAN.md#p5-03) | Worker gửi email & retry độc lập | `TODO` | P5-01, P0-04 | | D-03, D-13; claim/lease, 2 worker |
| [P5-04](PLAN.md#p5-04) | Mẫu email & nội dung bắt buộc | `TODO` | P5-03 | | AC-06 |
| [P5-05](PLAN.md#p5-05) | API cảnh báo & đánh dấu đã đọc | `TODO` | P5-01, P5-02, P1-05 | | D-24 `resolvedAt` |

---

## 9. Phase 6 — Ảnh đơn thuốc

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P6-01](PLAN.md#p6-01) | Entity ảnh & storage private | `DONE` | P0-04, P1-05 | `PrescriptionStorageTests` 3/3 (không có cột binary; thư mục lưu ngoài WebRoot, file không truy cập được qua URL tĩnh → 404; chỉ owner có hộp) · full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E · commit ba4f715 | `PrescriptionImageService`; giữ `LocalFileStorage` private (D-04 vẫn TBD, theo [ĐỀ XUẤT]); không cần migration |
| [P6-02](PLAN.md#p6-02) | Upload validation (5 MB, magic bytes, EXIF) | `DONE` | P6-01 | `ImageSanitizerTests` 7/7 (magic bytes bỏ qua phần mở rộng; JPEG bỏ EXIF GPS/XMP/COM/APP sau scan/dữ liệu sau EOI; PNG chỉ giữ chunk ảnh; WebP bỏ EXIF/XMP + sửa header; file hỏng → invalid) · `PrescriptionUploadApiTests` 9/9 (owner 201; relative/người ngoài 403; ẩn danh 401; .exe đổi tên 415; JPEG hỏng 400; 5,242,881 B và 6 MB → 413; đúng 5 MB → 201; file lưu không còn EXIF/GPS; audit không có tên file; DB lỗi không để file mồ côi) · full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E · commit fb6b464 | Loại metadata bằng cách viết lại JPEG/PNG/WebP theo allow-list, không thêm thư viện; `contentType` theo magic bytes; `sizeBytes` = sau khi loại metadata. **Lệch hợp đồng**: API_CONTRACT §8/§12 yêu cầu ảnh "giải mã được" — hiện chỉ kiểm cấu trúc, không giải mã pixel (cần duyệt thư viện ảnh hoặc chấp nhận). JPEG bỏ ICC (APP2) có thể lệch màu nhẹ. D-16 vẫn TBD (không chống trùng) |
| [P6-03](PLAN.md#p6-03) | Xem/tải ảnh có kiểm tra quyền | `TODO` | P6-02 | | AC-11 |
| [P6-04](PLAN.md#p6-04) | Xóa mềm & audit ảnh | `TODO` | P6-03 | | D-16 |
| [P6-05](PLAN.md#p6-05) | Đối chiếu file & runbook backup | `TODO` | P6-01 | | |

---

## 10. Phase 7 — Hoàn thiện & nghiệm thu

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [P7-01](PLAN.md#p7-01) | Phủ audit thao tác quan trọng | `TODO` | P2-04, P2-09, P3-02, P5-02, P6-04 | | |
| [P7-02](PLAN.md#p7-02) | Bộ test nghiệm thu AC-01…AC-12 | `TODO` | P1–P7-01, HW-02…HW-04, UI-01…UI-07 | QA baseline 2026-10-29: [TEST_PLAN.md](TEST_PLAN.md) 82 case · [TEST_REPORT.md](TEST_REPORT.md) 1 PASS / 0 FAIL / 8 BLOCKED / 73 NOT_RUN | Theo dõi tại §13; harness E2E chưa có (DEF-13) |
| [P7-03](PLAN.md#p7-03) | Rà soát bảo mật & hardening | `TODO` | P1–P6 | | |
| [P7-04](PLAN.md#p7-04) | Observability & health | `DONE` | P0-04 | `HealthCheckTests` 2/2 (live 200; ready 503 khi mất DB, body chỉ `Unhealthy`) · `HealthCheckDatabaseTests` 1/1 · `TraceIdLogScopeTests` 1/1 (scope `TraceId` = `traceId` của ProblemDetails; không log query/header) · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit 7026a27 | `/health/live`, `/health/ready` (DB + storage), health checks có sẵn, không thêm package; `appsettings.Production.json` chặt hơn. `/health/ready` công khai, không rate limit — chặn ở proxy khi triển khai thật. D-19 vẫn TBD |
| [P7-05](PLAN.md#p7-05) | Bộ demo & tài liệu bàn giao | `TODO` | P7-02 | | |
| [P7-06](PLAN.md#p7-06) | Runbook vận hành & chính sách dữ liệu | `TODO` | P6-05 | | D-17 |

---

## 11. Web UI track

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [UI-01](PLAN.md#ui-01) | Trang xác thực tài khoản | `DONE` | P1-02…P1-04, P0-01 | `AccountPagesTests` 20/20 (6 trang GET 200 + antiforgery; 7 form thiếu CSRF → 400; chuỗi AC-12 qua form; thông báo trung tính; lỗi `PasswordPolicy`; login 429) · **trình duyệt thật 2026-11-09** (Kestrel HTTPS + LocalDB): đăng ký (lỗi mật khẩu yếu hiển thị) → xác minh bằng mã trong email sink → đăng nhập → trang tài khoản → đăng xuất → `/account` chuyển về login; desktop + mobile 375px không cuộn ngang · full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E · commit 78ad493 | Razor `/account/*` (D-01); `LoginService` + `SessionCookie` dùng chung với API. Quên/đặt lại mật khẩu mới kiểm bằng test HTTP, chưa bấm thử trên trình duyệt. Email chỉ chứa mã, không link (D-30) |
| [UI-02](PLAN.md#ui-02) | Ghép hộp & trạng thái thiết bị | `TODO` | P2-04, P2-05, P2-07, UI-01 | | |
| [UI-03](PLAN.md#ui-03) | Quản lý lịch uống | `TODO` | P3-02, UI-01 | | |
| [UI-04](PLAN.md#ui-04) | Dashboard & lịch sử | `TODO` | P3-06, P4-04, UI-02 | | AC-06 |
| [UI-05](PLAN.md#ui-05) | Người thân & cảnh báo | `TODO` | P2-09, P2-10, P5-05, UI-01 | | |
| [UI-06](PLAN.md#ui-06) | Ảnh đơn thuốc | `TODO` | P6-02…P6-04, UI-01 | | AC-11 |
| [UI-07](PLAN.md#ui-07) | Rà soát responsive & thuật ngữ | `TODO` | UI-01…UI-06 | | Không còn "đã uống" |

---

## 12. Hardware / firmware track

> [TBD-D06] Code firmware có thể nằm ngoài repo này; checklist vẫn theo dõi để đảm bảo tích hợp.

| ID | Task | Trạng thái | Phụ thuộc | Bằng chứng / Test | Ghi chú |
|---|---|---|---|---|---|
| [HW-01](PLAN.md#hw-01) | Sơ đồ khối, BOM, pin map, nguồn | `TODO` | — | | D-25 onboarding |
| [HW-02](PLAN.md#hw-02) | Firmware nhắc theo RTC, đèn/còi đúng ngăn | `TODO` | HW-01 | | AC-01, AC-04, D-26 |
| [HW-03](PLAN.md#hw-03) | Cạnh nắp, nắp mở sẵn, hàng đợi sự kiện | `TODO` | HW-02 | | AC-02, AC-03, AC-04; trạng thái nắp khi reboot |
| [HW-04](PLAN.md#hw-04) | Wi‑Fi, heartbeat, sync lịch, gửi batch | `TODO` | HW-03, P2-07, P3-04, P4-02 | | AC-05, D-25, D-26 |
| [HW-05](PLAN.md#hw-05) | In QR & quy trình provision nhãn | `TODO` | P2-02, P2-03 | | AC-08 |

---

## 13. Tiêu chí nghiệm thu (theo dõi riêng)

<a id="acceptance"></a>

> Chỉ tick khi có test/bằng chứng tương ứng. Xem [PLAN.md §16](PLAN.md#traceability).
>
> Cột **Trạng thái** dùng quy ước QA của [TEST_PLAN.md §2](TEST_PLAN.md#2-quy-ước-trạng-thái): `PASS` / `FAIL` / `BLOCKED` / `NOT_RUN`. Không AC nào được tính đạt khi chưa có bằng chứng chạy thực tế. Kết quả QA ngày 2026-10-29: [TEST_REPORT.md](TEST_REPORT.md).

| AC | Tiêu chí | Trạng thái | Test / Bằng chứng |
|---|---|---|---|
| AC-01 | Nhắc đúng ngăn, đúng giờ; hai ngăn kia không sáng | `BLOCKED` | TC-SYNC-001, TC-DOSE-003 — thiếu thiết bị/simulator (D-06) và API lần uống |
| AC-02 | Xác nhận đúng cửa sổ; sớm/sai ngăn không xác nhận | `NOT_RUN` | TC-DOSE-003…005, TC-DOSE-009 — chưa có P3-05/P4-03 |
| AC-03 | Nắp mở sẵn không tự xác nhận; đóng rồi mở lại hợp lệ | `BLOCKED` | TC-DOSE-006 — cần hành vi firmware/cạnh nắp (HW-03) |
| AC-04 | Offline vẫn nhắc theo RTC và giữ sự kiện | `BLOCKED` | TC-SYNC-001, TC-SYNC-002 — thiếu thiết bị/simulator (D-06) |
| AC-05 | Gửi lại batch không tạo kết quả trùng | `NOT_RUN` | TC-SYNC-003, TC-SYNC-004, TC-SYNC-012 — chưa có P4-02 |
| AC-06 | Offline: Web/email dùng Unknown/"không thể xác nhận" | `NOT_RUN` | TC-ALERT-002, TC-ALERT-008, TC-UI-002 — chưa có P4-04/P5-02/UI-04 |
| AC-07 | Người thân xem được nhưng bị chặn sửa cấu hình/ảnh | `NOT_RUN` | TC-AUTHZ-002…TC-AUTHZ-006 — chưa có endpoint để phân quyền |
| AC-08 | QR dùng một lần; quét lại bị từ chối; không lộ DeviceApiKey | `NOT_RUN` (phần API có test: `DevicePairingApiTests`; chờ UI-02 + quét QR thật) | TC-PAIR-005, TC-PAIR-007, TC-PAIR-009 — chưa có P2-02…P2-04 |
| AC-09 | Email quá hạn đủ chủ + mọi relative Accepted; retry không nhân đôi | `NOT_RUN` | TC-ALERT-003, TC-ALERT-006 — chưa có P5-02/P5-03, D-03 chưa chốt |
| AC-10 | Snapshot không đổi ngược khi sửa lịch/thuốc | `NOT_RUN` | TC-SCHED-006, TC-DOSE-001, TC-DOSE-008 — chưa có P0-02/P3-03 |
| AC-11 | Ảnh sai loại/quá 5 MB bị từ chối; loại EXIF; file ngoài DB | `NOT_RUN` | TC-IMG-002…TC-IMG-007 — chưa có P6-*, D-04 chưa chốt |
| AC-12 | Đăng ký → xác minh → đăng nhập → quên/đặt lại hoạt động | `NOT_RUN` | TC-AUTH-001, TC-AUTH-004…007 — chuỗi API đã có test đầu-cuối `AccountFlow_RegisterVerifyLoginForgotReset_EndToEnd` (P1-02…P1-04); chưa kiểm chứng UI (UI-01) nên chưa đạt theo QA |

### 13.1. Kịch bản kiểm thử tối thiểu (dùng cho P7-02)

| AC | Setup → Hành động → Kỳ vọng (đo được) |
|---|---|
| AC-01 | Lịch 07:00 ngăn 1, grace 15. Tại 07:00: chỉ LED ngăn 1 sáng + còi; LED ngăn 2/3 tắt; log/telemetry ghi đúng slot 1. |
| AC-02 | Dose 12:00–12:15 ngăn 2. (a) mở ngăn 2 lúc 11:59 → không `Confirmed`; (b) mở ngăn 1 lúc 12:05 → không; (c) mở ngăn 2 lúc 12:05 → `Confirmed`, `confirmedAt=12:05`; (d) mở đúng 12:15 → `Confirmed` (biên inclusive). |
| AC-03 | Nắp ngăn 2 mở từ 11:50. Lúc 12:05 vẫn mở → không `Confirmed`. Đóng 12:06, mở lại 12:07 → `Confirmed`; event 11:50 vẫn được lưu. |
| AC-04 | Ngắt Wi‑Fi trước 07:00. 07:00 thiết bị vẫn sáng đèn/còi theo RTC; mở đúng ngăn; event nằm trong queue; reconnect → batch gửi lên, server ghi 1 event. |
| AC-05 | Gửi cùng batch (gồm 1 event mở hợp lệ) 3 lần. DB có đúng 1 `DeviceEvents`/`eventId`, 1 `Confirmed`, không thêm alert/recipient; lần 2–3 trả `Duplicate`. |
| AC-06 | Thiết bị offline tại `DeadlineAt`. Dashboard `Unknown`; email/web dùng "không thể xác nhận do mất kết nối"; không có chuỗi "quên uống"/"bỏ liều". |
| AC-07 | Relative Accepted: GET schedules/dashboard/history/alerts → 200; POST/PUT/DELETE schedule → 403; invitation tạo/revoke → 403; ảnh content → 403. Owner làm cùng thao tác → thành công. |
| AC-08 | Pair mã lần 1 → 200; quét lại → 409 `PAIRING_CODE_USED`; response + QR không chứa `DeviceApiKey`/hash; 2 request đồng thời → đúng 1 thành công. |
| AC-09 | Owner + 2 relative Accepted + 1 relative đã revoke. Dose online quá hạn → alert có đúng 3 recipient; email sink nhận 3; cho 1 địa chỉ lỗi và retry → người khác không nhận thêm bản thứ 2. |
| AC-10 | Sinh dose 07:00/15 phút với thuốc A. Sửa lịch thành 08:00/30 phút, thuốc B. Dose cũ + snapshot vẫn 07:00/15/A; lịch sử/dashboard không đổi; chỉ lịch hiện hành đổi. |
| AC-11 | Upload `.exe` đổi tên `.jpg` → 415; file 5,242,881 bytes → 413; JPEG có EXIF GPS → file lưu không còn EXIF; DB không có binary; file không nằm trong `wwwroot`; relative tải → 403. |
| AC-12 | Đăng ký → nhận email xác minh → xác minh → đăng nhập; quên mật khẩu → nhận token → đặt lại → đăng nhập bằng mật khẩu mới; token cũ dùng lại bị từ chối. |

### 13.2. Kết quả QA baseline — 2026-10-29

> Chi tiết: [TEST_PLAN.md](TEST_PLAN.md) (82 test case) và [TEST_REPORT.md](TEST_REPORT.md) (kết quả + defect log).

| Chỉ số | Giá trị |
|---|---|
| Tổng test case | 82 |
| Đã chạy | 1 (TC-SMOKE-001) |
| `PASS` | 1 |
| `FAIL` | 0 |
| `BLOCKED` | 8 (TC-SCHED-007, TC-SCHED-009, TC-DOSE-011, TC-SYNC-001, TC-SYNC-002, TC-SYNC-006, TC-SYNC-011, TC-ALERT-009) |
| `NOT_RUN` | 73 |
| AC đạt | 0/12 |

Kết luận: **MVP NOT ACCEPTED**. Nguyên nhân gốc: P0-02…P7-06 chưa triển khai (API nghiệp vụ trả `404`), `QA-ENV-1` thiếu SQL Server/email sink/clock/simulator, và D-22/D-23/D-24/D-26 chưa chốt. Không tick cổng M1–M5. Xem danh sách lỗi cần sửa tại [TEST_REPORT.md §7–§9](TEST_REPORT.md#7-defect-log-lỗi-cần-sửa).

---

## 14. Nhật ký bằng chứng (append-only)

> Thêm một dòng mỗi khi một task chuyển `DONE` hoặc một cổng mốc được tick. Không sửa/xóa dòng cũ.

| Ngày | Task/Mốc | Từ → Đến | Bằng chứng (test/commit/artifact) | Người thực hiện |
|---|---|---|---|---|
| 2026-10-29 | Khởi tạo tài liệu | — → hoàn tất kế hoạch | 4 tài liệu PLAN/TASKS/API_CONTRACT/AGENTS | Agent |
| 2026-10-29 | P0-01 | `TODO` → `DONE` | Build 0W/0E; `HomePageTests.Get_Root_ReturnsHtmlPage` 1/1; HTTP 200 HTML + CSS 200; D-01/D-02 chốt trong PLAN §3 | Agent |
| 2026-10-29 | QA baseline | — → 1 PASS / 82 | `dotnet build` 0W/0E; `dotnet test` 1/1 pass; `GET /api/users/me` & `/api/schedules` → 404; [TEST_PLAN.md](TEST_PLAN.md) + [TEST_REPORT.md](TEST_REPORT.md); 0/12 AC đạt | QA Automation |
| 2026-10-29 | P0-02 | `TODO` → `DONE` | Migration `20261029070908_InitialSchema` apply trên DB LocalDB trống; `SchemaConstraintTests` 21/21; full suite 22/22; build 0W/0E; [docs/erd.md](docs/erd.md) + [docs/schema.sql](docs/schema.sql) | Agent |
| 2026-10-29 | P0-03 | `TODO` → `DONE` | [API_CONTRACT.md](API_CONTRACT.md) v1.4 (D-07/D-08/D-09, §1.4/§1.5, error catalog); `SharedEnumContractTests` 7/7; full suite 29/29; build 0W/0E | Agent |
| 2026-10-29 | P0-04 | `TODO` → `DONE` | `ProblemDetailsTests` 7/7, `SystemClockTests` 1/1, `FileEmailSenderTests` 2/2, `LocalFileStorageTests` 11/11; full suite 50/50; build 0W/0E; [API_CONTRACT.md](API_CONTRACT.md) v1.4.1 (405); Kestrel smoke: `/` 200, CSS 200, `/api/xyz` 404 problem+json | Agent |
| 2026-10-29 | P0-05 | `TODO` → `DONE` | Quét secret: 0 khớp mẫu credential (chỉ LocalDB không mật khẩu); export 69 file từ working tree → build 0W/0E, Kestrel `/` 200 HTML + CSS 200; full suite 50/50; [README.md](README.md) hướng dẫn user-secrets/env; đặc tả copy vào [docs/requirements/](docs/requirements/Hop-thuoc-thong-minh.html) | Agent |
| 2026-10-29 | P1-01 | `TODO` → `DONE` | `EmailNormalizerTests` 8/8; `PasswordHasherServiceTests` 8/8; `ApplicationUserTests` 2/2; full suite 68/68; build 0W/0E; không cần migration mới | Agent |
| 2026-10-29 | P1-03 | `TODO` → `DONE` | `AuthApiTests` 6/6; `InMemoryUserSessionStoreTests` 6/6; full suite 80/80 (43 unit + 37 integration); build 0W/0E; CSRF global filter + `GET /api/users/me`; D-27 ghi nhận tại PLAN/API_CONTRACT | Agent |
| 2026-10-29 | P2-01 | `TODO` → `DONE` | `DeviceProvisioningTests` unit 7/7 + integration 2/2; full suite 89/89 (50 unit + 39 integration); build 0W/0E; `DeviceProvisioning` (Domain) + `DeviceProvisioningService` (Infrastructure); không cần migration mới | Agent |
| 2026-10-29 | P2-08 | `TODO` → `DONE` | `RelativeInvitationTests` 3/3; `SchemaConstraintTests` thêm 4 test (pending trùng sau chuẩn hóa, revoked giữ lịch sử, `ExpiresAt` NOT NULL, relationship revoke/re-accept); full suite 96/96; build 0W/0E; không cần migration mới | Agent |
| 2026-10-29 | P3-01 | `TODO` → `DONE` | `MedicationScheduleRulesTests` 9/9; `SchemaConstraintTests` thêm 8 test; migration `20261029081552_AddScheduleDateRangeCheck`; full suite 113/113 (62 unit + 51 integration); build 0W/0E; [docs/schema.sql](docs/schema.sql) + [docs/erd.md](docs/erd.md) cập nhật | Agent |
| 2026-10-29 | P5-01 | `TODO` → `DONE` | `SchemaConstraintTests` 4 test cảnh báo/recipient/delivery + retry; full suite 114/114 (62 unit + 52 integration); build 0W/0E; không cần migration mới | Agent |
| 2026-10-29 | P1-02 | `TODO` → `DONE` | `RegistrationApiTests` 12/12; `PasswordPolicyTests` 12/12; `SecureTokenTests` 2/2; `HmacTokenHasherTests` 7/7; `SchemaConstraintTests` 36/36; migration `20261029092143_AddAuthTokens`; full suite 149/149 (83 unit + 66 integration); build 0W/0E; [README.md](README.md) hướng dẫn `Security:TokenPepper`; [docs/erd.md](docs/erd.md) + [docs/schema.sql](docs/schema.sql) cập nhật | Agent |
| 2026-10-29 | P1-04 | `TODO` → `DONE` | `PasswordResetApiTests` 10/10 (gồm chuỗi AC-12 API); `InMemoryUserSessionStoreTests` 7/7 (`RevokeAllForUser`); full suite 160/160 (84 unit + 76 integration); build 0W/0E; không cần migration mới; [README.md](README.md) bổ sung endpoint quên/đặt lại | Agent |
| 2026-11-09 | P1-05 | `TODO` → `DONE` | `DeviceAccessPolicyTests` 6/6; `DeviceAccessTests` 5/5; full suite 171/171 (90 unit + 81 integration); build 0W/0E; không cần migration mới | Agent |
| 2026-11-09 | P1-06 | `TODO` → `DONE` | `RateLimitingTests` 5/5; full suite 176/176 (90 unit + 86 integration); build 0W/0E; D-28 ghi nhận tại PLAN §3 | Agent |
| 2026-11-09 | P7-04 | `TODO` → `DONE` | commit 7026a27; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P3-02 | `TODO` → `IN_PROGRESS` | commit 3fc26d3; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | UI-01 | `TODO` → `DONE` | commit 78ad493; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-09 | `TODO` → `DONE` | commit 10ebde0; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-10 | `TODO` → `DONE` | commit 4e42976; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-02 | `TODO` → `DONE` | commit 1af6891; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-03 | `TODO` → `IN_PROGRESS` | commit 1af6891; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-06 | `TODO` → `DONE` | commit 3a98f65; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-07 | `TODO` → `DONE` | commit b5c20d2; full suite 285/285 (120 unit + 165 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P4-01 | `TODO` → `DONE` | commit 03a8f66; full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P4-02 | `TODO` → `IN_PROGRESS` | commit 009744d; full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-04 | `TODO` → `DONE` | commit bae066f; full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P2-05 | `TODO` → `DONE` | commit c2f9984; full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P6-01 | `TODO` → `DONE` | commit ba4f715; full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E | Subagent + Agent review |
| 2026-11-09 | P6-02 | `TODO` → `DONE` | commit fb6b464; full suite 339/339 (139 unit + 200 integration) sau merge · build 0W/0E | Subagent + Agent review |

---

## 15. Việc chưa được phép làm ở giai đoạn này

- Chỉ triển khai task có trong [PLAN.md](PLAN.md), dependency đã `DONE`; không tự thêm task/hạng mục ngoài kế hoạch.
- Không đánh dấu bất kỳ task `DONE`/cổng mốc nào khi chưa có bằng chứng.
- Không tự chốt các TBD trong [PLAN.md §3](PLAN.md#decision-log).

---

## 16. Ghi chú đồng bộ tài liệu

- Khi task hoàn thành mà thay đổi endpoint/schema → cập nhật [API_CONTRACT.md](API_CONTRACT.md) và [PLAN.md](PLAN.md) trước, rồi mới `DONE`.
- Khi phát hiện task thiếu → thêm vào PLAN với ID mới (đúng phase), rồi thêm dòng tương ứng ở đây.
- Khi một task bị tách/gộp → ghi rõ ID thay thế vào cột "Ghi chú", không tái sử dụng ID cũ.

---

## 17. Phụ lục — phân rã task lớn (dùng nội bộ, không thay ID PLAN)

> Các ID con chỉ để chia việc trong một task; trạng thái vẫn ghi ở dòng task cha. Khi tách thành task PLAN thật, cấp ID mới theo quy tắc §16.

### P0-02 · Schema & migration

- [x] P0-02a — 17 entity + quan hệ 1–n/n–n + `DbContext`
- [x] P0-02b — unique/check/filtered index + 4 index đường đọc nóng
- [x] P0-02c — migration `InitialSchema` + test apply trên SQL Server trống
- [x] P0-02d — test ràng buộc (slot 1–3, owner unique, event unique, alert recipient unique)

### P0-03 · Chốt hợp đồng API & enum

- [x] P0-03a — chốt D-07: header, định dạng khóa, HMAC+pepper, constant-time
- [x] P0-03b — chốt D-08: Crockford Base32 8 ký tự, TTL 15 phút, payload QR
- [x] P0-03c — chốt D-09: enum §10.2, `daysOfWeek` ISO-8601, `Channel = Email`
- [x] P0-03d — quy ước ProblemDetails + catalog lỗi + test khóa enum

### P0-04 · Hạ tầng cắt ngang

- [x] P0-04a — `IClock`/`SystemClock` + `FakeClock` cho test
- [x] P0-04b — `IEmailSender` + `FileEmailSender` dev sink (`.eml`, chặn header injection, không log nội dung)
- [x] P0-04c — `IFileStorage` + `LocalFileStorage` (key ngẫu nhiên, chặn path traversal, ngoài `wwwroot`, `App_Data/` gitignore)
- [x] P0-04d — ProblemDetails middleware: exception 500, status-code 400/401/403/404/405/409/413/415/429/5xx, validation 400 camelCase, `traceId`
- [x] P0-04e — logging HTTP chỉ method/path/status/duration; test không ghi header/body/query
- [x] P0-04f — định tuyến: route MVC mặc định loại `/api` + `UseStaticFiles` để 405 không bị fallback static che thành 404
- [x] P0-04g — `ProblemDetailsFactory` tùy biến: 415/4xx do MVC sinh cũng có `code` + `traceId`

### P0-05 · Secret & vệ sinh repo

- [x] P0-05a — `UserSecretsId` cho `SmartMedBox.Api` + mẫu `appsettings.Development.sample.json` (LocalDB, không credential)
- [x] P0-05b — README: user-secrets, biến môi trường `ConnectionStrings__Default`, quy ước `SMARTMEDBOX_CONNECTION`/`SMARTMEDBOX_TEST_SERVER`
- [x] P0-05c — rà `.gitignore` (`.env*`, `*.key`, `*.pem`, `appsettings.*.local.json`, `App_Data/`)
- [x] P0-05d — quét secret working tree: không có credential thật; lưu bản sao đặc tả vào `docs/requirements/`
- [x] P0-05e — xác minh export sạch build 0W/0E + chạy API 200; full suite 50/50

### P1-01 · Entity người dùng & hash mật khẩu

- [x] P1-01a — `EmailNormalizer` (trim/lowercase, giới hạn 320) + áp dụng trong setter `ApplicationUser.Email`
- [x] P1-01b — `IPasswordHasher` + `PasswordHasherService` dùng `PasswordHasher<ApplicationUser>`; không lưu mật khẩu rõ
- [x] P1-01c — test unit chuẩn hóa email/hash và test tích hợp trùng email khác hoa/thường bị chặn
- [x] P1-01d — xác nhận schema hiện có đủ, không cần migration mới; đăng ký DI

### P1-02 · Đăng ký & xác minh email

- [x] P1-02a — `PasswordPolicy` (≥ 8, ≤ 128, chữ + số) + `SecureToken` (32 byte CSPRNG, base64url) + `AuthTokenLifetime` 24 giờ theo D-14
- [x] P1-02b — `AuthToken` entity + cấu hình: check `Purpose`, unique `TokenHash`, index `(UserId, Purpose, ExpiresAt)`, migration `AddAuthTokens`
- [x] P1-02c — `ITokenHasher`/`HmacTokenHasher` (HMAC-SHA256 + pepper `Security:TokenPepper` bắt buộc ≥ 32 ký tự) + `EmailTemplates` tiếng Việt
- [x] P1-02d — `RegistrationService`: đăng ký + token trong một transaction (bắt unique race → `EMAIL_TAKEN`), xác minh dùng một lần nguyên tử, resend trung tính
- [x] P1-02e — `POST /api/auth/register|verify-email|resend-verification` (401/409/410/400 theo API_CONTRACT) + test tích hợp trên HTTPS

### P1-03 · Đăng nhập/đăng xuất/hồ sơ

- [x] P1-03a — cookie authentication (`SmartMedBox.Auth`, `Secure`/`HttpOnly`/`SameSite=Lax`) + antiforgery global (`X-CSRF-TOKEN`), lỗi 401/403 dạng ProblemDetails
- [x] P1-03b — `POST /api/auth/login`: chuẩn hóa email, verify hash, chặn email chưa xác minh, không phân biệt sai mật khẩu/email không tồn tại
- [x] P1-03c — session server-side (`IUserSessionStore`) + `session_id` claim, thu hồi khi logout; `GET /api/users/me` trả hồ sơ + `ownedDevice`
- [x] P1-03d — test tích hợp trên HTTPS (cookie Secure), CSRF thiếu/sai, thu hồi session sau logout; unit test session store
- [ ] P1-03e — chốt D-27 (durable/multi-instance session, TTL) trước khi dùng cho môi trường thật

### P1-04 · Quên & đặt lại mật khẩu

- [x] P1-04a — `ForgotPasswordAsync`: chuẩn hóa email, im lặng với email lạ/không hợp lệ; mỗi lần chỉ token mới nhất còn hiệu lực
- [x] P1-04b — `ResetPasswordAsync`: tiêu thụ token nguyên tử trong transaction, cập nhật hash, vô hiệu các token đặt lại khác
- [x] P1-04c — `IUserSessionStore.RevokeAllForUser` thu hồi mọi phiên sau khi đổi mật khẩu
- [x] P1-04d — `POST /api/auth/forgot-password|reset-password` + `EmailTemplates.PasswordReset` tiếng Việt
- [x] P1-04e — test tích hợp đầy đủ (202 trung tính, token một lần/hết hạn, thu hồi phiên) + chuỗi AC-12 mức API

### P1-05 · Phân quyền theo hộp

- [x] P1-05a — `DeviceAccessRole` + `DeviceAccessPolicy.Allows` (Read: Owner/Relative; Manage: chỉ Owner)
- [x] P1-05b — `DeviceAccessService.GetRoleAsync/CanAsync`: owner theo `Devices.OwnerUserId`, relative theo `CareRelationships` `Active`; hộp chưa ghép/không tồn tại → None
- [x] P1-05c — `CurrentUser.TryGetUserId` lấy userId từ cookie phiên; `UsersController` dùng lại
- [x] P1-05d — test ma trận unit + integration SQL Server + HTTP (403 thống nhất, bỏ qua userId client gửi)

### P2-01 · Entity Devices / PairingCodes / Compartments

- [x] P2-01a — xác nhận entities + migration có từ P0-02, không cần migration mới
- [x] P2-01b — `DeviceProvisioning.Create` seed đúng 3 ngăn slot 1–3 (Sáng/Trưa/Tối), owner NULL, trim/validate độ dài
- [x] P2-01c — `DeviceProvisioningService.ProvisionAsync` lưu device + 3 ngăn trong một transaction; test lỗi `DeviceId` trùng không tạo ngăn
- [x] P2-01d — giữ pass các test ràng buộc sẵn có từ P0-02 (owner unique, `(DeviceId, SlotNumber)` unique, slot 1–3)

### P2-08 · Entity CareRelationships / RelativeInvitations

- [x] P2-08a — xác nhận entities + migration có từ `InitialSchema`; filtered unique `(DeviceId, RelativeUserId)` Active và `(DeviceId, InviteeEmail)` Pending
- [x] P2-08b — chuẩn hóa `InviteeEmail` (trim/lowercase) ngay trong entity để unique index so khớp đúng
- [x] P2-08c — test chặn 2 lời mời Pending trùng (kể cả khác hoa/thường/khoảng trắng); revoked giữ lịch sử và cho phép mời lại
- [x] P2-08d — test `ExpiresAt` NOT NULL; relationship revoke giữ hàng cũ + tạo Active mới; chặn 2 Active trùng

### P2-09 · Lời mời người thân

- [x] P2-09a — entity/validation + chặn trùng Pending/relationship
- [x] P2-09b — tạo/revoke/list invitation + audit
- [x] P2-09c — email token một lần + flow người chưa có tài khoản
- [x] P2-09d — accept transaction + sai email/hết hạn/dùng lại + rate limit

### P3-01 · Entity lịch & thuốc

- [x] P3-01a — xác nhận entities + PK kép `ScheduleDays` có từ `InitialSchema`
- [x] P3-01b — thêm check `CK_MedicationSchedules_DateRange`, migration `AddScheduleDateRangeCheck`, regenerate [docs/schema.sql](docs/schema.sql)
- [x] P3-01c — `MedicationScheduleRules.EnsureValid`: ít nhất 1 ngày, DayOfWeek 1–7 không trùng, grace 1–120, timezone bắt buộc
- [x] P3-01d — test DB: grace/DayOfWeek/quantity ngoài miền bị chặn, `EndDate < StartDate` bị chặn, xóa mềm giữ hàng

### P3-03 · Sinh dose & snapshot

- [ ] P3-03a — tính occurrence theo timezone/ngày/khung hiệu lực
- [ ] P3-03b — insert idempotent + `DoseItemSnapshots` + grace copy
- [ ] P3-03c — chạy lặp/song song + sinh bù sau downtime
- [ ] P3-03d — test AC-10 + D-22 (không đổi dose đã sinh)

### P4-02 · Nhận batch sự kiện

- [x] P4-02a — auth thiết bị + validation batch/phần tử + giới hạn
- [x] P4-02b — xử lý theo thứ tự `occurredAt`/`eventId`, idempotent
- [x] P4-02c — kết quả từng phần tử + phần tử lỗi không rollback phần tử hợp lệ
- [ ] P4-02d — test gửi lặp 3 lần, batch xáo thứ tự, event tương lai

### P5-01 · Entity Alerts / AlertRecipients / EmailDeliveries

- [x] P5-01a — xác nhận entities + migration + unique `(AlertId, RecipientUserId, Channel)` và `(DoseInstanceId, AlertType)` có từ `InitialSchema`
- [x] P5-01b — `EmailDeliveries` có `AttemptNumber`/`NextAttemptAt`/`SentAt`/`ProviderMessageId`/`LockedUntil`; unique `(AlertRecipientId, AttemptNumber)`; index `(Status, NextAttemptAt)`
- [x] P5-01c — test chặn recipient trùng, 2 alert cùng loại, 2 attempt cùng số
- [x] P5-01d — test retry thêm `EmailDeliveries` mới mà không tạo recipient mới

### P5-02 · Job cảnh báo

- [ ] P5-02a — query dose đến hạn + xác định online/offline tại `DeadlineAt`
- [ ] P5-02b — tạo alert + recipients trong transaction, idempotent
- [ ] P5-02c — chọn đúng AlertType + không alert dose Confirmed
- [ ] P5-02d — test AC-09, chạy job 2 instance, revoke trước đánh giá

### P5-03 · Email worker

- [ ] P5-03a — claim delivery bằng lease, chống 2 worker
- [ ] P5-03b — gửi + lưu `SentAt`/`ProviderMessageId`/attempt
- [ ] P5-03c — backoff, max attempts, `Exhausted`, cô lập lỗi
- [ ] P5-03d — test retry một người không ảnh hưởng người khác + crash mô phỏng

### P7-02 · Nghiệm thu

- [ ] P7-02a — harness E2E API + DB + email sink + clock giả
- [ ] P7-02b — simulator thiết bị cho AC-01…AC-05
- [ ] P7-02c — UI browser test cho AC-06/07/11/12
- [ ] P7-02d — báo cáo 12/12 + gap chưa kiểm chứng trên phần cứng thật
