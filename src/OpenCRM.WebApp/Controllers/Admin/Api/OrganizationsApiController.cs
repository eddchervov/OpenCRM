using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.WebApp.Dtos.Admin.Organizations;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API получения списка организаций для администратора. </summary>
[Route("api/v1/admin/organizations")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewUsers)]
public sealed class OrganizationsApiController(
    IAdminOrganizationService adminOrganizationService,
    IOrganizationArchiveService organizationArchiveService,
    IAuthTokenService authTokenService) : ControllerBase
{
    /// <summary> Возвращает список организаций. </summary>
    [HttpGet]
    public async Task<IActionResult> Get(
        string? search = null, string? user = null, string sort = "created", bool descending = true,
        int? page = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var organizations = page == null && search == null && user == null
            ? await adminOrganizationService.GetAllAsync()
            : await adminOrganizationService.GetAsync(
                search, user, sort, descending, page ?? 1, pageSize, cancellationToken);

        return Ok(organizations);
    }

    /// <summary> Возвращает карточку и статистику организации. </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken cancellationToken)
    {
        var organization = await adminOrganizationService.GetByIdAsync(id, cancellationToken);

        return organization == null ? NotFound() : Ok(organization);
    }

    /// <summary> Архивирует организацию от имени администратора с обязательной причиной. </summary>
    [HttpPost("{id:guid}/archive")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageUsers)]
    public Task<IActionResult> Archive(Guid id, ChangeAdminOrganizationArchiveRequest request) =>
        this.ChangeArchiveStateAsync(id, request, shouldArchive: true);

    /// <summary> Восстанавливает организацию от имени администратора с обязательной причиной. </summary>
    [HttpPost("{id:guid}/restore")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageUsers)]
    public Task<IActionResult> Restore(Guid id, ChangeAdminOrganizationArchiveRequest request) =>
        this.ChangeArchiveStateAsync(id, request, shouldArchive: false);

    private async Task<IActionResult> ChangeArchiveStateAsync(
        Guid organizationId, ChangeAdminOrganizationArchiveRequest request, bool shouldArchive)
    {
        var admin = await authTokenService.GetCurrentUserAsync(this.User);
        if (admin == null)
        {
            return this.Unauthorized();
        }

        try
        {
            if (shouldArchive)
            {
                await organizationArchiveService.ArchiveByAdminAsync(admin.Id, organizationId, request.Reason);
            }
            else
            {
                await organizationArchiveService.RestoreByAdminAsync(admin.Id, organizationId, request.Reason);
            }
        }
        catch (ArgumentException exception)
        {
            return this.BadRequest(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return this.Conflict(new { message = exception.Message });
        }

        return this.Ok(new
        {
            message = shouldArchive ? "Организация отправлена в архив." : "Организация восстановлена."
        });
    }
}
