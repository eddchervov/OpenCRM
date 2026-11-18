using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.WebApp.Models.Maintenance;
using OpenCRM.WebApp.Presentation;
using System.Globalization;

namespace OpenCRM.WebApp.Controllers.Maintenance;

/// <summary> Контроллер страниц просмотра и редактирования обслуживаний. </summary>
[Authorize]
public class MaintenanceController(
        IVehicleQueryService vehicleService,
        IMaintenanceQueryService maintenanceQueryService,
        IOrganizationQueryService organizationService,
        IOrganizationSubscriptionService organizationSubscriptionService,
        ICatalogQueryService organizationScenarioCatalogService,
        IOrganizationWorkspaceShellService organizationWorkspaceShellService,
        UserManager<ApplicationUser> userManager,
        IUsersService usersService
    ) : Controller
{
    /// <summary> Отображает страницу обслуживания. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}")]
    public async Task<IActionResult> Maintenance(Guid maintenanceId, Guid? organizationId = null)
    {
        var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

        await usersService.RecordLastDateOfEntry(userId);

        GetMaintenanceByIdDto maintenance;
        try
        {
            maintenance = await maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
        }
        catch
        {
            return Redirect("/Error/404");
        }

        if (!organizationId.HasValue)
        {
            ViewData["MaintenanceVehicle"] = await vehicleService.GetById(userId, null, maintenance.VehicleId);
        }

        ViewData["MaintenanceId"] = maintenanceId.ToString();
        ViewData["MaintenanceOrganizationId"] = organizationId?.ToString();
        await PopulateOrganizationWorkspaceChromeAsync(userId, organizationId);
        await PopulateScenarioCatalogAsync(userId, organizationId);
        return View(organizationId.HasValue ? "MaintenanceOrganization" : "MaintenancePersonal");
    }

    /// <summary> Отображает историю изменений обслуживания. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/history/view")]
    public async Task<IActionResult> History(Guid maintenanceId, Guid? organizationId = null, string? returnUrl = null)
    {
        var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

        await usersService.RecordLastDateOfEntry(userId);

        GetMaintenanceByIdDto maintenance;
        try
        {
            maintenance = await maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
        }
        catch
        {
            return Redirect("/Error/404");
        }

        if (!organizationId.HasValue)
        {
            ViewData["MaintenanceVehicle"] = await vehicleService.GetById(userId, null, maintenance.VehicleId);
        }

        ViewData["MaintenanceId"] = maintenanceId.ToString();
        ViewData["MaintenanceOrganizationId"] = organizationId?.ToString();
        ViewData["MaintenanceReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        await PopulateOrganizationWorkspaceChromeAsync(userId, organizationId);

        return View("MaintenanceHistory");
    }

    /// <summary> Отображает форму создания обслуживания для техники. </summary>
    [HttpGet("vehicle/{vehicleId:guid}/maintenance/create")]
    public async Task<IActionResult> Create(
        Guid vehicleId,
        Guid? organizationId = null,
        string? returnUrl = null,
        string? prefillTitle = null,
        string? prefillComment = null,
        string? plannedDate = null,
        string? plannedStartTime = null,
        string? plannedEndTime = null,
        string? schedulerSourceCode = null,
        Guid? schedulerSourceEntityId = null,
        string? schedulerTriggerKey = null)
    {
        var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

        await usersService.RecordLastDateOfEntry(userId);

        GetVehicleDto vehicle;
        try
        {
            vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
        }
        catch
        {
            return Redirect("/Error/404");
        }

        ViewData["MaintenanceOrganizationId"] = organizationId?.ToString();
        ViewData["MaintenanceReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        ViewData["MaintenancePrefillTitle"] = string.IsNullOrWhiteSpace(prefillTitle) ? null : prefillTitle.Trim();
        ViewData["MaintenancePrefillComment"] = string.IsNullOrWhiteSpace(prefillComment) ? null : prefillComment.Trim();
        ViewData["MaintenancePrefillPlannedDate"] = TryParseDate(plannedDate)?.ToString("yyyy-MM-dd");
        ViewData["MaintenancePrefillPlannedStartTime"] = TryParseTime(plannedStartTime);
        ViewData["MaintenancePrefillPlannedEndTime"] = TryParseTime(plannedEndTime);
        ViewData["MaintenanceSchedulerSourceCode"] = string.IsNullOrWhiteSpace(schedulerSourceCode) ? null : schedulerSourceCode.Trim();
        ViewData["MaintenanceSchedulerSourceEntityId"] = schedulerSourceEntityId?.ToString();
        ViewData["MaintenanceSchedulerTriggerKey"] = string.IsNullOrWhiteSpace(schedulerTriggerKey) ? null : schedulerTriggerKey.Trim();
        await PopulateOrganizationWorkspaceChromeAsync(userId, organizationId);
        await PopulateScenarioCatalogAsync(userId, organizationId);

        return View(organizationId.HasValue ? "CreateMaintenanceOrganization" : "CreateMaintenancePersonal", vehicle);
    }

    /// <summary> Отображает форму редактирования обслуживания. </summary>
    [HttpGet("vehicle/{vehicleId:guid}/maintenance/{maintenanceId:guid}/update")]
    public async Task<IActionResult> Update(Guid vehicleId, Guid maintenanceId, Guid? organizationId = null, string? returnUrl = null)
    {
        var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

        await usersService.RecordLastDateOfEntry(userId);

        GetVehicleDto vehicle;
        GetMaintenanceByIdDto maintenance;
        try
        {
            vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
            maintenance = await maintenanceQueryService.GetById(userId, organizationId, maintenanceId);
        }
        catch
        {
            return Redirect("/Error/404");
        }

        if (maintenance.VehicleId != vehicleId)
        {
            return Redirect("/Error/404");
        }

        ViewData["MaintenanceOrganizationId"] = organizationId?.ToString();
        ViewData["MaintenanceReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        await PopulateOrganizationWorkspaceChromeAsync(userId, organizationId);
        await PopulateScenarioCatalogAsync(userId, organizationId);

        return View(organizationId.HasValue ? "UpdateMaintenanceOrganization" : "UpdateMaintenancePersonal", new UpdateMaintenanceFormModel
        {
            Vehicle = vehicle,
            MaintenanceId = maintenanceId
        });
    }

    /// <summary> Заполняет данные оформления рабочего пространства для страницы. </summary>
    private async Task PopulateOrganizationWorkspaceChromeAsync(string userId, Guid? organizationId)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        var overview = await organizationService.GetOverviewAsync(userId);
        var organization = overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value);
        if (organization == null)
        {
            return;
        }

        ViewData["MaintenanceOrganizationName"] = organization.Name;
        this.ViewData["OrganizationTimeZoneId"] = organization.TimeZoneId;
        ViewData["MaintenanceOrganizationMembersCount"] = organization.MembersCount;
        ViewData["MaintenanceCanManageSettings"] = organization.CanManageSettings;
        ViewData["MaintenanceHasVehiclesModule"] = await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
        ViewData["MaintenanceHasClientsModule"] = await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
        ViewData["MaintenanceHasDefectsModule"] = await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
        ViewData["MaintenanceHasCountersModule"] = await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
        ViewData["MaintenanceHasSchedulerModule"] = await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
    }

    /// <summary> Заполняет каталог сценариев для страницы. </summary>
    private async Task PopulateScenarioCatalogAsync(string userId, Guid? organizationId)
    {
        if (!organizationId.HasValue)
        {
            return;
        }

        try
        {
            var shell = await organizationWorkspaceShellService.GetAsync(userId, organizationId.Value);
            ViewData["MaintenanceWorkspaceShell"] = shell;
            ViewData["MaintenanceFormProfile"] = shell.Scenario.MaintenanceForm;
            var catalog = await organizationScenarioCatalogService.GetAsync(userId, organizationId.Value);
            ViewData["MaintenanceScenarioCatalog"] = catalog;
            ViewData["MaintenanceScenarioCode"] = catalog.ScenarioCode;
            ViewData["MaintenanceScenarioUxProfile"] = OrganizationScenarioPresentationHelper.ResolveUxProfile(catalog.ScenarioCode);
        }
        catch
        {
            ViewData["MaintenanceWorkspaceShell"] = null;
            ViewData["MaintenanceFormProfile"] = null;
            ViewData["MaintenanceScenarioCatalog"] = null;
            ViewData["MaintenanceScenarioCode"] = null;
            ViewData["MaintenanceScenarioUxProfile"] = OrganizationScenarioPresentationHelper.ResolveUxProfile(null);
        }
    }

    /// <summary> Разбирает строку в дату, возвращая null при ошибке. </summary>
    private static DateTime? TryParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.Date
            : null;
    }

    /// <summary> Пытается разобрать строку со временем и возвращает нормализованное значение. </summary>
    private static string? TryParseTime(string? value)
    {
        return TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var time)
            ? time.ToString(@"hh\:mm", CultureInfo.InvariantCulture)
            : null;
    }
}



