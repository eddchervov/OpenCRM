using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Features;

namespace OpenCRM.WebApp.Controllers.Api.V1.Features;

/// <summary> API доступа к функциям текущего пользователя. </summary>
[ApiController]
[Route("api/v1/features")]
[Authorize]
public class FeaturesController : ControllerBase
{
    /// <summary> Сервис проверки доступа к функциям. </summary>
    private readonly IFeatureAccessService _featureAccessService;

    /// <summary> Менеджер пользователей Identity. </summary>
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary> Создаёт контроллер с сервисом доступа к функциям. </summary>
    public FeaturesController(IFeatureAccessService featureAccessService, UserManager<ApplicationUser> userManager)
    {
        _featureAccessService = featureAccessService;
        _userManager = userManager;
    }

    /// <summary> Возвращает функции, доступные текущему пользователю. </summary>
    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentUserFeatures()
    {
        var userId = _userManager.GetUserId(User);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var snapshot = await _featureAccessService.GetSnapshotAsync(userId);
        return Ok(snapshot);
    }
}

