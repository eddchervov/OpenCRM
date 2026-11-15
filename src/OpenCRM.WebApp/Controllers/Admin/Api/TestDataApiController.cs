using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API генерации тестовых данных для администратора. </summary>
[Route("api/v1/admin/test-data")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ManagePermissions)]
public sealed class TestDataApiController(IAdminTestDataService adminTestDataService) : ControllerBase
{
    /// <summary> Ищет организации по подстроке. </summary>
    [HttpGet("organizations")]
    public async Task<IReadOnlyList<AdminTestDataOrganizationDto>> SearchOrganizations(string? search = null)
    {
        return await adminTestDataService.SearchOrganizationsAsync(search);
    }

    /// <summary> Возвращает страницу организаций для тестовых данных. </summary>
    [HttpGet("organizations/page")]
    public async Task<AdminTestDataOrganizationsPageDto> SearchOrganizationsPage(
        string? search = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return await adminTestDataService.SearchOrganizationsPageAsync(search, page, pageSize, cancellationToken);
    }

    /// <summary> Возвращает выбранную организацию для тестовых данных. </summary>
    [HttpGet("organizations/{organizationId:guid}")]
    public async Task<IActionResult> GetOrganization(Guid organizationId, CancellationToken cancellationToken)
    {
        var organization = await adminTestDataService.GetOrganizationAsync(organizationId, cancellationToken);
        if (organization == null)
        {
            return NotFound();
        }

        return Ok(organization);
    }

    /// <summary> Возвращает автомобили организации. </summary>
    [HttpGet("organizations/{organizationId:guid}/vehicles")]
    public async Task<IActionResult> GetVehicles(Guid organizationId)
    {
        var result = await adminTestDataService.GetVehiclesAsync(organizationId);
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        return Ok(result.Vehicles);
    }

    /// <summary> Генерирует тестовые автомобили организации. </summary>
    [HttpPost("organizations/{organizationId:guid}/vehicles/generate")]
    public async Task<IActionResult> GenerateVehicles(
        Guid organizationId,
        [FromBody] AdminGenerateTestVehiclesRequest request)
    {
        var result = await adminTestDataService.GenerateVehiclesAsync(organizationId, request.Count);

        return MapGenerationResult(result);
    }

    /// <summary> Генерирует тестовые обслуживания автомобиля. </summary>
    [HttpPost("organizations/{organizationId:guid}/vehicles/{vehicleId:guid}/maintenances/generate")]
    public async Task<IActionResult> GenerateMaintenances(
        Guid organizationId,
        Guid vehicleId,
        [FromBody] AdminGenerateTestMaintenancesRequest request)
    {
        var result = await adminTestDataService.GenerateMaintenancesAsync(
            organizationId,
            vehicleId,
            request.Count,
            request.Mode);

        return MapGenerationResult(result);
    }

    /// <summary> Генерирует тестовые дефекты автомобиля. </summary>
    [HttpPost("organizations/{organizationId:guid}/vehicles/{vehicleId:guid}/defects/generate")]
    public async Task<IActionResult> GenerateDefects(
        Guid organizationId,
        Guid vehicleId,
        [FromBody] AdminGenerateTestDefectsRequest request)
    {
        var result = await adminTestDataService.GenerateDefectsAsync(organizationId, vehicleId, request.Count);

        return MapGenerationResult(result);
    }

    /// <summary> Преобразует результат генерации в HTTP-ответ. </summary>
    private IActionResult MapGenerationResult(AdminTestDataGenerationResultDto result)
    {
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        return Ok(new
        {
            result.Message,
            result.CreatedCount
        });
    }
}
