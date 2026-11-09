using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartMedBox.Api.Security;

namespace SmartMedBox.IntegrationTests.TestSupport;

/// <summary>Endpoint giả dùng scheme thiết bị như các endpoint /api/device/* thật.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = DeviceAuthenticationHandler.SchemeName)]
[IgnoreAntiforgeryToken]
[Route("api/test-probe/device-auth")]
public sealed class DeviceAuthProbeController : ControllerBase
{
    [HttpPost]
    public IActionResult Post() => Ok(new { deviceKey = User.FindFirst(DeviceAuthenticationHandler.DeviceKeyClaim)?.Value });
}
