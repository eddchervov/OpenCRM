using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace OpenCRM.WebApp.Controllers.Pages;

/// <summary> Контроллер страницы уведомлений пользователя. </summary>
[Authorize]
[Route("notifications")]
public sealed class NotificationsController : Controller
{
    /// <summary> Отображает список уведомлений. </summary>
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
