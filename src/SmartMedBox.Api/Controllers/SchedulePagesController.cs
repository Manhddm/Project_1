using Microsoft.AspNetCore.Mvc;
using SmartMedBox.Api.Security;

namespace SmartMedBox.Api.Controllers;

[Route("schedules")]
public sealed class SchedulePagesController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => User.TryGetUserId(out _)
        ? View()
        : RedirectToAction(nameof(AccountController.Login), "Account", new { returnUrl = "/schedules" });
}
