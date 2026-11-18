using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Planning.Contracts.Dtos;
using OpenCRM.Modules.Catalog.Contracts.Dtos;
using OpenCRM.Modules.Catalog.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Domain.Interfaces;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.WebApp.Models.Organizations;
using OpenCRM.WebApp.Infrastructure.Api;
using OpenCRM.WebApp.Product;
using System.Globalization;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API рабочего пространства организации: профиль, участники, биллинг, календари, каталог и склад. </summary>
[ApiController]
[Authorize]
public class OrganizationWorkspaceApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationQueryService organizationQueries,
    IOrganizationProfileCommandService organizationProfile,
    IOrganizationArchiveService organizationArchive,
    IOrganizationMembershipService organizationMembership,
    IOrganizationInvitationCommandService invitationCommands,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationSubscriptionCheckoutService checkoutService,
    IOrganizationPermissionService organizationPermissionService,
    IWorkspaceOverviewService workspaceOverviewService,
    IOrganizationWorkspaceShellService organizationWorkspaceShellService,
    IOrganizationWorkspaceQueryService organizationWorkspaceQueryService,
    IOrganizationWikiContentProvider wikiContent,
    IVehicleOptionQueryService vehicleOptions,
    ISchedulerQueryService schedulerQueries,
    ISchedulerTaskCommandService schedulerCommands,
    ISchedulerRecurrenceService schedulerRecurrence,
    ICatalogQueryService organizationScenarioCatalogService,
    ICatalogCommandService catalogCommandService,
    IInventoryQueryService inventoryQueryService,
    IStockCommandService stockCommandService,
    ISystemClock systemClock,
    ILogger<OrganizationWorkspaceApiController> logger) : ControllerBase
{
    /// <summary> Возвращает оболочку рабочего пространства организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/shell")]
    public async Task<IActionResult> LoadWorkspaceShell(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await organizationWorkspaceShellService.GetAsync(user.Id, organizationId));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary> Возвращает обзор рабочего пространства организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/overview")]
    public async Task<IActionResult> LoadWorkspaceOverview(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var result = await workspaceOverviewService.GetAsync(user.Id, organizationId);

        if (result.Overview == null)
        {
            return NotFound();
        }

        return Ok(result.Overview);
    }

    /// <summary> Возвращает данные рабочего дня организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/workday")]
    public Task<IActionResult> LoadWorkspaceWorkday(Guid organizationId, DateOnly date)
    {
        return this.LoadWorkspaceDayAsync(organizationId, date, false);
    }

    /// <summary> Возвращает визуализацию дня рабочего пространства. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/visualization")]
    public Task<IActionResult> LoadWorkspaceVisualization(Guid organizationId, DateOnly date)
    {
        return this.LoadWorkspaceDayAsync(organizationId, date, true);
    }

    /// <summary> Формирует данные дня рабочего пространства для страницы или визуализации. </summary>
    private async Task<IActionResult> LoadWorkspaceDayAsync(Guid organizationId, DateOnly date, bool isVisualization)
    {
        if (date == default || date == DateOnly.MaxValue)
        {
            return BadRequest(new { message = "Укажите корректную дату рабочего дня." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.SingleOrDefault(item => item.Id == organizationId);
        if (organization == null)
        {
            return NotFound();
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(organization.TimeZoneId ?? "UTC");

        var snapshot = await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId);
        var canViewWork = snapshot.Features.Any(feature => feature.BoolValue == true
            && (feature.Code == FeatureCodes.MaintenanceModule || feature.Code == FeatureCodes.VehiclesModule))
            && await organizationPermissionService.HasPermissionAsync(
                user.Id,
                organizationId,
                OrganizationPermissionCodes.MaintenanceView);
        if (!canViewWork)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Нет доступа к работам организации." });
        }

        var canViewClients = snapshot.Features.Any(feature => feature.Code == FeatureCodes.ClientsModule && feature.BoolValue == true)
            && await organizationPermissionService.HasPermissionAsync(
                user.Id,
                organizationId,
                OrganizationPermissionCodes.ClientsView);

        try
        {
            if (isVisualization)
            {
                return Ok(await organizationWorkspaceQueryService.GetVisualizationAsync(
                    organizationId,
                    date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    canViewClients,
                    timeZone));
            }

            return Ok(await organizationWorkspaceQueryService.GetWorkdayAsync(
                organizationId,
                date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                canViewClients,
                timeZone));
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Не удалось загрузить рабочий день организации. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                organizationId,
                user.Id,
                HttpContext.TraceIdentifier);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "Не удалось загрузить рабочий день. Попробуйте позже." });
        }
    }

    /// <summary> Возвращает список участников организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/members")]
    public async Task<IActionResult> LoadWorkspaceMembers(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.FirstOrDefault(x => x.Id == organizationId);
        if (organization == null)
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.MembersView);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }

        var workspaceAccess = await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organizationId);
        var featureSnapshot = await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId);
        var hasMembersFeature = featureSnapshot.Features.Any(feature =>
            feature.Code == FeatureCodes.MembersModule && feature.BoolValue == true);

        return Ok(new
        {
            Organization = organization,
            WorkspaceAccess = workspaceAccess,
            RolePermissionMatrix = await organizationPermissionService.GetRoleMatrixAsync(user.Id, organizationId),
            CanManageMembers = hasMembersFeature && organization.CanManageMembers && !workspaceAccess.IsReadOnly,
            CanInviteMembers = hasMembersFeature && !workspaceAccess.IsReadOnly
                && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MembersInvite),
            CanManageRoles = hasMembersFeature && !workspaceAccess.IsReadOnly
                && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MembersRolesManage),
            CanRemoveMembers = !workspaceAccess.IsReadOnly
                && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MembersRemove),
            RoleOptions = new[]
            {
                new { Code = OrganizationRoleCodes.Admin, Name = "Администратор" },
                new { Code = OrganizationRoleCodes.Employee, Name = "Сотрудник" },
                new { Code = OrganizationRoleCodes.Performer, Name = "Исполнитель" },
                new { Code = OrganizationRoleCodes.Viewer, Name = "Только просмотр" }
            }
        });
    }

    /// <summary> Возвращает список статей wiki с поиском и фильтрацией. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/wiki")]
    public async Task<IActionResult> LoadWorkspaceWiki(Guid organizationId, string? slug = null, string? q = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.WikiView);
            var articles = wikiContent.Search(q);
            var selectedArticle = articles.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase))
                ?? articles.FirstOrDefault()
                ?? (string.IsNullOrWhiteSpace(q) ? wikiContent.GetDefaultArticle() : null);

            return Ok(new
            {
                Articles = articles,
                SelectedArticle = selectedArticle,
                SearchQuery = q?.Trim() ?? string.Empty,
                HasSearchResults = articles.Count > 0
            });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Возвращает статью wiki по её slug. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/wiki/articles/{slug}")]
    public async Task<IActionResult> LoadWorkspaceWikiArticle(Guid organizationId, string slug)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.WikiView);
            var article = wikiContent.GetBySlug(slug);
            return article == null ? NotFound() : Ok(article);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Возвращает настройки организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/settings")]
    public async Task<IActionResult> LoadWorkspaceSettings(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.FirstOrDefault(x => x.Id == organizationId);
        if (organization == null)
        {
            return NotFound();
        }

        var canOpenSettings =
            await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit)
            || await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceRequisitesEdit)
            || await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceArchive);
        if (!canOpenSettings)
        {
            return BadRequest(new { message = OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.WorkspaceProfileEdit) });
        }

        var workspaceAccess = await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organizationId);
        var featureSnapshot = await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId);

        return Ok(new
        {
            Organization = organization,
            WorkspaceAccess = workspaceAccess,
            WorkspaceFeatures = featureSnapshot.Features,
            CanManageProfile = !workspaceAccess.IsReadOnly
                && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceProfileEdit),
            CanManageRequisites = !workspaceAccess.IsReadOnly
                && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceRequisitesEdit),
            CanArchive = await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceArchive),
            ScenarioCatalog = await organizationScenarioCatalogService.GetAsync(user.Id, organizationId)
        });
    }

    /// <summary> Возвращает данные биллинга организации. </summary>
    [CommercialModule]
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/billing")]
    public async Task<IActionResult> LoadWorkspaceBilling(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.FirstOrDefault(x => x.Id == organizationId);
        if (organization == null)
        {
            return NotFound();
        }

        if (!organization.CanManageBilling)
        {
            logger.LogWarning(
                "Доступ к тарифам запрещён. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                organizationId, user.Id, HttpContext.TraceIdentifier);

            return StatusCode(StatusCodes.Status403Forbidden, ApiErrorResponse.Create(
                StatusCodes.Status403Forbidden,
                "У вас нет права управлять тарифом этой организации. Обратитесь к её владельцу.",
                null,
                HttpContext.TraceIdentifier));
        }

        try
        {
            var demoAvailability = await organizationSubscriptionService.CheckDemoAvailabilityAsync(user.Id, organizationId);
            var extensionInfo = await organizationSubscriptionService.GetSubscriptionExtensionInfoAsync(user.Id, organizationId);

            var plans = await organizationSubscriptionService.GetAvailablePlansAsync(
                user.Id, organizationId, includeUnavailableDemoForTesting: user.CanUseTestSubscriptions);

            return Ok(new
            {
                Organization = organization,
                WorkspaceAccess = await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organizationId),
                WorkspaceFeatures = (await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId)).Features,
                CurrentSubscription = await organizationSubscriptionService.GetCurrentAsync(user.Id, organizationId),
                EffectivePlan = await organizationSubscriptionService.GetEffectivePlanAsync(user.Id, organizationId),
                WorkspaceVehicleLimit = await organizationSubscriptionService.GetVehicleLimitAsync(user.Id, organizationId, 10),
                WorkspaceMembersLimit = await organizationSubscriptionService.GetMembersLimitAsync(user.Id, organizationId, PlatformDefaults.WorkspaceMembersLimit),
                AvailableBusinessPlans = plans.Where(plan => !plan.IsDemoPlan || demoAvailability.Available).ToList(),
                TestDemoPlans = user.CanUseTestSubscriptions
                    ? plans.Where(plan => plan.IsDemoPlan && !demoAvailability.Available).ToList()
                    : [],
                CanUseTestSubscriptions = user.CanUseTestSubscriptions,
                DemoBusinessAvailable = demoAvailability.Available,
                DemoBusinessAvailabilityMessage = demoAvailability.Message,
                CanExtendBusinessSubscription = extensionInfo.CanExtend,
                BusinessDaysUntilExpiry = extensionInfo.DaysUntilExpiry,
                BusinessExtensionMessage = extensionInfo.Message,
                CanManageBilling = organization.CanManageBilling
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Не удалось загрузить тарифы. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                organizationId, user.Id, HttpContext.TraceIdentifier);

            return StatusCode(StatusCodes.Status500InternalServerError, ApiErrorResponse.Create(
                StatusCodes.Status500InternalServerError,
                "Не удалось загрузить тарифы. Попробуйте позже или обратитесь в поддержку.",
                null,
                HttpContext.TraceIdentifier));
        }
    }

    /// <summary> Возвращает статистику рабочего пространства. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/statistics")]
    public async Task<IActionResult> LoadWorkspaceStatistics(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        var organization = overview.Organizations.FirstOrDefault(x => x.Id == organizationId);
        if (organization == null)
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.AnalyticsView);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }

        var featureSnapshot = await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId);
        var hasAnalyticsModule = featureSnapshot.Features.Any(x => x.Code == FeatureCodes.AnalyticsModule && x.BoolValue == true);
        if (!hasAnalyticsModule)
        {
            return BadRequest(new { message = "Модуль статистики не подключен в тарифе организации." });
        }

        var hasClientsModule = featureSnapshot.Features.Any(x => x.Code == FeatureCodes.ClientsModule && x.BoolValue == true);
        var hasDefectsModule = featureSnapshot.Features.Any(x => x.Code == FeatureCodes.DefectsModule && x.BoolValue == true);
        var hasCountersModule = featureSnapshot.Features.Any(x => x.Code == FeatureCodes.CountersModule && x.BoolValue == true);
        var hasMaterialsModule = featureSnapshot.Features.Any(x => x.Code == FeatureCodes.MaterialsModule && x.BoolValue == true);

        return Ok(new
        {
            Organization = organization,
            WorkspaceFeatures = featureSnapshot.Features,
            Analytics = await organizationWorkspaceQueryService.GetAnalyticsAsync(
                organizationId,
                hasClientsModule,
                hasDefectsModule,
                hasCountersModule,
                hasMaterialsModule,
                this.GetOrganizationToday(organization.TimeZoneId))
        });
    }

    /// <summary> Обновляет профиль организации. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/workspace/settings/profile")]
    public async Task<IActionResult> UpdateWorkspaceProfile(Guid organizationId, [FromBody] UpdateOrganizationProfileDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationProfile.UpdateProfileAsync(user.Id, organizationId, dto);
            return Ok(new { message = "Настройки сохранены." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Обновляет реквизиты организации. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/workspace/settings/requisites")]
    public async Task<IActionResult> UpdateWorkspaceRequisites(Guid organizationId, [FromBody] UpdateOrganizationRequisitesDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationProfile.UpdateRequisitesAsync(user.Id, organizationId, dto);
            return Ok(new { message = "Реквизиты сохранены." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Переводит организацию в архив. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/workspace/settings/archive")]
    public async Task<IActionResult> ArchiveWorkspaceOrganization(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationArchive.ArchiveAsync(user.Id, organizationId);
            return Ok(new { message = "Организация отправлена в архив." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Приглашает нового участника в организацию. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/workspace/members/invitations")]
    public async Task<IActionResult> InviteWorkspaceMember(Guid organizationId, [FromBody] InviteOrganizationMemberDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await invitationCommands.InviteMemberAsync(user.Id, organizationId, dto);
            return Ok(new { message = "Приглашение создано." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Не удалось пригласить участника. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                organizationId, user.Id, HttpContext.TraceIdentifier);

            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "Не удалось пригласить участника. Попробуйте позже или обратитесь в поддержку."
            });
        }
    }

    /// <summary> Изменяет роль участника организации. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/workspace/members/{memberId:guid}/role")]
    public async Task<IActionResult> UpdateWorkspaceMemberRole(Guid organizationId, Guid memberId, [FromBody] UpdateWorkspaceMemberRoleRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationMembership.UpdateMemberRoleAsync(user.Id, organizationId, memberId, request.RoleCode);
            return Ok(new { message = "Роль участника обновлена." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Удаляет участника из организации. </summary>
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/workspace/members/{memberId:guid}")]
    public async Task<IActionResult> RemoveWorkspaceMember(Guid organizationId, Guid memberId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationMembership.RemoveMemberAsync(user.Id, organizationId, memberId);
            return Ok(new { message = "Участник удален из организации." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Передаёт права владельца организации другому участнику. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/workspace/members/{memberId:guid}/transfer-ownership")]
    public async Task<IActionResult> TransferWorkspaceOwnership(Guid organizationId, Guid memberId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationMembership.TransferOwnershipAsync(user.Id, organizationId, memberId);
            return Ok(new { message = "Владение организацией передано." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Отзывает приглашение в организацию. </summary>
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/workspace/members/invitations/{invitationId:guid}")]
    public async Task<IActionResult> RevokeWorkspaceInvitation(Guid organizationId, Guid invitationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await invitationCommands.RevokeInvitationAsync(user.Id, organizationId, invitationId);
            return Ok(new { message = "Приглашение отозвано." });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Обновляет права роли в организации. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/workspace/members/permissions/{roleCode}")]
    public async Task<IActionResult> UpdateWorkspaceRolePermissions(Guid organizationId, string roleCode, [FromBody] UpdateOrganizationRolePermissionsDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanWriteAsync(
                user.Id, organizationId, OrganizationPermissionCodes.MembersRolesManage);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogWarning(exception,
                "Отклонено изменение прав роли {RoleCode}. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                roleCode, organizationId, user.Id, this.HttpContext.TraceIdentifier);

            return this.Forbid();
        }

        try
        {
            await organizationPermissionService.UpdateRolePermissionsAsync(user.Id, organizationId, new UpdateOrganizationRolePermissionsDto
            {
                RoleCode = roleCode,
                EnabledPermissionCodes = dto.EnabledPermissionCodes
            });
            return Ok(new { message = "Права роли сохранены." });
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Не удалось изменить права роли {RoleCode}. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                roleCode, organizationId, user.Id, this.HttpContext.TraceIdentifier);

            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Покупает бизнес-тариф для организации. </summary>
    [CommercialModule]
    [HttpPost("/api/v1/organizations/{organizationId:guid}/workspace/billing/plans/{planId:int}/purchase")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> PurchaseWorkspaceBusinessPlan(Guid organizationId, int planId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PaymentBuyerDto? buyer)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            if (user.CanUseTestSubscriptions)
            {
                var testResult = await organizationSubscriptionService.ActivateForTestingAsync(user.Id, organizationId, planId);
                return testResult.Success
                    ? Ok(new { message = "Бизнес-тариф активирован в тестовом режиме." })
                    : BadRequest(new { message = testResult.Error });
            }

            if (!CheckoutHttpResponse.TryGetOperationId(this.HttpContext, out var operationId))
            {
                return this.BadRequest(new { message = "Idempotency-Key должен содержать UUID операции.", code = "Invalid" });
            }

            var result = await checkoutService.PurchaseAsync(user.Id, organizationId, planId, buyer, operationId);

            return CheckoutHttpResponse.Create(result, operationId);
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Активирует демо-тариф организации. </summary>
    [CommercialModule]
    [HttpPost("/api/v1/organizations/{organizationId:guid}/workspace/billing/plans/{planId:int}/activate-demo")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> ActivateWorkspaceDemoPlan(Guid organizationId, int planId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            var result = await organizationSubscriptionService.ActivateDemoAsync(user.Id, organizationId, planId);
            return result.Success
                ? Ok(new { message = "Демо-тариф подключен." })
                : BadRequest(new { message = result.Error });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Продлевает бизнес-тариф организации. </summary>
    [CommercialModule]
    [HttpPost("/api/v1/organizations/{organizationId:guid}/workspace/billing/extend")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(OpenCRM.WebApp.Infrastructure.Security.RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> ExtendWorkspaceBusinessPlan(Guid organizationId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PaymentBuyerDto? buyer)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            if (user.CanUseTestSubscriptions)
            {
                var testResult = await organizationSubscriptionService.ExtendSubscriptionForTestingAsync(user.Id, organizationId);
                return testResult.Success
                    ? Ok(new { message = "Тариф продлен в тестовом режиме." })
                    : BadRequest(new { message = testResult.Error });
            }

            if (!CheckoutHttpResponse.TryGetOperationId(this.HttpContext, out var operationId))
            {
                return this.BadRequest(new { message = "Idempotency-Key должен содержать UUID операции.", code = "Invalid" });
            }

            var result = await checkoutService.ExtendAsync(user.Id, organizationId, buyer, operationId);

            return CheckoutHttpResponse.Create(result, operationId);
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Запрос на смену роли участника. </summary>
    public sealed record UpdateWorkspaceMemberRoleRequest(string RoleCode);

    /// <summary> Возвращает список обслуживаний организации с пагинацией и фильтрами. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/maintenance")]
    public async Task<IActionResult> LoadWorkspaceMaintenance(
        Guid organizationId,
        string? maintenanceDateFrom = null,
        string? maintenanceDateTo = null,
        int maintenancePage = 1,
        int maintenancePageSize = 10)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.MaintenanceView);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }

        var normalizedPageSize = NormalizeWorkspacePageSize(maintenancePageSize);
        var normalizedPage = Math.Max(1, maintenancePage);
        var maintenanceDateRange = ResolveWorkspaceMaintenanceDateRange(
            maintenanceDateFrom,
            maintenanceDateTo,
            await this.GetOrganizationTodayAsync(user.Id, organizationId));
        var maintenanceFeed = await organizationWorkspaceQueryService.GetMaintenancesAsync(
            organizationId,
            maintenanceDateRange.From,
            maintenanceDateRange.To,
            normalizedPage,
            normalizedPageSize);

        return Ok(new
        {
            maintenanceFeed.Items,
            maintenanceFeed.Page,
            maintenanceFeed.PageSize,
            maintenanceFeed.TotalCount,
            TotalPages = Math.Max(1, (int)Math.Ceiling(maintenanceFeed.TotalCount / (double)Math.Max(1, maintenanceFeed.PageSize)))
        });
    }

    /// <summary> Возвращает календарь обслуживаний организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/maintenance-calendar")]
    public async Task<IActionResult> LoadWorkspaceMaintenanceCalendar(
        Guid organizationId,
        string? dateFrom = null,
        string? dateTo = null,
        Guid? vehicleId = null,
        bool includeAllStatuses = false)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.MaintenanceView);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }

        var dateRange = ResolveWorkspaceMaintenanceDateRange(
            dateFrom,
            dateTo,
            await this.GetOrganizationTodayAsync(user.Id, organizationId));
        var items = await organizationWorkspaceQueryService.GetMaintenanceCalendarAsync(organizationId, dateRange.From, dateRange.To, vehicleId, includeAllStatuses);

        var canEdit = await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MaintenanceEdit);

        return Ok(items.Select(item =>
        {
            var result = MapRecentMaintenance(item);
            result.CanReschedule = canEdit && item.Status is MaintenanceStatus.Planned or MaintenanceStatus.InProgress;

            return result;
        }));
    }

    /// <summary> Возвращает календарь задач планировщика организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/scheduler-tasks-calendar")]
    public async Task<IActionResult> LoadWorkspaceSchedulerTasksCalendar(
        Guid organizationId,
        string? dateFrom = null,
        string? dateTo = null,
        Guid? vehicleId = null,
        Guid? clientId = null,
        string? responsibleUserId = null,
        string? taskType = null,
        string? priority = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        var dateRange = ResolveWorkspaceMaintenanceDateRange(
            dateFrom,
            dateTo,
            await this.GetOrganizationTodayAsync(user.Id, organizationId));
        await schedulerRecurrence.GenerateRecurringTaskInstancesAsync(user.Id, organizationId, dateRange.To.AddDays(30));
        var items = await schedulerQueries.GetManualTaskCalendarAsync(
            user.Id,
            organizationId,
            dateRange.From,
            dateRange.To,
            vehicleId,
            clientId,
            responsibleUserId,
            taskType,
            priority);

        return Ok(items);
    }

    /// <summary> Обновляет расписание задачи планировщика. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/workspace/scheduler-tasks/{taskId:guid}/schedule")]
    public async Task<IActionResult> UpdateWorkspaceSchedulerTaskSchedule(
        Guid organizationId,
        Guid taskId,
        [FromBody] UpdateOrganizationSchedulerTaskScheduleDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await schedulerCommands.UpdateManualTaskScheduleAsync(user.Id, organizationId, taskId, dto);
            return NoContent();
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Возвращает последние напоминания планировщика. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/scheduler-reminders")]
    public async Task<IActionResult> LoadWorkspaceSchedulerReminders(Guid organizationId, int take = 20)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            var reminders = await schedulerQueries.GetDueRemindersAsync(user.Id, organizationId, DateTime.UtcNow, take);
            return Ok(reminders);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Отмечает напоминание планировщика как отправленное. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/workspace/scheduler-reminders/{taskId:guid}/sent")]
    public async Task<IActionResult> MarkWorkspaceSchedulerReminderSent(Guid organizationId, Guid taskId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await schedulerCommands.MarkReminderSentAsync(user.Id, organizationId, taskId, DateTime.UtcNow);
            return NoContent();
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Возвращает варианты техники для выбора с поиском. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/workspace/vehicle-options")]
    public async Task<IActionResult> LoadWorkspaceVehicleOptions(
        Guid organizationId,
        string? searchQuery = null,
        Guid? organizationClientId = null,
        int take = 20)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.VehiclesView);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }

        var vehicles = await vehicleOptions.GetOptionsAsync(
            organizationId,
            searchQuery,
            organizationClientId,
            take);

        return Ok(vehicles);
    }

    /// <summary> Возвращает каталог услуг и рабочих постов организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/scenario-catalog")]
    public async Task<IActionResult> LoadScenarioCatalog(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        var canOpenCatalog = await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceCatalogView);
        if (!canOpenCatalog)
        {
            return BadRequest(new { message = OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.WorkspaceCatalogView) });
        }

        return Ok(await organizationScenarioCatalogService.GetAsync(user.Id, organizationId));
    }

    /// <summary> Возвращает незавершённые записи для подтверждения закрытия поста. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/scenario-catalog/{itemId:guid}/affected-maintenance")]
    public async Task<IActionResult> GetWorkPostAffectedMaintenances(Guid organizationId, Guid itemId)
    {
        var user = await userManager.GetUserAsync(this.User);
        if (user is null)
        {
            return this.Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(organization => organization.Id != organizationId))
        {
            return this.NotFound();
        }

        try
        {
            var result = await organizationScenarioCatalogService.GetAffectedWorkPostMaintenancesAsync(user.Id, organizationId, itemId);

            return this.Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (KeyNotFoundException)
        {
            return this.NotFound();
        }
    }

    /// <summary> Создаёт позицию каталога организации. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/scenario-catalog")]
    public async Task<IActionResult> CreateScenarioCatalogItem(Guid organizationId, [FromBody] CreateOrganizationScenarioCatalogItemDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            return Ok(await catalogCommandService.CreateAsync(user.Id, organizationId, dto));
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Обновляет позицию каталога организации. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/scenario-catalog/{itemId:guid}")]
    public async Task<IActionResult> UpdateScenarioCatalogItem(Guid organizationId, Guid itemId, [FromBody] UpdateOrganizationScenarioCatalogItemDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            return Ok(await catalogCommandService.UpdateAsync(user.Id, organizationId, itemId, dto));
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Удаляет позицию каталога организации. </summary>
    [HttpDelete("/api/v1/organizations/{organizationId:guid}/scenario-catalog/{itemId:guid}")]
    public async Task<IActionResult> DeleteScenarioCatalogItem(Guid organizationId, Guid itemId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await catalogCommandService.DeleteAsync(user.Id, organizationId, itemId);
            return NoContent();
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Возвращает материалы, остатки и движения склада. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/materials")]
    public async Task<IActionResult> LoadMaterials(
        Guid organizationId,
        string? searchQuery = null,
        string? stockFilter = null,
        string? movementType = null,
        int stocksPage = 1,
        int stocksPageSize = 20,
        int movementsPage = 1,
        int movementsPageSize = 20,
        DateTime? movementDateFrom = null,
        DateTime? movementDateTo = null,
        DateTime? snapshotAt = null,
        Guid? stockId = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        return Ok(await inventoryQueryService.GetAsync(
            user.Id,
            organizationId,
            searchQuery,
            stockFilter,
            movementType,
            stocksPage,
            stocksPageSize,
            movementsPage,
            movementsPageSize,
            movementDateFrom,
            movementDateTo,
            snapshotAt,
            stockId));
    }

    /// <summary> Возвращает журнал обслуживаний организации с фильтрами. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/maintenance-journal")]
    public async Task<IActionResult> LoadMaintenanceJournal(
        Guid organizationId,
        int status = 3,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? search = null,
        int page = 1,
        int pageSize = 25,
        CancellationToken cancellationToken = default,
        string? handover = null,
        Guid? employeeId = null)
    {
        if (status < 0 || status > 4 || dateFrom > dateTo || dateTo == DateOnly.MaxValue
            || handover is not (null or "ready" or "handedOver"))
        {
            return BadRequest(new { message = "Проверьте статус и период журнала." });
        }

        if (search?.Length > 200)
        {
            return BadRequest(new { message = "Поисковый запрос не должен превышать 200 символов." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(item => item.Id != organizationId))
        {
            return NotFound();
        }

        await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkJournalView);
        if (!await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MaintenanceView))
        {
            return Forbid();
        }

        var canViewClients = await organizationPermissionService.HasPermissionAsync(
            user.Id, organizationId, OrganizationPermissionCodes.ClientsView);
        var canViewPayments = await organizationPermissionService.HasPermissionAsync(
            user.Id, organizationId, OrganizationPermissionCodes.FinanceView)
            || await organizationPermissionService.HasPermissionAsync(
                user.Id, organizationId, OrganizationPermissionCodes.FinanceManage);
        var result = await organizationWorkspaceQueryService.GetMaintenanceJournalAsync(
            organizationId, status == 0 ? null : (MaintenanceStatus)status, dateFrom, dateTo, search, page, pageSize,
            canViewClients, cancellationToken, handover, employeeId, canViewPayments);

        return Ok(result);
    }

    /// <summary> Возвращает журнал работ организации с фильтрами. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/work-journal")]
    public async Task<IActionResult> LoadWorkJournal(
        Guid organizationId,
        string? dateFrom = null,
        string? dateTo = null,
        Guid? vehicleId = null,
        string? kind = null,
        string? tab = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkJournalView);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }

        var organizationTimeZone = await this.GetOrganizationTimeZoneAsync(user.Id, organizationId);
        var (from, to) = ResolveWorkspaceMaintenanceDateRange(
            dateFrom,
            dateTo,
            this.GetOrganizationToday(organizationTimeZone));
        var featureSnapshot = await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId);
        var hasDefectsModule = featureSnapshot.Features.Any(x => x.Code == FeatureCodes.DefectsModule && x.BoolValue == true);

        return Ok(await organizationWorkspaceQueryService.GetWorkJournalAsync(
            organizationId,
            from,
            to,
            vehicleId,
            kind,
            tab,
            hasDefectsModule,
            organizationTimeZone));
    }

    /// <summary> Возвращает финансовые данные организации за период. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/finance")]
    public async Task<IActionResult> LoadFinance(
        Guid organizationId,
        string? dateFrom = null,
        string? dateTo = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            await organizationSubscriptionService.EnsureWorkspaceCanReadAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceView);
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }

        var canViewFinance = await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.FinanceView)
            || await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.FinanceManage);
        if (!canViewFinance)
        {
            return Forbid();
        }

        var featureSnapshot = await organizationSubscriptionService.GetFeatureSnapshotAsync(user.Id, organizationId);
        var hasFinanceModule = featureSnapshot.Features.Any(x => x.Code == FeatureCodes.FinanceModule && x.BoolValue == true);
        if (!hasFinanceModule)
        {
            return NotFound();
        }

        var (from, to) = ResolveWorkspaceFinanceDateRange(
            dateFrom,
            dateTo,
            await this.GetOrganizationTodayAsync(user.Id, organizationId));
        return Ok(await organizationWorkspaceQueryService.GetFinanceSummaryAsync(organizationId, from, to));
    }

    /// <summary> Оформляет поступление материала на склад. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/materials/receipt")]
    public async Task<IActionResult> CreateMaterialReceipt(Guid organizationId, [FromBody] CreateOrganizationMaterialReceiptDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            return Ok(await stockCommandService.AddReceiptAsync(user.Id, organizationId, dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Корректирует остаток материала на складе. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/materials/{stockId:guid}")]
    public async Task<IActionResult> AdjustMaterialStock(Guid organizationId, Guid stockId, [FromBody] AdjustOrganizationMaterialStockDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            return Ok(await stockCommandService.AdjustAsync(user.Id, organizationId, stockId, dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    /// <summary> Оформляет выдачу материала со склада. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/materials/issue")]
    public async Task<IActionResult> IssueMaterial(Guid organizationId, [FromBody] CreateOrganizationMaterialIssueDto dto)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        var overview = await organizationQueries.GetOverviewAsync(user.Id);
        if (overview.Organizations.All(x => x.Id != organizationId))
        {
            return NotFound();
        }

        try
        {
            return Ok(await stockCommandService.IssueAsync(user.Id, organizationId, dto));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception)
        {
            return BadRequest(new { message = "Не удалось выполнить действие. Попробуйте позже или обратитесь в поддержку." });
        }
    }

    private async Task<DateTime> GetOrganizationTodayAsync(string userId, Guid organizationId) =>
        this.GetOrganizationToday(await this.GetOrganizationTimeZoneAsync(userId, organizationId));

    private async Task<TimeZoneInfo> GetOrganizationTimeZoneAsync(string userId, Guid organizationId)
    {
        var organization = await organizationQueries.GetByIdAsync(userId, organizationId);

        return this.ResolveOrganizationTimeZone(organization.TimeZoneId);
    }

    private DateTime GetOrganizationToday(string? timeZoneId) =>
        this.GetOrganizationToday(this.ResolveOrganizationTimeZone(timeZoneId));

    private DateTime GetOrganizationToday(TimeZoneInfo timeZone) => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(systemClock.UtcNow, DateTimeKind.Utc),
        timeZone).Date;

    private TimeZoneInfo ResolveOrganizationTimeZone(string? timeZoneId) => string.IsNullOrWhiteSpace(timeZoneId)
        ? TimeZoneInfo.Utc
        : TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);

    /// <summary> Разбирает период дат для фильтра обслуживаний. </summary>
    private (DateTime From, DateTime To) ResolveWorkspaceMaintenanceDateRange(
        string? dateFrom,
        string? dateTo,
        DateTime today)
    {
        var from = ParseWorkspaceDate(dateFrom) ?? today;
        var to = ParseWorkspaceDate(dateTo) ?? today;

        if (to < from)
        {
            to = from;
        }

        return (
            DateTime.SpecifyKind(from.Date, DateTimeKind.Utc),
            DateTime.SpecifyKind(to.Date, DateTimeKind.Utc));
    }

    /// <summary> Разбирает период дат для финансового раздела. </summary>
    private (DateTime From, DateTime To) ResolveWorkspaceFinanceDateRange(
        string? dateFrom,
        string? dateTo,
        DateTime today)
    {
        var from = ParseWorkspaceDate(dateFrom) ?? today.AddDays(-29);
        var to = ParseWorkspaceDate(dateTo) ?? today;

        if (to < from)
        {
            to = from;
        }

        return (
            DateTime.SpecifyKind(from.Date, DateTimeKind.Utc),
            DateTime.SpecifyKind(to.Date, DateTimeKind.Utc));
    }

    /// <summary> Разбирает строку в дату, возвращая null при ошибке. </summary>
    private static DateTime? ParseWorkspaceDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            return null;
        }

        if (result.Date == DateTime.MaxValue.Date)
        {
            throw new InvalidOperationException("Дата периода должна быть раньше 31.12.9999.");
        }

        return result.Date;
    }

    /// <summary> Приводит размер страницы к допустимому значению. </summary>
    private static int NormalizeWorkspacePageSize(int pageSize)
    {
        return pageSize is 25 or 50 ? pageSize : 10;
    }

    /// <summary> Преобразует DTO обслуживания в модель представления. </summary>
    private static OrganizationRecentMaintenanceViewModel MapRecentMaintenance(OrganizationRecentMaintenanceDto item) =>
        new()
        {
            Id = item.Id,
            Version = item.Version,
            Status = item.Status,
            VehicleId = item.VehicleId,
            VehicleDisplayName = item.VehicleDisplayName,
            VehicleSubtitle = item.VehicleSubtitle,
            RegistrationPlate = item.RegistrationPlate,
            BrandLogoKey = item.BrandLogoKey,
            ServiceNames = item.ServiceNames,
            EmployeeNames = item.EmployeeNames,
            ReadyForHandoverAt = item.ReadyForHandoverAt,
            HandedOverAt = item.HandedOverAt,
            TypeName = item.TypeName,
            StatusName = item.StatusName,
            StatusBadgeClass = GetMaintenanceStatusBadgeClass(item.Status),
            DateLabel = item.IsPlannedDate ? "Плановая дата" : "Дата",
            Date = item.Date,
            PlannedStartTime = item.PlannedStartTime,
            PlannedEndDate = item.PlannedEndDate,
            PlannedEndTime = item.PlannedEndTime,
            Location = item.Location,
            Mileage = item.Mileage,
            MileageLabel = GetMileageUnitShortLabel(item.MileageUnit),
            Comment = item.Comment,
            Price = item.Price,
            CurrencyCode = item.CurrencyCode,
            DetailUrl = item.DetailUrl
        };

    /// <summary> Возвращает CSS-класс бейджа по статусу обслуживания. </summary>
    private static string GetMaintenanceStatusBadgeClass(MaintenanceStatus status) => status switch
    {
        MaintenanceStatus.Planned => "text-bg-info",
        MaintenanceStatus.InProgress => "text-bg-warning",
        MaintenanceStatus.Completed => "text-bg-success",
        MaintenanceStatus.Cancelled => "text-bg-secondary",
        _ => "text-bg-light"
    };

    /// <summary> Возвращает короткую подпись единицы пробега техники. </summary>
    private static string GetMileageUnitShortLabel(VehicleMileageUnit mileageUnit) =>
        mileageUnit == VehicleMileageUnit.EngineHours ? "м/ч" : "км";
}
