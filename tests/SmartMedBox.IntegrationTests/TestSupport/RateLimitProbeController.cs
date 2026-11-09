using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartMedBox.Api.Security;

namespace SmartMedBox.IntegrationTests.TestSupport;

/// <summary>Endpoint giả gắn policy cho các endpoint chưa triển khai (pair P2-04, invitation P2-09).</summary>
[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/test-probe/rate-limit")]
public sealed class RateLimitProbeController : ControllerBase
{
    [HttpPost("pair")]
    [EnableRateLimiting(RateLimitPolicies.Pair)]
    public IActionResult Pair() => NoContent();

    [Authorize]
    [HttpPost("invitation")]
    [EnableRateLimiting(RateLimitPolicies.InvitationCreate)]
    public IActionResult Invitation() => NoContent();
}
