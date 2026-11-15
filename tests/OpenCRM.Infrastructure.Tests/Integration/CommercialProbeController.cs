using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.WebApp.Infrastructure.Security;

namespace OpenCRM.Infrastructure.Tests.Integration;

[ApiController]
[Route("api/commercial-module-probe")]
[Authorize(Roles = "Admin")]
[CommercialModule]
public sealed class CommercialProbeController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => this.Ok(new { available = true });
}
