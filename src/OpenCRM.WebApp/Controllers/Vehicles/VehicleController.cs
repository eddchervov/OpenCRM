using OpenCRM.Modules.Organizations.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.Modules.Vehicles.Contracts;
using OpenCRM.Modules.Vehicles.Contracts.Dtos;
using OpenCRM.WebApp.Models.Vehicles;
using OpenCRM.WebApp.Navigation;
using System.Text.Json;

namespace OpenCRM.WebApp.Controllers.Vehicles
{
    /// <summary> Контроллер работы с автомобилями и их разделами. </summary>
    [Authorize]
    public class VehicleController(
        UserManager<ApplicationUser> userManager,
        IUsersService usersService,
        IOrganizationQueryService organizationService,
        IOrganizationSubscriptionService organizationSubscriptionService,
        IVehicleQueryService vehicleService)
        : Controller
    {
        /// <summary> Отображает список автомобилей. </summary>
        [HttpGet("vehicles")]
        public async Task<IActionResult> Index(Guid? organizationId = null)
        {
            var user = await userManager.GetUserAsync(User);
            if (user == null)
            {
                return NotFound($"Unable to load user with ID '{userManager.GetUserId(User)}'.");
            }

            await usersService.RecordLastDateOfEntry(user.Id);

            if (organizationId.HasValue)
            {
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(user.Id, organizationId.Value, "vehicles");
            }

            var overview = await organizationService.GetOverviewAsync(user.Id);

            ViewData["EmailConfirmed"] = user.EmailConfirmed;
            ViewData["VehicleScenario"] = new VehicleScenarioViewModel();
            ViewData["TransferOrganizationsJson"] = JsonSerializer.Serialize(
                overview.Organizations.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    roleCode = x.CurrentUserRoleCode
                }));

