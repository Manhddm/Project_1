# Hộp thuốc thông minh — MVP

Backend ASP.NET Core (.NET 10) cho Hộp thuốc thông minh: Web API, giao diện Razor cho chủ hộp/người thân, và REST/JSON cho thiết bị ESP32.

## Yêu cầu môi trường

- .NET SDK 10.0 trở lên (`dotnet --version`)
- SQL Server (dùng từ P0-02 trở đi)

## Cấu trúc repository

```
src/
  SmartMedBox.Api/             ASP.NET Core: API controllers + Razor views
  SmartMedBox.Domain/          Entity, enum, invariant nghiệp vụ
  SmartMedBox.Infrastructure/  EF Core, email, file storage, background jobs
tests/
  SmartMedBox.UnitTests/       Unit test (xUnit)
  SmartMedBox.IntegrationTests/ Integration test (WebApplicationFactory)
AGENTS.md · PLAN.md · TASKS.md · API_CONTRACT.md · TEST_PLAN.md · TEST_REPORT.md
Project_1.sln
```

Phụ thuộc project: `Api → Domain, Infrastructure`; `Infrastructure → Domain`; test project tham chiếu các project cần kiểm thử.

## Chạy ứng dụng

Trước khi chạy, **bắt buộc** cấu hình `ConnectionStrings:Default` và `Security:TokenPepper` (user-secrets hoặc biến môi trường — xem mục “Cấu hình & secret”). API kiểm tra cấu hình lúc khởi động và dừng ngay nếu thiếu.

```bash
dotnet restore
dotnet build
dotnet run --project src/SmartMedBox.Api
```

Mặc định: `https://localhost:7249` và `http://localhost:5189` (xem `src/SmartMedBox.Api/Properties/launchSettings.json`). Dùng profile **https** khi thử luồng đăng nhập: cookie xác thực và CSRF đều đặt `Secure`, nên chỉ được gửi qua HTTPS.

Thư mục runtime `src/SmartMedBox.Api/App_Data/` (đã gitignore) chứa email dev sink (`email-sink/`) và vùng file private (`uploads/`); đổi vị trí qua `EmailSink:DirectoryPath` và `FileStorage:RootPath` trong `appsettings.json`.

### Tài khoản (P1-02)

- `POST /api/auth/register` — tạo tài khoản chưa xác minh, gửi email chứa token (TTL 24 giờ) vào dev sink; trả `201`. Email đã tồn tại → `409` `EMAIL_TAKEN`; mật khẩu không đạt chính sách (≥ 8 ký tự gồm chữ và số, ≤ 128) → `400` `VALIDATION_ERROR`.
- `POST /api/auth/verify-email` — dùng một lần; token sai/đã dùng → `400` `TOKEN_INVALID`, hết hạn → `410` `TOKEN_EXPIRED`.
- `POST /api/auth/resend-verification` — luôn trả `202` với thông báo trung tính (không tiết lộ email có tồn tại); gửi token mới và vô hiệu token cũ.
- Token chỉ lưu dạng `HMAC-SHA256(pepper, token)`; xem `Security:TokenPepper` bên dưới.

### Quên & đặt lại mật khẩu (P1-04)

- `POST /api/auth/forgot-password` — luôn trả `202` với thông báo trung tính (không tiết lộ email có tồn tại, kể cả email sai định dạng); gửi token TTL 24 giờ, dùng một lần và vô hiệu token đặt lại cũ.
- `POST /api/auth/reset-password` — mật khẩu mới theo chính sách D-14; thành công trả `200` và **thu hồi mọi phiên đăng nhập cũ** của tài khoản. Token sai/đã dùng → `400` `TOKEN_INVALID`, hết hạn → `410` `TOKEN_EXPIRED`.

### Xác thực Web (P1-03)

