using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления каталогом техники. </summary>
[Route("api/v1/admin/catalog")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ManageContent)]
public sealed class VehicleCatalogAdminApiController(
    IAdminVehicleCatalogService adminVehicleCatalogService,
    IAdminVehicleCatalogSeedService adminVehicleCatalogSeedService)
    : ControllerBase
{
    /// <summary> Наполняет каталог техники базовыми данными. </summary>
    [HttpPost("seed")]
    public async Task<IActionResult> Seed(CancellationToken cancellationToken)
    {
        var result = await adminVehicleCatalogSeedService.SeedAsync(cancellationToken);

        return Ok(result);
    }

    /// <summary> Возвращает список марок с фильтрами. </summary>
    [HttpGet("brands")]
    public async Task<IActionResult> GetBrands([FromQuery] VehicleKind? kind = null, [FromQuery] string? search = null)
    {
        var brands = await adminVehicleCatalogService.GetBrandsAsync(kind, search);

        return Ok(brands);
    }

    /// <summary> Создаёт марку техники. </summary>
    [HttpPost("brands")]
    public async Task<IActionResult> CreateBrand([FromBody] AdminSaveBrandDto dto)
    {
        var result = await adminVehicleCatalogService.CreateBrandAsync(dto);

        return MapOperationResult(result);
    }

    /// <summary> Обновляет марку техники. </summary>
    [HttpPut("brands/{brandId:int}")]
    public async Task<IActionResult> UpdateBrand(int brandId, [FromBody] AdminSaveBrandDto dto)
    {
        var result = await adminVehicleCatalogService.UpdateBrandAsync(brandId, dto);

        return MapOperationResult(result);
    }

    /// <summary> Удаляет марку техники. </summary>
    [HttpDelete("brands/{brandId:int}")]
    public async Task<IActionResult> DeleteBrand(int brandId)
    {
        var result = await adminVehicleCatalogService.DeleteBrandAsync(brandId);

        return MapOperationResult(result);
    }

    /// <summary> Возвращает модели марки с поиском. </summary>
    [HttpGet("brands/{brandId:int}/models")]
    public async Task<IActionResult> GetModels(int brandId, [FromQuery] string? search = null)
    {
        var result = await adminVehicleCatalogService.GetModelsAsync(brandId, search);
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        return Ok(result.Models);
    }

    /// <summary> Создаёт модель техники. </summary>
    [HttpPost("brands/{brandId:int}/models")]
    public async Task<IActionResult> CreateModel(int brandId, [FromBody] AdminSaveModelDto dto)
    {
        var result = await adminVehicleCatalogService.CreateModelAsync(brandId, dto);

        return MapOperationResult(result);
    }

    /// <summary> Обновляет модель техники. </summary>
    [HttpPut("models/{modelId:int}")]
    public async Task<IActionResult> UpdateModel(int modelId, [FromBody] AdminSaveModelDto dto)
    {
        var result = await adminVehicleCatalogService.UpdateModelAsync(modelId, dto);

        return MapOperationResult(result);
    }

    /// <summary> Удаляет модель техники. </summary>
    [HttpDelete("models/{modelId:int}")]
    public async Task<IActionResult> DeleteModel(int modelId)
    {
        var result = await adminVehicleCatalogService.DeleteModelAsync(modelId);

        return MapOperationResult(result);
    }

    /// <summary> Преобразует результат операции в HTTP-ответ. </summary>
    private IActionResult MapOperationResult(AdminOperationResult result)
    {
        if (result.NotFound)
        {
            return NotFound(new { message = result.ErrorMessage });
        }

        if (!result.Succeeded)
        {
            return BadRequest(new { message = result.ErrorMessage });
        }

        if (result.Id.HasValue)
        {
            return Ok(new { message = result.Message, id = result.Id.Value });
        }

        return Ok(new { message = result.Message });
    }
}
