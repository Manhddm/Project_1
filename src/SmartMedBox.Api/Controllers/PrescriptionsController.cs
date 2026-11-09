using Microsoft.AspNetCore.Mvc;
using SmartMedBox.Api.Errors;
using SmartMedBox.Api.Security;
using SmartMedBox.Infrastructure.Prescriptions;

namespace SmartMedBox.Api.Controllers;

[Route("prescriptions")]
public sealed class PrescriptionsController(PrescriptionImageService images) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        if (!User.TryGetUserId(out var userId))
        {
            return RedirectToAction("Login", "Account", new { returnUrl = "/prescriptions" });
        }

        if (await images.GetOwnedDeviceIdAsync(userId, cancellationToken) is null)
        {
            return ApiProblemResults.Create(HttpContext, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden);
        }

        return View();
    }
}