- `POST /api/auth/login` đặt cookie `SmartMedBox.Auth` (HttpOnly, Secure, SameSite=Lax), thời hạn session 8 giờ; `POST /api/auth/logout` thu hồi session phía server; `GET /api/users/me` trả hồ sơ + hộp đã ghép (nếu có).
- Mọi request ghi cần CSRF header `X-CSRF-TOKEN`. Hiện **chưa có** endpoint công khai phát token cho API client; UI Razor (UI-01) sẽ render token trong form. Session đang lưu in-memory (mất khi restart, không chia sẻ đa instance) — xem **D-27** trong [PLAN.md](PLAN.md#decision-log).

## Cấu hình & secret

Không đặt mật khẩu, khóa thiết bị, pepper hay chuỗi kết nối có thông tin đăng nhập trong `appsettings*.json` hay bất kỳ file nào được commit. API đọc chuỗi kết nối từ khóa `ConnectionStrings:Default`, lấy từ user-secrets khi phát triển cục bộ hoặc biến môi trường khi triển khai.

`Security:TokenPepper` là pepper HMAC cho token xác minh email/đặt lại mật khẩu (P1-02, P1-04): **bắt buộc**, tối thiểu 32 ký tự, không có giá trị mặc định; API dừng khởi động nếu thiếu. Sinh giá trị ngẫu nhiên mạnh rồi lưu vào user-secrets/env, **không** ghi vào file mẫu hay bất kỳ file nào được commit. Đổi pepper sẽ làm mọi token đang phát hành mất hiệu lực.

```bash
# Sinh pepper (48 byte ngẫu nhiên → base64)
openssl rand -base64 48
```

`appsettings.Development.sample.json` chỉ là mẫu tham khảo cho LocalDB (không chứa credential); không điền giá trị thật vào file mẫu hay bất kỳ `appsettings*.json` nào. Dùng user-secrets hoặc biến môi trường bên dưới.

User-secrets (khuyến nghị khi phát triển):

```bash
dotnet user-secrets set "ConnectionStrings:Default" "Server=(localdb)\\MSSQLLocalDB;Database=SmartMedBox;Trusted_Connection=True;TrustServerCertificate=True" --project src/SmartMedBox.Api
dotnet user-secrets set "Security:TokenPepper" "<pepper-ngẫu-nhiên-≥32-ký-tự>" --project src/SmartMedBox.Api
dotnet user-secrets list --project src/SmartMedBox.Api
```

Biến môi trường (CI/triển khai), dùng dấu `__` cho cấu hình lồng nhau:

```bash
# Bash
export ConnectionStrings__Default="Server=<host>;Database=SmartMedBox;User Id=<user>;Password=<password>;TrustServerCertificate=True"
export Security__TokenPepper="<pepper-ngẫu-nhiên-≥32-ký-tự>"

# PowerShell
$env:ConnectionStrings__Default = "Server=<host>;Database=SmartMedBox;User Id=<user>;Password=<password>;TrustServerCertificate=True"
$env:Security__TokenPepper = "<pepper-ngẫu-nhiên-≥32-ký-tự>"
```

Email (D-03): `Email:Provider` = `FileSink` (mặc định — ghi `.eml` vào `EmailSink:DirectoryPath`, dùng cho dev/test) hoặc `Smtp`. Khi chọn `Smtp`, API kiểm tra cấu hình lúc khởi động và dừng nếu thiếu/sai. Các khóa `Email:Smtp:*`: `Host` (bắt buộc), `Port` (mặc định 587), `EnableSsl` (mặc định `true`), `FromAddress` (bắt buộc, cũng là domain của `Message-ID`), `FromName`, `UserName` + `Password` (đặt cùng nhau, **chỉ** qua user-secrets/env), `PickupDirectory` (tùy chọn: ghi `.eml` ra thư mục thay vì gửi qua mạng, để chẩn đoán). Mỗi lần gửi sinh header `Message-ID`, lưu vào `EmailDeliveries.ProviderMessageId`. Retry (D-13): tối đa 5 lần, chờ 1 → 5 → 15 → 60 phút, lease 2 phút — mặc định trong `EmailRetryPolicyOptions`/`EmailDeliveryWorkerOptions`.

```bash
dotnet user-secrets set "Email:Provider" "Smtp" --project src/SmartMedBox.Api
dotnet user-secrets set "Email:Smtp:Host" "<smtp-host>" --project src/SmartMedBox.Api
dotnet user-secrets set "Email:Smtp:FromAddress" "<no-reply@domain>" --project src/SmartMedBox.Api
dotnet user-secrets set "Email:Smtp:UserName" "<user>" --project src/SmartMedBox.Api
dotnet user-secrets set "Email:Smtp:Password" "<password>" --project src/SmartMedBox.Api
# env: Email__Provider=Smtp, Email__Smtp__Host=..., Email__Smtp__Password=...
```

Quy ước khác: `SMARTMEDBOX_CONNECTION` cho lệnh `dotnet ef` (design-time) và `SMARTMEDBOX_TEST_SERVER` cho integration test (xem mục Chạy test). Không log hoặc dán các giá trị này vào issue/PR.

## Job nền (D-11, D-12, D-13, D-26)

Ba job chạy trong process Web (`BackgroundService`), mỗi lần chạy một DI scope mới; an toàn khi nhiều instance chạy song song nên không cần khóa phân tán. Lỗi chỉ log loại exception (không PII), job tiếp tục ở chu kỳ sau.

| Khóa cấu hình | Mặc định | Ghi chú |
|---|---|---|
| `BackgroundJobs:Enabled` | `true` | `false` tắt cả ba job (host integration test đặt `false`) |
| `BackgroundJobs:DoseGenerationInterval` | `00:15:00` | D-11; sinh dose cho `[now − 24h, now + 48h]`, chạy ngay khi khởi động |
| `BackgroundJobs:AlertEvaluationInterval` | `00:01:00` | [ĐỀ XUẤT]; kết nối tại `DeadlineAt` theo `DeviceConnectivity:OfflineThreshold` (D-12, `00:05:00`) |
| `BackgroundJobs:EmailDeliveryIdleDelay` | `00:00:10` | [ĐỀ XUẤT]; gửi liên tục tới khi hết delivery đến hạn rồi chờ |
| `EmailRetryPolicy:MaxAttempts` | `5` | D-13 |
| `EmailRetryPolicy:RetryDelays` | `00:01:00`, `00:05:00`, `00:15:00`, `01:00:00` | D-13; cấu hình thì thay toàn bộ danh sách |
| `EmailDeliveryWorker:LeaseDuration` | `00:02:00` | D-13 (`LockedUntil`) |
| `DeviceEventTime:MaxFutureSkew` | `00:05:00` | D-26 |
| `DeviceEventTime:MaxAge` | `30.00:00:00` | D-26 |

## Chạy test

```bash
dotnet test
```

Integration test dùng SQL Server thật: mỗi lần chạy tạo database tạm `SmartMedBox_Test_<guid>` trên `(localdb)\MSSQLLocalDB`, apply migration rồi xóa sau khi xong. Trỏ sang instance khác bằng biến môi trường `SMARTMEDBOX_TEST_SERVER` (connection string tới database `master`).

Lệnh EF (design-time) dùng `SMARTMEDBOX_CONNECTION`, mặc định LocalDB `SmartMedBox`:

```bash
dotnet ef migrations add <Name> --project src/SmartMedBox.Infrastructure --startup-project src/SmartMedBox.Infrastructure --output-dir Data/Migrations
```

## Quyết định kỹ thuật đã chốt

| Mã | Quyết định |
|---|---|
| D-01 | Giao diện Razor/MVC nằm trong project `SmartMedBox.Api` |
| D-02 | Xác thực Web bằng cookie session + CSRF; không dùng bảng `RefreshTokens` |
| D-07 | Thiết bị xác thực bằng header `X-Device-Id` + `X-Device-Api-Key`; khóa 32 byte base64url, lưu HMAC+pepper |
| D-08 | Pairing code 8 ký tự Crockford Base32, hạn 15 phút; QR chỉ chứa `{"v":1,"code":"..."}` |
| D-09 | Enum dùng chuỗi PascalCase cố định (API_CONTRACT §10.2); `daysOfWeek` 1–7 theo ISO-8601 |
| D-14 | Mật khẩu ≥ 8 ký tự (chữ + số, ≤ 128); token xác minh/đặt lại TTL 24 giờ; lời mời hết hạn 7 ngày; rate limit login 10/15 phút/IP, pair 5/phút/IP, invitation 10/ngày, accept 10/phút/IP |

## Tài liệu

- [AGENTS.md](AGENTS.md) — quy tắc làm việc, bảo mật, Definition of Done
- [PLAN.md](PLAN.md) — kế hoạch, task ID, decision log
- [TASKS.md](TASKS.md) — trạng thái và bằng chứng
- [API_CONTRACT.md](API_CONTRACT.md) — hợp đồng API
- [TEST_PLAN.md](TEST_PLAN.md) — kế hoạch kiểm thử (82 test case)
- [TEST_REPORT.md](TEST_REPORT.md) — kết quả kiểm thử và defect log
- [docs/requirements/Hop-thuoc-thong-minh.html](docs/requirements/Hop-thuoc-thong-minh.html) — bản sao đặc tả MVP để truy vết

## Bảo mật

Không commit secret, chuỗi kết nối thật hay khóa thiết bị. Cấu hình nhạy cảm đặt trong user-secrets hoặc biến môi trường.

## Provision thiết bị (P2-02, D-05)

Đặt `ConnectionStrings__Default` và `Security__TokenPepper` giống API, rồi chạy:

```bash
dotnet run --project tools/SmartMedBox.Provisioning -- provision <serial>
dotnet run --project tools/SmartMedBox.Provisioning -- issue-code <deviceId>
```

`DeviceApiKey` chỉ in **một lần**; server chỉ lưu HMAC. Pairing code hết hạn sau 15 phút (D-08); phát mã mới bằng `issue-code` (quy trình phát mã: D-29).
