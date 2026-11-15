using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-конвертации обращения организации в плановую запись. </summary>
[ApiController]
[Authorize]
public sealed class OrganizationInquiryBookingsApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationInquiryBookingService inquiryBookingService,
    ILogger<OrganizationInquiryBookingsApiController> logger) : ControllerBase
{
    /// <summary> Создаёт плановую запись из обращения. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/inquiries/{inquiryId:guid}/booking")]
    public async Task<ActionResult<OrganizationInquiryBookingResultDto>> Create(
        Guid organizationId,
        Guid inquiryId,
        [FromBody] CreateOrganizationInquiryBookingDto dto)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var result = await inquiryBookingService.CreateAsync(userId, organizationId, inquiryId, dto);

            return Ok(result);
        }
        catch (InquiryConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Не удалось создать запись из обращения {InquiryId} организации {OrganizationId} пользователем {UserId}",
                inquiryId,
                organizationId,
                userId);

            return StatusCode(
                StatusCodes.Status500InternalServerError,
                new { message = "Не удалось создать запись. Попробуйте позже или обратитесь в поддержку." });
        }
    }
}
