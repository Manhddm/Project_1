using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;

namespace SmartMedBox.IntegrationTests.TestSupport;

[ApiController]
[IgnoreAntiforgeryToken]
[Route("api/test-probe")]
public sealed class TestProbeController(IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [HttpPost("validate")]
    public IActionResult Validate([FromBody] ProbeRequest request) => Ok(new { request.Name });

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("secret internal detail");
}

public sealed class ProbeRequest
{
    [Required]
    public string? Name { get; set; }
}
