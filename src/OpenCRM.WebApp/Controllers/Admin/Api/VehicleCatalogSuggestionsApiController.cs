using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary>
/// API модерации предложений в каталог техники.
/// </summary>
[Route("api/v1/admin/catalog-suggestions")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewContent)]
public sealed class VehicleCatalogSuggestionsApiController(
    IAdminCatalogSuggestionService adminCatalogSuggestionService) : ControllerBase
{
    /// <summary>
    /// Возвращает список предложений.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] VehicleCatalogSuggestionStatus? status = null)
    {
        var suggestions = await adminCatalogSuggestionService.GetAsync(status);

        return Ok(suggestions);
    }

    /// <summary>
    /// Одобряет предложение и добавляет запись в каталог.
    /// </summary>
    [HttpPost("{id:int}/approve")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageContent)]
    public async Task<IActionResult> Approve(int id, [FromBody] AdminReviewCatalogSuggestionDto dto)
    {
        var result = await adminCatalogSuggestionService.ApproveAsync(id, GetAdminUserId(), dto.Comment);

        return MapOperationResult(result);
    }

    /// <summary>
    /// Отклоняет предложение.
    /// </summary>
    [HttpPost("{id:int}/reject")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageContent)]
    public async Task<IActionResult> Reject(int id, [FromBody] AdminReviewCatalogSuggestionDto dto)
    {
        var result = await adminCatalogSuggestionService.RejectAsync(id, GetAdminUserId(), dto.Comment);

        return MapOperationResult(result);
    }

    /// <summary>
    /// Удаляет предложение, не затрагивая записи каталога техники.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageContent)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await adminCatalogSuggestionService.DeleteAsync(id, cancellationToken);

        return MapOperationResult(result, includeResult: true);
    }

    /// <summary> Возвращает идентификатор текущего администратора. </summary>
    private string GetAdminUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.Identity?.Name ?? "admin";
    }

    /// <summary> Преобразует результат операции в HTTP-ответ. </summary>
    private IActionResult MapOperationResult(AdminOperationResult result)
    {
        return this.MapOperationResult(result, includeResult: false);
    }

    /// <summary> Преобразует результат операции удаления с сохранением DTO в HTTP-ответе. </summary>
    private IActionResult MapOperationResult(AdminOperationResult result, bool includeResult)
    {
        if (result.NotFound)
        {
            return includeResult ? NotFound(result) : NotFound(new { message = result.ErrorMessage });
        }

        if (!result.Succeeded)
        {
            return includeResult ? BadRequest(result) : BadRequest(new { message = result.ErrorMessage });
        }

        return includeResult ? Ok(result) : Ok(new { message = result.Message });
    }
}
