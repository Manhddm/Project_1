using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMedBox.Api.Security;
using SmartMedBox.Infrastructure.Devices;

namespace SmartMedBox.Api.Controllers;

public sealed class PairDeviceForm
{
    public string? Code { get; set; }
}

public sealed record DevicePageViewModel(
    PairDeviceForm PairForm,
    DeviceStatusViewModel? Device);

public sealed record DeviceStatusViewModel(
    string SerialNumber,
    int ScheduleVersion,
    DateTimeOffset? LastSeenAt,
    IReadOnlyList<CompartmentStatusViewModel> Compartments);

public sealed record CompartmentStatusViewModel(int SlotNumber, string Name, bool IsEnabled);

[Route("devices")]
public sealed class DevicePagesController(DeviceOwnershipService ownership) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        var device = await ownership.GetOwnedAsync(userId, cancellationToken);
        return View(new DevicePageViewModel(new PairDeviceForm(), ToViewModel(device)));
    }

    [HttpGet("pair")]
    public IActionResult Pair() => User.Identity?.IsAuthenticated == true
        ? View(new PairDeviceForm())
        : RedirectToAction(nameof(AccountController.Login), "Account");

    [HttpPost("pair")]
    [EnableRateLimiting(RateLimitPolicies.Pair)]
    public async Task<IActionResult> Pair(PairDeviceForm form, CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return RedirectToAction(nameof(AccountController.Login), "Account");
        }

        if (string.IsNullOrWhiteSpace(form.Code))
        {
            ModelState.AddModelError(nameof(PairDeviceForm.Code), "Hãy nhập mã ghép trên nhãn của hộp.");
            return View(form);
        }

        var result = await ownership.PairAsync(userId, form.Code, cancellationToken);
        if (result.Outcome != PairingOutcome.Success)
        {
            ModelState.AddModelError(nameof(PairDeviceForm.Code), PairingMessage(result.Outcome));
            return View(form);
        }

        TempData["Message"] = "Ghép hộp thành công.";
        return RedirectToAction(nameof(Index));
    }

    private static DeviceStatusViewModel? ToViewModel(OwnedDevice? device) => device is null
        ? null
        : new DeviceStatusViewModel(
            device.SerialNumber,
            device.ScheduleVersion,
            device.LastSeenAt,
            device.Compartments.Select(x => new CompartmentStatusViewModel(x.SlotNumber, x.Name, x.IsEnabled)).ToList());

    private static string PairingMessage(PairingOutcome outcome) => outcome switch
    {
        PairingOutcome.EmailNotVerified => "Email chưa được xác minh. Hãy xác minh email trước khi ghép hộp.",
        PairingOutcome.CodeInvalid => "Mã ghép không đúng hoặc không tồn tại.",
        PairingOutcome.CodeUsed => "Mã ghép đã được sử dụng.",
        PairingOutcome.CodeExpired => "Mã ghép đã hết hạn. Hãy dùng mã mới.",
        PairingOutcome.UserAlreadyOwnsDevice => "Tài khoản đã sở hữu một hộp thuốc.",
        PairingOutcome.DeviceAlreadyPaired => "Hộp thuốc đã được ghép với tài khoản khác.",
        PairingOutcome.Success => "Ghép hộp thành công.",
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}
