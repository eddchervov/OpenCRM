using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.WebApp.Navigation;

namespace OpenCRM.WebApp.Controllers;

[Authorize(Policy = "AssistantWebSession")]
public sealed class AssistantController(
    AssistantAccessService access,
    UserManager<ApplicationUser> users,
    IOrganizationQueryService organizationService,
    IOrganizationSubscriptionService subscriptions) : Controller
{
    [HttpGet("assistant/{organizationId:guid}")]
    public async Task<IActionResult> Index(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = users.GetUserId(this.User) ?? string.Empty;
        var actor = new AssistantActor(userId, organizationId, "web");
        bool hasAssistantFeature;
        try
        {
            await access.EnsureHistoryAvailableAsync(actor, cancellationToken);
            hasAssistantFeature = await subscriptions.HasFeatureAsync(userId, organizationId, Helpers.FeatureCodes.Assistant);
            if (hasAssistantFeature)
            {
                await access.EnsureAvailableAsync(actor, cancellationToken);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return this.NotFound();
        }

        var organization = (await organizationService.GetOverviewAsync(userId)).Organizations
            .FirstOrDefault(item => item.Id == organizationId);
        if (organization == null || !IsAssistantWorkspaceScenario(organization.PrimaryScenarioCode))
        {
            return this.NotFound();
        }

        var workspaceUrl = $"{this.Request.PathBase}{OrganizationWorkspaceUrlHelper.Resolve(organization, "assistant")}";
        if (hasAssistantFeature)
        {
            return this.Redirect(workspaceUrl);
        }

        this.Response.Headers.CacheControl = "no-store";
        this.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=()";
        this.ViewData["OrganizationId"] = organizationId;
        this.ViewData["AssistantOrganizationName"] = organization.Name;
        this.ViewData["OrganizationTimeZoneId"] = organization.TimeZoneId;
        this.ViewData["AssistantWorkspaceUrl"] = workspaceUrl.Split('#')[0];
        this.ViewData["AssistantPreviewOnly"] = true;

        return this.View("~/Views/AiPilot/Index.cshtml");
    }

    [HttpGet("assistant/{organizationId:guid}/embedded")]
    public async Task<IActionResult> Embedded(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = users.GetUserId(this.User) ?? string.Empty;
        try
        {
            await access.EnsureAvailableAsync(new AssistantActor(userId, organizationId, "web"), cancellationToken);
        }
        catch (Exception exception) when (exception is InvalidOperationException or UnauthorizedAccessException)
        {
            return this.NotFound();
        }

        var organization = (await organizationService.GetOverviewAsync(userId)).Organizations
            .FirstOrDefault(item => item.Id == organizationId);
        if (organization == null || !IsAssistantWorkspaceScenario(organization.PrimaryScenarioCode))
        {
            return this.NotFound();
        }

        this.Response.Headers.CacheControl = "no-store";
        this.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=(self)";
        this.ViewData["OrganizationId"] = organizationId;
        this.ViewData["AssistantEmbedded"] = true;
        this.ViewData["OrganizationTimeZoneId"] = organization.TimeZoneId;

        return this.View("~/Views/AiPilot/Index.cshtml");
    }

    private static bool IsAssistantWorkspaceScenario(string? scenarioCode)
    {
        var normalized = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(scenarioCode);
        return normalized is OrganizationTypeCodes.ServiceStation or OrganizationTypeCodes.Detailing;
    }
}
