using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.WebApp.Infrastructure;
using OpenCRM.WebApp.Navigation;
using OpenCRM.WebApp.Product;
using OpenCRM.WebApp.Models.Organizations;
using OpenCRM.WebApp.Infrastructure.Localization;
using Microsoft.Extensions.Localization;

namespace OpenCRM.WebApp.Controllers.Organizations;

/// <summary> Контроллер для работы с организациями: создание, восстановление, приглашения и подписка. </summary>
[Authorize]
public class OrganizationsController(
    UserManager<ApplicationUser> userManager,
    IUsersService usersService,
    IOrganizationQueryService organizationQueries,
    IOrganizationProfileCommandService organizationProfile,
    IOrganizationArchiveService organizationArchive,
    IOrganizationInvitationQueryService invitationQueries,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationTypeCatalog organizationTypeCatalog,
    ILogger<OrganizationsController> logger,
    IStringLocalizer<LocalizationStrings> text) : Controller
{
    /// <summary> Отображает список организаций пользователя. </summary>
    [HttpGet("/organizations")]
    public async Task<IActionResult> Index()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        await usersService.RecordLastDateOfEntry(user.Id);

        var invitations = await invitationQueries.GetPendingInvitationsAsync(user.Id);

        var model = new OrganizationIndexViewModel
        {
            Overview = await organizationQueries.GetOverviewAsync(user.Id),
            Invitations = invitations,
            StatusMessage = TempData["Success"]?.ToString(),
            ErrorMessage = TempData["Error"]?.ToString()
        };

        if (model.Overview.Organizations.Count == 0 && invitations.Count == 0)
        {
            return RedirectToAction(nameof(Create));
        }

        model.OrganizationTypeOptions = await GetOrganizationTypeOptionsAsync(user.Id);
        this.ApplyOrganizationCreationAccess(model.OrganizationTypeOptions, out var canCreateOrganization, out var createMessage);
        model.CanCreateOrganization = canCreateOrganization;
        model.CreateOrganizationMessage = createMessage;

        var workspaceAccessByOrganizationId = new Dictionary<Guid, OrganizationWorkspaceAccessDto>();
        foreach (var organization in model.Overview.Organizations)
        {
            workspaceAccessByOrganizationId[organization.Id] =
                await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organization.Id);
        }

        model.WorkspaceAccessByOrganizationId = workspaceAccessByOrganizationId;

        return View(model);
    }

    /// <summary> Отображает форму создания организации. </summary>
    [HttpGet("/organizations/create")]
    public async Task<IActionResult> Create()
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        await usersService.RecordLastDateOfEntry(user.Id);

        var model = await BuildCreateViewModelAsync(
            user.Id,
            new CreateOrganizationInputModel(),
            TempData["Success"]?.ToString(),
            TempData["Error"]?.ToString());

        return View(model);
    }

    /// <summary> Обрабатывает отправку формы создания организации. </summary>
    [HttpPost("/organizations/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateOrganization([Bind(Prefix = "CreateOrganization")] CreateOrganizationInputModel model)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        if (!ModelState.IsValid)
        {
            var viewModel = await BuildCreateViewModelAsync(
                user.Id,
                model,
                errorMessage: text["organization.create.invalid"]);
            return View(nameof(Create), viewModel);
        }

        try
        {
            var organizationId = await organizationProfile.CreateAsync(user.Id, new CreateOrganizationDto
            {
                Name = model.Name,
                Description = model.Description,
                PrimaryScenarioCode = model.PrimaryScenarioCode,
                CurrencyCode = model.CurrencyCode,
                TimeZoneId = model.TimeZoneId
            });

            TempData["Success"] = text["organization.create.success"].Value;
            TempData["AnalyticsGoal"] = "organization_created";
            TempData["AnalyticsParams"] = System.Text.Json.JsonSerializer.Serialize(new
            {
                scenario = model.PrimaryScenarioCode
            });
            return Redirect(OrganizationWorkspaceUrlHelper.Resolve(organizationId, model.PrimaryScenarioCode));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to create organization for user {UserId}", user.Id);
            var viewModel = await BuildCreateViewModelAsync(user.Id, model, errorMessage: text[ex.Message].Value);
            return View(nameof(Create), viewModel);
        }
    }

    /// <summary> Восстанавливает архивную организацию по её идентификатору. </summary>
    [HttpPost("/organizations/{organizationId:guid}/restore")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restore(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
        }

        try
        {
            await organizationArchive.RestoreAsync(user.Id, organizationId);
            TempData["Success"] = text["organization.restoreSuccess"].Value;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to restore organization {OrganizationId} by user {UserId}", organizationId, user.Id);
            TempData["Error"] = text[ex.Message].Value;
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary> Возвращает типы организаций с учётом ограничений владельца. </summary>
    private async Task<IReadOnlyList<OrganizationTypeChoiceViewModel>> GetOrganizationTypeOptionsAsync(string userId)
    {
        var organizationTypes = await organizationTypeCatalog.GetOrganizationTypeOptionsAsync();
        var overview = await organizationQueries.GetOverviewAsync(userId);
        var ownedOrganizations = overview.Organizations
            .Concat(overview.ArchivedOrganizations)
            .Where(organization => organization.CurrentUserRoleCode == OrganizationRoleCodes.Owner)
            .ToArray();

        return organizationTypes.Select(type =>
        {
            var matchingOrganizations = ownedOrganizations
                .Where(organization => NormalizeOrganizationTypeCode(organization.PrimaryScenarioCode)
                    == NormalizeOrganizationTypeCode(type.Code))
                .ToArray();
            var archivedOrganizationExists = matchingOrganizations.Any(organization => organization.IsArchived);
            var isAvailable = matchingOrganizations.Length == 0;

            return new OrganizationTypeChoiceViewModel
            {
                Code = type.Code,
                Name = type.Name,
                Description = type.Description,
                IsAvailable = isAvailable,
                HasArchivedOrganization = archivedOrganizationExists,
                UnavailableMessage = isAvailable
                    ? null
                    : archivedOrganizationExists
                        ? text["organization.archivedType"].Value
                        : text["organization.existingType"].Value
            };
        }).ToArray();
    }

    /// <summary> Формирует модель представления страницы создания организации. </summary>
    private async Task<OrganizationCreateViewModel> BuildCreateViewModelAsync(
        string userId,
        CreateOrganizationInputModel input,
        string? statusMessage = null,
        string? errorMessage = null)
    {
        var organizationTypeOptions = await GetOrganizationTypeOptionsAsync(userId);
        var model = new OrganizationCreateViewModel
        {
            OrganizationTypeOptions = organizationTypeOptions,
            CreateOrganization = input,
            StatusMessage = statusMessage,
            ErrorMessage = errorMessage
        };

        this.ApplyOrganizationCreationAccess(model.OrganizationTypeOptions, out var canCreateOrganization, out var createMessage);
        model.CanCreateOrganization = canCreateOrganization;
        model.CreateOrganizationMessage = createMessage;

        return model;
    }

    /// <summary> Определяет, может ли пользователь создавать организации, и текст сообщения. </summary>
    private void ApplyOrganizationCreationAccess(
        IReadOnlyList<OrganizationTypeChoiceViewModel> organizationTypeOptions,
        out bool canCreateOrganization,
        out string? createMessage)
    {
        if (organizationTypeOptions.Any(option => option.IsAvailable))
        {
            canCreateOrganization = true;
            createMessage = null;
            return;
        }

        canCreateOrganization = false;
        createMessage = organizationTypeOptions.Any(option => option.HasArchivedOrganization)
            ? text["organization.allTypesArchived"].Value
            : text["organization.allTypesExist"].Value;
    }

    private static string NormalizeOrganizationTypeCode(string code) => code.Trim().Replace('_', '-').ToLowerInvariant();
}
