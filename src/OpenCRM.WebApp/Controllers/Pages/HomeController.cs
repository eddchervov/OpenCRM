using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Modules.Organizations.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Users;
using OpenCRM.WebApp.Infrastructure;
using OpenCRM.WebApp.Navigation;

namespace OpenCRM.WebApp.Controllers.Pages
{
    /// <summary>
    /// Корневой маршрут платформы: показывает нейтральную главную страницу,
    /// пользователя с организациями — в рабочую область выбранной организации.
    /// Публичных страниц продукта здесь нет.
    /// </summary>
    public class HomeController : Controller
    {
        /// <summary> Сервис работы с данными пользователей. </summary>
        private readonly IUsersService _usersService;

        /// <summary> Менеджер пользователей Identity. </summary>
        private readonly UserManager<ApplicationUser> _userManager;

        /// <summary> Сервис запросов данных об организациях. </summary>
        private readonly IOrganizationQueryService _organizationService;

        /// <summary> Создаёт контроллер с его зависимостями. </summary>
        public HomeController(
            IUsersService usersService,
            UserManager<ApplicationUser> userManager,
            IOrganizationQueryService organizationService)
        {
            _usersService = usersService;
            _userManager = userManager;
            _organizationService = organizationService;
        }

        /// <summary> Открывает рабочее пространство или начало работы с организацией. </summary>
        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);

            if (string.IsNullOrEmpty(userId))
            {
                return View();
            }

            var teamOverview = await _organizationService.GetOverviewAsync(userId);

            if (TryResolveCurrentOrganization(teamOverview, out var currentOrganization))
            {
                CurrentOrganizationCookie.Write(Response, currentOrganization.Id);
                return Redirect(Url.Content("~" + OrganizationWorkspaceUrlHelper.Resolve(currentOrganization)));
            }

            return this.Redirect(Url.Content("~/organizations"));
        }

        /// <summary>
        /// Направляет вошедшего пользователя к обращениям в поддержку.
        /// Публичной страницы контактов в платформе нет: её оформляет приложение установки,
        /// поэтому для анонимного запроса возвращается «не найдено».
        /// </summary>
        [AllowAnonymous]
        [Route("contacts")]
        public IActionResult Contacts()
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return this.Redirect(Url.Content("~/personal/support"));
            }

            return NotFound();
        }

        /// <summary> Пытается определить текущую организацию из обзора команды. </summary>
        private bool TryResolveCurrentOrganization(TeamOverviewDto teamOverview, out OrganizationDto organization)
        {
            organization = null!;

            if (teamOverview.Organizations.Count == 0)
            {
                return false;
            }

            if (CurrentOrganizationCookie.TryRead(Request, out var selectedOrganizationId))
            {
                var selectedOrganization = teamOverview.Organizations.FirstOrDefault(x => x.Id == selectedOrganizationId);
                if (selectedOrganization != null)
                {
                    organization = selectedOrganization;
                    return true;
                }
            }

            organization = teamOverview.Organizations[0];
            return true;
        }
    }
}
