using OpenCRM.Services.Interfaces.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер быстрых записей клиентов на детейлинг организации. </summary>
[ApiController]
[Authorize]
public sealed class OrganizationDetailingQuickBookingsApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationDetailingQuickBookingService detailingQuickBookingService,
    IApplicationUnitOfWork unitOfWork,
    ILogger<OrganizationDetailingQuickBookingsApiController> logger)
    : ControllerBase
{
    /// <summary> Сообщение об неожиданной ошибке при создании быстрой записи. </summary>
    private const string UnexpectedCreateErrorMessage = "Не удалось создать запись. Попробуйте позже или обратитесь в поддержку.";

    /// <summary> Создаёт быструю запись клиента на детейлинг организации. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/detailing/bookings/quick")]
    public async Task<ActionResult<OrganizationDetailingQuickBookingResultDto>> Create(
        Guid organizationId,
        [FromBody] CreateOrganizationDetailingQuickBookingDto dto)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            OrganizationDetailingQuickBookingResultDto? result = null;
            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                result = await detailingQuickBookingService.CreateAsync(userId, organizationId, dto);
            });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to create detailing quick booking for organization {OrganizationId} by user {UserId}", organizationId, userId);
            return BadRequest(new { message = UnexpectedCreateErrorMessage });
        }
    }
}
