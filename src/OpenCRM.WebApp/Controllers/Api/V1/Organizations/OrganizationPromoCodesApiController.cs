using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.WebApp.Models.Organizations;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary>
/// API организационных промокодов на странице тарифов организации.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/organizations/{organizationId:guid}/workspace/billing/promocodes")]
[CommercialModule]
public class OrganizationPromoCodesApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationPromoCodeService promoCodeService,
    ILogger<OrganizationPromoCodesApiController> logger) : ControllerBase
{
    /// <summary> Возвращает список применённых промокодов организации. </summary>
    [HttpGet]
    public async Task<IActionResult> GetApplications(Guid organizationId)
    {
        try
        {
            var actorId = await this.ResolveActorIdAsync();
            if (actorId is null)
            {
                return this.Unauthorized();
            }

            var applications = await promoCodeService.GetApplicationsAsync(actorId, organizationId);

            return Ok(applications);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception)
        {
            // Не раскрываем детали непредвиденной ошибки из БД клиенту.
            logger.LogError(exception, "Неожиданная ошибка при получении организационных промокодов. OrganizationId: {OrganizationId}", organizationId);

            return this.StatusCode(StatusCodes.Status500InternalServerError, new { message = "Не удалось получить промокоды. Попробуйте позже." });
        }
    }

    /// <summary> Возвращает страницу истории промокодов организации. </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory(Guid organizationId, int page = 1, int pageSize = 20)
    {
        var actorId = await this.ResolveActorIdAsync();
        if (actorId is null)
        {
            return this.Unauthorized();
        }

        try
        {
            var result = await promoCodeService.GetApplicationsPageAsync(actorId, organizationId, page, pageSize);

            return this.Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Применяет промокод организации к тарифу. </summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicyNames.Payments)]
    public async Task<IActionResult> Apply(Guid organizationId, [FromBody] ApplyOrganizationPromoCodeDto dto)
    {
        try
        {
            var actorId = await this.ResolveActorIdAsync();
            if (actorId is null)
            {
                return this.Unauthorized();
            }

            var application = await promoCodeService.ApplyAsync(actorId, organizationId, dto.Code);

            return Ok(application);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception)
        {
            // Не раскрываем детали непредвиденной ошибки из БД клиенту.
            logger.LogError(exception, "Неожиданная ошибка при применении организационного промокода. OrganizationId: {OrganizationId}", organizationId);

            return this.StatusCode(StatusCodes.Status500InternalServerError, new { message = "Не удалось применить промокод. Попробуйте позже." });
        }
    }

    /// <summary> Определяет идентификатор текущего пользователя как инициатора действия. </summary>
    private async Task<string?> ResolveActorIdAsync()
    {
        var user = await userManager.GetUserAsync(this.User);
        if (user is null)
        {
            return null;
        }

        return user.Id;
    }
}
