using Microsoft.AspNetCore.Mvc;
using SmartMedBox.Api.Security;

namespace SmartMedBox.Api.Controllers;

[Route("alerts")]
public sealed class AlertPagesController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => User.TryGetUserId(out _)
        ? View()
        : RedirectToAction(nameof(AccountController.Login), "Account");
}
