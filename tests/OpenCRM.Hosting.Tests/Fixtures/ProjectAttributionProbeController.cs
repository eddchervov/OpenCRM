using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenCRM.Hosting.Tests.Fixtures;

[AllowAnonymous]
public sealed class ProjectAttributionProbeController : Controller
{
    [HttpGet("/__tests/project-attribution")]
    public IActionResult Index() => this.PartialView("~/Views/Shared/_ProjectAttribution.cshtml");
}