using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API управления часовым поясом организации. </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[Route("api/v1/organizations/{organizationId:guid}/time-zone")]
public sealed class OrganizationTimeZoneApiController(IOrganizationTimeZoneService service) : ControllerBase
{
    /// <summary> Возвращает часовой пояс организации и право его изменения. </summary>
    [HttpGet]
    public Task<IActionResult> Get(Guid organizationId) => this.ExecuteAsync(() =>
        service.GetAsync(this.GetRequiredUserId(), organizationId));

    /// <summary> Условно обновляет часовой пояс организации. </summary>
    [HttpPut]
    public Task<IActionResult> Update(Guid organizationId, [FromBody] UpdateOrganizationTimeZoneDto dto) =>
        this.ExecuteAsync(() => service.UpdateAsync(this.GetRequiredUserId(), organizationId, dto));

    private string GetRequiredUserId() => this.GetApiUserId() ?? throw new UnauthorizedAccessException();

    private async Task<IActionResult> ExecuteAsync<T>(Func<Task<T>> action)
    {
        if (string.IsNullOrWhiteSpace(this.GetApiUserId()))
        {
            return this.Unauthorized();
        }

        try
        {
            return this.Ok(await action());
        }
        catch (UnauthorizedAccessException)
        {
            return this.Forbid();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            return this.Conflict(new { message = exception.Message });
        }
        catch (KeyNotFoundException exception)
        {
            return this.NotFound(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
    }
}
