using Microsoft.AspNetCore.Mvc;
using SmartMedBox.Api.Security;

namespace SmartMedBox.Api.Controllers;

[Route("relatives")]
public sealed class RelativePagesController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => User.TryGetUserId(out _)
        ? View()
        : RedirectToAction(nameof(AccountController.Login), "Account");
}
