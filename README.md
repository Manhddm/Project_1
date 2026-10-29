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

```bash
dotnet restore
dotnet build
dotnet run --project src/SmartMedBox.Api
```

Mặc định: `https://localhost:7249` và `http://localhost:5189` (xem `src/SmartMedBox.Api/Properties/launchSettings.json`).

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

## Tài liệu

- [AGENTS.md](AGENTS.md) — quy tắc làm việc, bảo mật, Definition of Done
- [PLAN.md](PLAN.md) — kế hoạch, task ID, decision log
- [TASKS.md](TASKS.md) — trạng thái và bằng chứng
- [API_CONTRACT.md](API_CONTRACT.md) — hợp đồng API
- [TEST_PLAN.md](TEST_PLAN.md) — kế hoạch kiểm thử (82 test case)
- [TEST_REPORT.md](TEST_REPORT.md) — kết quả kiểm thử và defect log

## Bảo mật

Không commit secret, chuỗi kết nối thật hay khóa thiết bị. Cấu hình nhạy cảm đặt trong user-secrets hoặc biến môi trường.
