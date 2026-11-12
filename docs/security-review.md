# Rà soát bảo mật & hardening — P7-03

> Ngày: 2026-11-12 · Phạm vi: `src/SmartMedBox.Api`, `src/SmartMedBox.Infrastructure`, cấu hình và tệp được git theo dõi.
> Đối chiếu: AGENTS.md §4.3, §4.5, §7, §8; API_CONTRACT.md §1.2–§1.6, §11.
> Người rà soát (tự động): AI Agent. **Chữ ký người duyệt: ____________________ (chưa ký)**

Lệnh chạy test bảo mật:

```bash
export SMARTMEDBOX_TEST_SERVER='Server=.\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True'
dotnet test tests/SmartMedBox.IntegrationTests --filter "FullyQualifiedName~SmartMedBox.IntegrationTests.Security"
```

## 1. Checklist DoD

| # | Mục DoD / đầu ra PLAN | Bằng chứng | Trạng thái |
|---|---|---|---|
| 1 | Ma trận quyền **từng endpoint**; test quyền trên **tất cả** endpoint | `Security/EndpointAuthorizationMatrixTests`: liệt kê mọi `RouteEndpoint` từ `EndpointDataSource` của app thật (loại probe của test), so với bảng phân loại `Anonymous / User / OwnerOnly / UserPage / Device / Health`. Endpoint mới chưa phân loại hoặc phân loại thừa → fail (đã thử xóa 1 dòng: test fail với "Endpoint chưa phân loại quyền: GET api/users/me"). Theo từng nhóm: anonymous gọi API người dùng → `401 UNAUTHORIZED`, trang Razor → 302 `/account/login`. Phân quyền chủ/người thân/người lạ theo hộp: `DeviceAccessTests.Endpoints_AuthorizationMatrix_OwnerManagesRelativeReadsOthersForbidden`, `SchedulesApiTests.Endpoints_Relative_ReadsButWritesForbidden`, `RelativeInvitationApiTests.InvitationEndpoints_RelativeOrStranger_Return403`, `PrescriptionUploadApiTests.Upload_RelativeOrStranger_Returns403`, `PrescriptionDeleteApiTests.Delete_RelativeOrStranger_ReturnsForbidden`, `PrescriptionPagesTests.Get_RelativePage_ReturnsForbiddenProblemDetails`, `CareRelationshipApiTests.*`, `AlertsApiTests.List_RecipientRevokedAfterAlertCreated_ReturnsForbidden` | ĐẠT |
| 2 | Thiết bị không gọi được API người dùng; cookie không gọi được API thiết bị | `EndpointAuthorizationMatrixTests.UserEndpoints_WithValidDeviceCredentials_StillRejected` (khóa thiết bị **hợp lệ** trên mọi endpoint User/OwnerOnly/UserPage → 401/302), `DeviceEndpoints_WithUserCookieAndCsrf_Return401DeviceUnauthorized` (cookie + CSRF hợp lệ trên cả 3 endpoint thiết bị → `401 DEVICE_UNAUTHORIZED`); `DeviceAuthenticationTests` | ĐẠT |
| 3 | CSRF (D-02) cho mọi request ghi | `WriteEndpoints_LoggedInWithoutCsrfHeader_Return400CsrfInvalid`: mọi endpoint ghi không miễn CSRF, đã đăng nhập, thiếu header → `400 CSRF_INVALID`. `EveryEndpoint_IsClassified_AndCsrfExemptionMatchesTable` khóa danh sách miễn CSRF: chỉ 3 endpoint thiết bị và 5 luồng token ẩn danh (`api/auth/register|verify-email|resend-verification|forgot-password|reset-password`). `login`/`logout` vẫn bắt CSRF (chống login-CSRF) | ĐẠT |
| 4 | SameSite / HttpOnly / Secure cookie | `Program.cs`: cookie auth + antiforgery `HttpOnly`, `SameSite=Lax`, `Secure=Always` ngoài Development. `AuthApiTests` (dòng ~90) kiểm tra `httponly`, `samesite=lax` | ĐẠT |
| 5 | Không mở CORS (D-01) | `grep AddCors|UseCors|Access-Control` trong `src/` → không có kết quả | ĐẠT |
| 6 | HTTPS + HSTS ngoài Development | `Program.cs` bật `UseHsts` + `UseHttpsRedirection` khi không phải Development. `SecurityHeadersTests.Production_HttpRequest_RedirectsToHttpsAndHttpsResponseHasHsts` (host môi trường `Production`: HTTP → 307 sang HTTPS; HTTPS có `Strict-Transport-Security`) | ĐẠT |
| 7 | Header bảo mật | **Đã sửa** (commit `fix(security): add nosniff, frame, referrer and CSP headers…`): `Security/SecurityHeaders.cs` thêm cho mọi response `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, `Content-Security-Policy: default-src 'self'; img-src 'self' blob:; object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'none'`. `SecurityHeadersTests.AnyResponse_HasSecurityHeaders` (HTML, file tĩnh, 401, 404, health) + `UnhandledException_ProblemResponse_StillHasSecurityHeaders` (500). Header tải ảnh giữ nguyên: `PrescriptionReadApiTests` vẫn pass | ĐẠT (CSP chưa kiểm chứng trên browser thật, xem F-05) |
| 8 | Rate limit auth / lời mời / **pair, accept** → 429 + `Retry-After` | `RateLimitingTests.Login_EleventhAttemptWithin15Minutes_Returns429WithRetryAfter`, `Pair_ConfiguredThreshold_IsApplied`, `InvitationCreate_CountsPerUser_NotPerIp`; `DevicePairingApiTests.Pair_OverRateLimit_Returns429WithRetryAfter` (`Retry-After: 60`); `RelativeInvitationApiTests.CreateInvitation_OverConfiguredLimit_Returns429`, `AcceptInvitation_OverConfiguredLimit_Returns429`; `AccountPagesTests.Login_OverRateLimit_Returns429WithRetryAfter` (form Razor). Trang Razor `POST /devices/pair` dùng chung policy `pair` | ĐẠT (register/forgot/… chưa có, xem F-01) |
| 9 | Không có secret trong repo | `git ls-files` không có `.env`, `*.pfx`, `*.pem`, `*.key`, `secrets*`; `.claude/launch.json` bị ignore (`.gitignore:60 .claude/`) và không được theo dõi. `git grep` connection string có mật khẩu / pepper / API key: chỉ có giá trị giả trong test (`integration-test-pepper-…`, `probe-secret-pw` cho host không tồn tại) và placeholder `<password>` trong README. `appsettings*.json` chỉ có connection string `Trusted_Connection` localdb, không có pepper. Lịch sử git: không thấy pepper/mật khẩu thật | ĐẠT |
| 10 | Log không lộ dữ liệu | `AddHttpLogging` chỉ ghi method, path, status, duration (không header/body/query). `LogRedactionTests.Requests_WithDeviceKeyCookieAndBody_NeverAppearInLogs` (`X-Device-Id`, `X-Device-Api-Key`, cookie auth/CSRF, body JSON không xuất hiện trong bất kỳ log nào); `TraceIdLogScopeTests` (query string + header tùy ý) | ĐẠT |
| 11 | Ảnh đơn thuốc private | Lưu ngoài `wwwroot` (`App_Data/uploads`, bị git ignore); chỉ đọc qua `GET /api/prescriptions/images/{id}/content` (OwnerOnly, `Cache-Control: private, no-store`, `nosniff`). `PrescriptionStorageTests`, `PrescriptionReadApiTests`, `PrescriptionUploadApiTests` | ĐẠT |
| 12 | Rà dependency | `dotnet list Project_1.sln package --vulnerable --include-transitive` → cả 6 project "has no vulnerable packages". Xem §3 | ĐẠT |
| 13 | Backup mã hóa khi lưu và khi truyền | `docs/runbook-backup-restore.md` mô tả AES-256 + certificate, encrypted volume/KMS, TLS; `docs/backup-restore-test-report.md` ghi `NOT RUN` | **NOT VERIFIED** — việc vận hành, cần người thực hiện (F-06) |

## 2. Phát hiện

| ID | Mức | Mô tả | Trạng thái |
|---|---|---|---|
| F-00 | Trung bình | Không có header bảo mật (`nosniff`, chống clickjacking, Referrer-Policy, CSP) trên HTML/API; trang Razor có thể bị nhúng iframe (clickjacking form ghép hộp/đăng nhập) | **Đã sửa** — `SecurityHeaders.cs`, test `SecurityHeadersTests` |
| F-01 | Trung bình | `register`, `forgot-password`, `resend-verification`, `verify-email`, `reset-password` chưa có rate limit. Token 32 byte CSPRNG nên dò token không khả thi, nhưng `forgot-password`/`resend-verification` có thể bị lạm dụng để spam email tới một địa chỉ. Ngưỡng là **[TBD-D28]** | MỞ — cần chốt D-28 |
| F-02 | Trung bình (vận hành) | Rate limit đếm theo `RemoteIpAddress`, bộ đếm trong bộ nhớ từng process. Sau reverse proxy mà chưa bật `ForwardedHeaders` thì mọi người dùng chung một IP (khóa nhầm toàn bộ); nhiều instance thì mỗi instance đếm riêng (giảm hiệu lực chống brute-force pair). Đã ghi `shortcut:` trong `ApiRateLimiting.cs` | MỞ — chốt khi triển khai |
| F-03 | Thấp | Session lưu in-memory (**[TBD-D27]**): restart mất phiên; nhiều instance không chia sẻ phiên. Không phải lỗ hổng, là giới hạn vận hành | MỞ — D-27 |
| F-04 | Thấp | `AllowedHosts: "*"`. Hiện email không chứa link (D-30) nên không có host-header injection; khi chốt D-30 cần đặt `AllowedHosts` và base URL cố định | MỞ — gắn với D-30 |
| F-05 | Thấp | CSP mới chưa kiểm chứng trên browser thật. Rà tĩnh: không có `<script>`/`<style>` inline, không có `style=`/`on*=`/`javascript:` trong Views và JS; ảnh xem trước dùng `blob:` (đã cho phép); không tài nguyên ngoài origin. Cần mở các trang chính và xem console không có vi phạm CSP | MỞ — cần kiểm tra UI |
| F-06 | Cao (vận hành) | Mã hóa backup khi lưu/truyền chưa có bằng chứng thực thi; chỉ có runbook | NOT VERIFIED — người vận hành |
| F-07 | Thông tin | 5 endpoint token ẩn danh miễn CSRF. Không gắn phiên, cần token 256-bit trong body nên CSRF không gây hại; `login`/`logout` vẫn bắt CSRF | Chấp nhận |
| F-08 | Thông tin | Nhiều package lệch bản vá trong cùng major (xem §3); không có cảnh báo lỗ hổng | Đề xuất nâng bản vá |

Không phát hiện: lộ `DeviceApiKey`/pairing code trong log hay response; endpoint thiếu kiểm tra quyền; CORS mở; secret trong repo.

## 3. Dependency

`dotnet list Project_1.sln package --vulnerable --include-transitive`: **không có package có lỗ hổng** (SmartMedBox.Api, Domain, Infrastructure, UnitTests, IntegrationTests, Provisioning).

`--outdated --highest-minor` (cùng major, chưa nâng):

| Project | Package | Hiện tại | Bản vá mới |
|---|---|---|---|
| Infrastructure | Microsoft.EntityFrameworkCore.Design / .SqlServer | 10.0.11 | 10.0.15 |
| Infrastructure | Microsoft.Extensions.Identity.Core | 10.0.5 | 10.0.15 |
| Infrastructure | System.Security.Cryptography.Pkcs | 10.0.14 | 10.0.15 |
| Tests | Microsoft.AspNetCore.Mvc.Testing | 10.0.14 | 10.0.15 |
| Tests | Microsoft.NET.Test.Sdk / xunit.runner.visualstudio | 17.14.1 / 3.1.4 | 17.14.2 / 3.1.5 |

Có major 11.x cho EF Core/Identity/Mvc.Testing — không nâng trong MVP.

## 4. Việc cần người thực hiện

1. Ký duyệt checklist này.
2. Chốt D-28 (rate limit cho các luồng token ẩn danh) — F-01.
3. Khi triển khai: bật `ForwardedHeaders` sau proxy, quyết định store rate limit/session dùng chung (F-02, F-03), đặt `AllowedHosts` (F-04).
4. Mở các trang Razor chính (desktop + mobile) và xác nhận console không có vi phạm CSP — F-05.
5. Thực hiện backup mã hóa + restore theo runbook và điền `docs/backup-restore-test-report.md` — F-06.
