using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> Создание записи с атомарной привязкой к рекомендации. </summary>
[ApiController]
[Authorize]
public sealed class OrganizationRepeatVisitBookingsApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationRepeatVisitBookingService bookings) : ControllerBase
{
    [HttpPost("/api/v1/organizations/{organizationId:guid}/repeat-visits/{repeatVisitId:guid}/booking")]
    public async Task<IActionResult> Create(Guid organizationId, Guid repeatVisitId, [FromBody] CreateOrganizationRepeatVisitBookingDto dto)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return this.Unauthorized();
        }

        try
        {
            return this.Ok(await bookings.CreateAsync(userId, organizationId, repeatVisitId, dto));
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (KeyNotFoundException)
        {
            return this.NotFound();
        }
        catch (RepeatVisitConflictException exception)
        {
            return this.Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
    }
}
