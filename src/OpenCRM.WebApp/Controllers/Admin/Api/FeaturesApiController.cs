using Microsoft.AspNetCore.Authorization;
using OpenCRM.Services.Interfaces.Admin;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления определениями возможностей. </summary>
[ApiController]
[Route("api/v1/admin/features")]
[Authorize(Policy = AdminAuthorizationPolicies.ManageConfiguration)]
public class FeaturesApiController : ControllerBase
{
    /// <summary> Зависимость сервиса администрирования возможностей. </summary>
    private readonly IFeatureAdminService _featureAdminService;

    /// <summary> Создаёт контроллер с сервисом администрирования возможностей. </summary>
    public FeaturesApiController(IFeatureAdminService featureAdminService)
    {
        _featureAdminService = featureAdminService;
    }

    /// <summary> Возвращает все определения возможностей. </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var features = await _featureAdminService.GetDefinitionsAsync();
        return Ok(features);
    }

    /// <summary> Возвращает определение возможности по идентификатору. </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var feature = await _featureAdminService.GetDefinitionByIdAsync(id);
        return feature == null ? NotFound() : Ok(feature);
    }

    /// <summary> Создаёт новое определение возможности. </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] FeatureDefinitionUpsertDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var feature = await _featureAdminService.CreateDefinitionAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = feature.Id }, feature);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Обновляет определение возможности. </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FeatureDefinitionUpsertDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            var feature = await _featureAdminService.UpdateDefinitionAsync(id, dto);
            return feature == null ? NotFound() : Ok(feature);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Переключает состояние активности возможности. </summary>
    [HttpPatch("{id:int}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var result = await _featureAdminService.ToggleDefinitionStatusAsync(id);
        return result
            ? Ok(new { message = "Статус фичи обновлен." })
            : NotFound();
    }
}



