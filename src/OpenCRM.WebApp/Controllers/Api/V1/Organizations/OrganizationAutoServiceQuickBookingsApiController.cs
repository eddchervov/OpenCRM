using OpenCRM.Services.Interfaces.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер быстрых записей клиентов в автосервис организации. </summary>
[ApiController]
[Authorize]
public sealed class OrganizationAutoServiceQuickBookingsApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationAutoServiceQuickBookingService autoServiceQuickBookingService,
    IApplicationUnitOfWork unitOfWork,
    ILogger<OrganizationAutoServiceQuickBookingsApiController> logger)
    : ControllerBase
{
    /// <summary> Сообщение об неожиданной ошибке при создании быстрой записи. </summary>
    private const string UnexpectedCreateErrorMessage = "Не удалось создать приемку. Попробуйте позже или обратитесь в поддержку.";

    /// <summary> Создаёт быструю запись клиента в автосервис организации. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/autoservice/bookings/quick")]
    public async Task<ActionResult<OrganizationAutoServiceQuickBookingResultDto>> Create(
        Guid organizationId,
        [FromBody] CreateOrganizationAutoServiceQuickBookingDto dto)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            OrganizationAutoServiceQuickBookingResultDto? result = null;
            await unitOfWork.ExecuteInTransactionAsync(async () =>
            {
                result = await autoServiceQuickBookingService.CreateAsync(userId, organizationId, dto);
            });

            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to create autoservice quick booking for organization {OrganizationId} by user {UserId}", organizationId, userId);
            return BadRequest(new { message = UnexpectedCreateErrorMessage });
        }
    }
}