            return View("List");
        }

        /// <summary> Отображает страницу автомобиля. </summary>
        [HttpGet("vehicle/{vehicleId:guid}")]
        public async Task<IActionResult> Details(Guid vehicleId, Guid? organizationId = null)
        {
            var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

            await usersService.RecordLastDateOfEntry(userId);

            if (organizationId.HasValue)
            {
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(userId, organizationId.Value, $"vehicles/{vehicleId:D}");
            }

            GetVehicleDto vehicle;
            try
            {
                vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
            }
            catch
            {
                return Redirect("/Error/404");
            }

            var vehicleScenario = new VehicleScenarioViewModel();
            var overview = await organizationService.GetOverviewAsync(userId);
            var currentOrganization = organizationId.HasValue
                ? overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value)
                : null;
            ViewData["VehicleId"] = vehicleId.ToString();
            ViewData["VehicleDisplayName"] = BuildVehicleBreadcrumbTitle(vehicle);
            ViewData["VehicleOrganizationId"] = organizationId?.ToString() ?? string.Empty;
            ViewData["VehicleOrganizationName"] = currentOrganization?.Name ?? string.Empty;
            ViewData["VehicleOrganizationMembersCount"] = currentOrganization?.MembersCount ?? 0;
            ViewData["VehicleCanManageSettings"] = currentOrganization?.CanManageSettings ?? false;
            ViewData["VehicleHasVehiclesModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
            ViewData["VehicleHasClientsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
            ViewData["VehicleHasDefectsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
            ViewData["VehicleHasCountersModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
            ViewData["VehicleHasSchedulerModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
            ViewData["VehicleScenario"] = vehicleScenario;
            ViewData["VehicleNextStepTitle"] = vehicleScenario.NextStepTitle;
            ViewData["VehicleNextStepText"] = vehicleScenario.NextStepText;
            ViewData["VehicleNextActions"] = vehicleScenario.NextActions;
            ViewData["TransferOrganizationsJson"] = JsonSerializer.Serialize(
                overview.Organizations.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    roleCode = x.CurrentUserRoleCode
                }));
            return View("VehiclePersonal");
        }

        /// <summary> Отображает журнал автомобиля. </summary>
        [HttpGet("vehicle/{vehicleId:guid}/journal")]
        public async Task<IActionResult> Journal(Guid vehicleId, Guid? organizationId = null, string? returnUrl = null)
        {
            var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

            await usersService.RecordLastDateOfEntry(userId);

            if (organizationId.HasValue)
            {
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(userId, organizationId.Value, $"vehicles/{vehicleId:D}");
            }

            GetVehicleDto vehicle;
            try
            {
                vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
            }
            catch
            {
                return Redirect("/Error/404");
            }

            var vehicleScenario = new VehicleScenarioViewModel();
            var overview = await organizationService.GetOverviewAsync(userId);
            var currentOrganization = organizationId.HasValue
                ? overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value)
                : null;

            ViewData["VehicleId"] = vehicleId.ToString();
            ViewData["VehicleDisplayName"] = BuildVehicleBreadcrumbTitle(vehicle);
            ViewData["VehicleOrganizationId"] = organizationId?.ToString() ?? string.Empty;
            ViewData["VehicleOrganizationName"] = currentOrganization?.Name ?? string.Empty;
            ViewData["VehicleOrganizationMembersCount"] = currentOrganization?.MembersCount ?? 0;
            ViewData["VehicleCanManageSettings"] = currentOrganization?.CanManageSettings ?? false;
            ViewData["VehicleHasVehiclesModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
            ViewData["VehicleHasClientsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
            ViewData["VehicleHasDefectsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
            ViewData["VehicleHasCountersModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
            ViewData["VehicleHasSchedulerModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
            ViewData["VehicleScenario"] = vehicleScenario;
            ViewData["VehicleJournalReturnUrl"] = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
            ViewData["TransferOrganizationsJson"] = JsonSerializer.Serialize(
                overview.Organizations.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    roleCode = x.CurrentUserRoleCode
                }));

            return View("VehicleJournal");
        }

        /// <summary> Отображает фотографии автомобиля. </summary>
        [HttpGet("vehicle/{vehicleId:guid}/photos")]
        public async Task<IActionResult> Photos(Guid vehicleId, Guid? organizationId = null)
        {
            var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

            await usersService.RecordLastDateOfEntry(userId);

            if (organizationId.HasValue)
            {
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(userId, organizationId.Value, $"vehicles/{vehicleId:D}/photos");
            }

            GetVehicleDto vehicle;
            try
            {
                vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
            }
            catch
            {
                return Redirect("/Error/404");
            }

            var vehicleScenario = new VehicleScenarioViewModel();
            var overview = await organizationService.GetOverviewAsync(userId);
            var currentOrganization = organizationId.HasValue
                ? overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value)
                : null;

            ViewData["VehicleId"] = vehicleId.ToString();
            ViewData["VehicleDisplayName"] = BuildVehicleBreadcrumbTitle(vehicle);
            ViewData["VehicleOrganizationId"] = organizationId?.ToString() ?? string.Empty;
            ViewData["VehicleOrganizationName"] = currentOrganization?.Name ?? string.Empty;
            ViewData["VehicleOrganizationMembersCount"] = currentOrganization?.MembersCount ?? 0;
            ViewData["VehicleCanManageSettings"] = currentOrganization?.CanManageSettings ?? false;
            ViewData["VehicleHasVehiclesModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
            ViewData["VehicleHasClientsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
            ViewData["VehicleHasDefectsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
            ViewData["VehicleHasCountersModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
            ViewData["VehicleHasSchedulerModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
            ViewData["VehicleScenario"] = vehicleScenario;

            return View("VehiclePhotos");
        }

        /// <summary> Отображает список фотоальбомов автомобиля. </summary>
        [HttpGet("vehicle/{vehicleId:guid}/albums")]
        public async Task<IActionResult> Albums(Guid vehicleId, Guid? organizationId = null)
        {
            var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

            await usersService.RecordLastDateOfEntry(userId);

            if (organizationId.HasValue)
            {
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(userId, organizationId.Value, $"vehicles/{vehicleId:D}/albums");
            }

            GetVehicleDto vehicle;
            try
            {
                vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
            }
            catch
            {
                return Redirect("/Error/404");
            }

            var vehicleScenario = new VehicleScenarioViewModel();
            var overview = await organizationService.GetOverviewAsync(userId);
            var currentOrganization = organizationId.HasValue
                ? overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value)
                : null;

            ViewData["VehicleId"] = vehicleId.ToString();
            ViewData["VehicleDisplayName"] = BuildVehicleBreadcrumbTitle(vehicle);
            ViewData["VehicleOrganizationId"] = organizationId?.ToString() ?? string.Empty;
            ViewData["VehicleOrganizationName"] = currentOrganization?.Name ?? string.Empty;
            ViewData["VehicleOrganizationMembersCount"] = currentOrganization?.MembersCount ?? 0;
            ViewData["VehicleCanManageSettings"] = currentOrganization?.CanManageSettings ?? false;
            ViewData["VehicleHasVehiclesModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
            ViewData["VehicleHasClientsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
            ViewData["VehicleHasDefectsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
            ViewData["VehicleHasCountersModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
            ViewData["VehicleHasSchedulerModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
            ViewData["VehicleScenario"] = vehicleScenario;

            return View("VehicleAlbums");
        }

        /// <summary> Отображает содержимое фотоальбома автомобиля. </summary>
        [HttpGet("vehicle/{vehicleId:guid}/albums/{albumId:guid}")]
        public async Task<IActionResult> Album(Guid vehicleId, Guid albumId, Guid? organizationId = null)
        {
            var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

            await usersService.RecordLastDateOfEntry(userId);

            if (organizationId.HasValue)
            {
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(userId, organizationId.Value, $"vehicles/{vehicleId:D}/albums/{albumId:D}");
            }

            GetVehicleDto vehicle;
            try
            {
                vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
            }
            catch
            {
                return Redirect("/Error/404");
            }

            var vehicleScenario = new VehicleScenarioViewModel();
            var overview = await organizationService.GetOverviewAsync(userId);
            var currentOrganization = organizationId.HasValue
                ? overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value)
                : null;

            ViewData["VehicleId"] = vehicleId.ToString();
            ViewData["VehicleDisplayName"] = BuildVehicleBreadcrumbTitle(vehicle);
            ViewData["VehicleAlbumId"] = albumId.ToString();
            ViewData["VehicleOrganizationId"] = organizationId?.ToString() ?? string.Empty;
            ViewData["VehicleOrganizationName"] = currentOrganization?.Name ?? string.Empty;
            ViewData["VehicleOrganizationMembersCount"] = currentOrganization?.MembersCount ?? 0;
            ViewData["VehicleCanManageSettings"] = currentOrganization?.CanManageSettings ?? false;
            ViewData["VehicleHasVehiclesModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
            ViewData["VehicleHasClientsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
            ViewData["VehicleHasDefectsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
            ViewData["VehicleHasCountersModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
            ViewData["VehicleHasSchedulerModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
            ViewData["VehicleScenario"] = vehicleScenario;

            return View("VehicleAlbum");
        }

        /// <summary> Отображает форму редактирования автомобиля. </summary>
        [HttpGet("vehicle/{vehicleId:guid}/edit")]
        public async Task<IActionResult> Edit(Guid vehicleId, Guid? organizationId = null, string? returnUrl = null)
        {
            var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

            await usersService.RecordLastDateOfEntry(userId);

            if (organizationId.HasValue)
            {
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(userId, organizationId.Value, $"vehicles/{vehicleId:D}/edit");
            }

            GetVehicleDto vehicle;
            try
            {
                vehicle = await vehicleService.GetById(userId, organizationId, vehicleId);
            }
            catch
            {
                return Redirect("/Error/404");
            }

            var vehicleScenario = new VehicleScenarioViewModel();
            var overview = await organizationService.GetOverviewAsync(userId);
            var currentOrganization = organizationId.HasValue
                ? overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value)
                : null;
            var defaultReturnUrl = organizationId.HasValue
                ? $"/vehicle/{vehicleId}?organizationId={organizationId.Value}"
                : $"/vehicle/{vehicleId}";
            var resolvedReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : defaultReturnUrl;

            ViewData["VehicleId"] = vehicleId.ToString();
            ViewData["VehicleDisplayName"] = BuildVehicleBreadcrumbTitle(vehicle);
            ViewData["VehicleOrganizationId"] = organizationId?.ToString() ?? string.Empty;
            ViewData["VehicleOrganizationName"] = currentOrganization?.Name ?? string.Empty;
            ViewData["VehicleOrganizationMembersCount"] = currentOrganization?.MembersCount ?? 0;
            ViewData["VehicleCanManageSettings"] = currentOrganization?.CanManageSettings ?? false;
            ViewData["VehicleHasVehiclesModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
            ViewData["VehicleHasClientsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
            ViewData["VehicleHasDefectsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
            ViewData["VehicleHasCountersModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
            ViewData["VehicleHasSchedulerModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
            ViewData["VehicleScenario"] = vehicleScenario;
            ViewData["VehicleNextStepTitle"] = vehicleScenario.NextStepTitle;
            ViewData["VehicleNextStepText"] = vehicleScenario.NextStepText;
            ViewData["VehicleNextActions"] = vehicleScenario.NextActions;
            ViewData["VehicleEditReturnUrl"] = resolvedReturnUrl;
            ViewData["VehicleFormMode"] = "edit";
            ViewData["TransferOrganizationsJson"] = JsonSerializer.Serialize(
                overview.Organizations.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    roleCode = x.CurrentUserRoleCode
                }));

            return View("VehicleEditPersonal");
        }

        /// <summary> Отображает форму создания автомобиля. </summary>
        [HttpGet("vehicle/create")]
        public async Task<IActionResult> Create(Guid? organizationId = null, Guid? organizationClientId = null, string? returnUrl = null)
        {
            var userId = userManager.GetUserId(User) ?? throw new InvalidOperationException("Current user was not found.");

            await usersService.RecordLastDateOfEntry(userId);

            if (organizationId.HasValue)
            {
                var section = organizationClientId.HasValue
                    ? $"vehicles/new/{organizationClientId.Value:D}"
                    : "vehicles/new";
                return await RedirectToModernOrganizationVehicleWorkspaceAsync(userId, organizationId.Value, section);
            }

            var vehicleScenario = new VehicleScenarioViewModel();
            var overview = await organizationService.GetOverviewAsync(userId);
            var currentOrganization = organizationId.HasValue
                ? overview.Organizations.FirstOrDefault(x => x.Id == organizationId.Value)
                : null;
            var defaultReturnUrl = organizationId.HasValue
                ? "/organizations"
                : "/vehicles";
            var resolvedReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : defaultReturnUrl;

            ViewData["VehicleId"] = string.Empty;
            ViewData["VehicleOrganizationId"] = organizationId?.ToString() ?? string.Empty;
            ViewData["VehicleOrganizationClientId"] = organizationClientId?.ToString() ?? string.Empty;
            ViewData["VehicleLockOrganizationClient"] = organizationId.HasValue && organizationClientId.HasValue;
            ViewData["VehicleOrganizationName"] = currentOrganization?.Name ?? string.Empty;
            ViewData["VehicleOrganizationMembersCount"] = currentOrganization?.MembersCount ?? 0;
            ViewData["VehicleCanManageSettings"] = currentOrganization?.CanManageSettings ?? false;
            ViewData["VehicleHasVehiclesModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.VehiclesModule);
            ViewData["VehicleHasClientsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.ClientsModule);
            ViewData["VehicleHasDefectsModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.DefectsModule);
            ViewData["VehicleHasCountersModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.CountersModule);
            ViewData["VehicleHasSchedulerModule"] = organizationId.HasValue
                && await organizationSubscriptionService.HasFeatureAsync(userId, organizationId.Value, Helpers.FeatureCodes.SchedulerModule);
            ViewData["VehicleScenario"] = vehicleScenario;
            ViewData["VehicleEditReturnUrl"] = resolvedReturnUrl;
            ViewData["VehicleFormMode"] = "create";
            ViewData["TransferOrganizationsJson"] = JsonSerializer.Serialize(
                overview.Organizations.Select(x => new
                {
                    id = x.Id,
                    name = x.Name,
                    roleCode = x.CurrentUserRoleCode
                }));

            return View("VehicleCreatePersonal");
        }

        /// <summary> Выполняет перенаправление в рабочую область автомобиля организации. </summary>
        private async Task<IActionResult> RedirectToModernOrganizationVehicleWorkspaceAsync(string userId, Guid organizationId, string section)
        {
            var organization = await organizationService.GetByIdAsync(userId, organizationId);
            return Redirect(OrganizationWorkspaceUrlHelper.Resolve(organization, section));
        }

        /// <summary> Формирует название автомобиля для хлебных крошек. </summary>
        private static string BuildVehicleBreadcrumbTitle(GetVehicleDto vehicle)
        {
            var title = Helpers.VehicleDisplayName.Format(vehicle.BrandName, vehicle.ModelName, vehicle.RegistrationPlate);

            return vehicle.YearIssue.HasValue ? $"{title} {vehicle.YearIssue.Value} г." : title;
        }
    }
}



