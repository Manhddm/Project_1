// CLI vận hành/nhà máy (D-05): provision thiết bị và cấp pairing code. Không có endpoint admin.
// Cấu hình qua biến môi trường giống API: ConnectionStrings__Default, Security__TokenPepper (cùng pepper với API).
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartMedBox.Domain.Devices;
using SmartMedBox.Infrastructure.Data;
using SmartMedBox.Infrastructure.Devices;
using SmartMedBox.Infrastructure.Prescriptions;
using SmartMedBox.Infrastructure.Security;
using SmartMedBox.Infrastructure.Storage;
using SystemClock = SmartMedBox.Infrastructure.Time.SystemClock;

if (args is ["reconcile-files"])
{
    await RunReconciliationAsync();
    return;
}

if (args.Length != 2 || args[0] is not ("provision" or "issue-code"))
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  provision <serialNumber>   Tạo thiết bị + 3 ngăn + DeviceApiKey + pairing code");
    Console.Error.WriteLine("  issue-code <deviceId>      Cấp pairing code mới cho thiết bị chưa ghép (vô hiệu mã cũ)");
    Console.Error.WriteLine("  reconcile-files            Đối chiếu metadata ảnh với private storage (read-only)");
    return 2;
}

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
var pepper = Environment.GetEnvironmentVariable("Security__TokenPepper");
if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(pepper) || pepper.Length < 32)
{
    Console.Error.WriteLine("ConnectionStrings__Default and Security__TokenPepper (>= 32 chars, same as the API) are required.");
    return 2;
}

await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
var service = new DeviceOnboardingService(
    db,
    new HmacTokenHasher(Options.Create(new TokenPepperOptions { TokenPepper = pepper })),
    new SystemClock());

try
{
    if (args[0] == "provision")
    {
        var result = await service.ProvisionAsync(args[1]);
        Console.WriteLine($"DeviceId:      {result.Provisioned.Device.DeviceId}");
        Console.WriteLine($"SerialNumber:  {result.Provisioned.Device.SerialNumber}");
        // Khóa thô chỉ hiển thị một lần tại đây; server chỉ lưu HMAC, không thể lấy lại.
        Console.WriteLine($"DeviceApiKey:  {result.DeviceApiKey}   (shown once - flash into the device now)");
        PrintCode(result.PairingCode);
        return 0;
    }

    var code = await service.IssuePairingCodeAsync(args[1]);
    if (code is null)
    {
        Console.Error.WriteLine("Device not found or already paired.");
        return 1;
    }

    PrintCode(code);
    return 0;
}
catch (Exception ex) when (ex is DbUpdateException or ArgumentException)
{
    Console.Error.WriteLine($"Failed: {ex.GetBaseException().Message}");
    return 1;
}

static void PrintCode(IssuedPairingCode code)
{
    Console.WriteLine($"PairingCode:   {PairingCode.FormatForLabel(code.Code)}");
    Console.WriteLine($"ExpiresAtUtc:  {code.ExpiresAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}");
    Console.WriteLine($"QrPayload:     {PairingCode.QrPayload(code.Code)}");
}

static async Task RunReconciliationAsync()
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");
    var storageRoot = Environment.GetEnvironmentVariable("FileStorage__RootPath");
    if (string.IsNullOrWhiteSpace(connectionString) || string.IsNullOrWhiteSpace(storageRoot))
    {
        Console.Error.WriteLine("ConnectionStrings__Default and FileStorage__RootPath are required.");
        Environment.ExitCode = 2;
        return;
    }

    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connectionString).Options);
    var service = new PrescriptionFileReconciliationService(
        db,
        new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = storageRoot })));
    var report = await service.ReconcileAsync();

    Console.WriteLine($"Metadata records: {report.MetadataCount}");
    Console.WriteLine($"Storage files:    {report.StorageFileCount}");
    PrintFindings("Missing files", report.MissingFiles);
    PrintFindings("Orphan files", report.OrphanFiles);
    PrintFindings("Invalid files", report.InvalidStorageFiles);
    PrintFindings("Duplicate keys", report.DuplicateMetadataKeys);
    Environment.ExitCode = report.IsClean ? 0 : 1;
}

static void PrintFindings(string label, IReadOnlyList<string> findings)
{
    Console.WriteLine($"{label}:       {findings.Count}");
    foreach (var finding in findings)
    {
        Console.WriteLine($"  - {finding}");
    }
}