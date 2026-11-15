using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenCRM.Hosting.Tests.Fixtures;

[AllowAnonymous]
public sealed class ProjectAttributionProbeController : Controller
{
    [HttpGet("/__tests/project-attribution")]
    public IActionResult Index() => this.PartialView("~/Views/Shared/_ProjectAttribution.cshtml");
    [HttpGet("/__tests/assistant/{preview:bool}")]
    public IActionResult Assistant(bool preview)
    {
        this.ViewData["AssistantEmbedded"] = true;
        this.ViewData["AssistantPreviewOnly"] = preview;
        this.ViewData["AssistantOrganizationName"] = "Fixture organization";
        this.ViewData["OrganizationId"] = Guid.Empty;

        return this.View("~/Views/AiPilot/Index.cshtml");
    }
}