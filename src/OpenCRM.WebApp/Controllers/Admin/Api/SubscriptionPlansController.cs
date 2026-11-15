using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using OpenCRM.Services.Interfaces.Admin;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using OpenCRM.WebApp.Dtos.Admin;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos.Admin;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления тарифными планами подписок. </summary>
[ApiController]
[Route("api/v1/admin/subscription-plans")]
[Authorize(Policy = AdminAuthorizationPolicies.ManageConfiguration)]
[CommercialModule]
public class SubscriptionPlansAdminController : ControllerBase
{
    /// <summary> Сервис администрирования тарифных планов. </summary>
    private readonly ISubscriptionPlanAdminService _subscriptionPlanAdminService;

    /// <summary> Сервис администрирования функций. </summary>
    private readonly IFeatureAdminService _featureAdminService;

    /// <summary> Создаёт контроллер с сервисами планов и функций. </summary>
    public SubscriptionPlansAdminController(
        ISubscriptionPlanAdminService subscriptionPlanAdminService,
        IFeatureAdminService featureAdminService)
    {
        _subscriptionPlanAdminService = subscriptionPlanAdminService;
        _featureAdminService = featureAdminService;
    }

    /// <summary> Возвращает список всех тарифных планов. </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var subscriptionPlans = await _subscriptionPlanAdminService.GetAllSubscriptionPlansAsync();
        return Ok(subscriptionPlans);
    }

    /// <summary> Возвращает тарифный план по идентификатору. </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        var subscriptionPlan = await _subscriptionPlanAdminService.GetSubscriptionPlanByIdAsync(id);
        if (subscriptionPlan == null)
            return NotFound();

        return Ok(subscriptionPlan);
    }

    /// <summary> Создаёт новый тарифный план. </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SubscriptionPlanCreateDto createDto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var subscriptionPlan = await _subscriptionPlanAdminService.CreateSubscriptionPlanAsync(createDto);
            return CreatedAtAction(nameof(GetById), new { id = subscriptionPlan.Id }, subscriptionPlan);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Обновляет данные тарифного плана. </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] SubscriptionPlanUpdateDto updateDto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        try
        {
            var subscriptionPlan = await _subscriptionPlanAdminService.UpdateSubscriptionPlanAsync(id, updateDto);
            if (subscriptionPlan == null)
                return NotFound(new { message = "Тариф не найден." });

            return Ok(subscriptionPlan);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Переключает статус тарифного плана. </summary>
    [HttpPatch("{id}/toggle-status")]
    public async Task<IActionResult> ToggleStatus(int id)
    {
        var result = await _subscriptionPlanAdminService.ToggleSubscriptionPlanStatusAsync(id);
        if (!result)
            return await CreateStatusErrorResultAsync(id);

        return Ok(new { message = "Статус тарифа обновлен." });
    }

    /// <summary> Активирует тарифный план. </summary>
    [HttpPatch("{id}/activate")]
    public async Task<IActionResult> Activate(int id)
    {
        var result = await _subscriptionPlanAdminService.ActivateSubscriptionPlanAsync(id);
        if (!result)
            return NotFound(new { message = "Тариф не найден." });

        return Ok(new { message = "Тариф активирован." });
    }

    /// <summary> Деактивирует тарифный план. </summary>
    [HttpPatch("{id}/deactivate")]
    public async Task<IActionResult> Deactivate(int id)
    {
        var result = await _subscriptionPlanAdminService.DeactivateSubscriptionPlanAsync(id);
        if (!result)
            return await CreateStatusErrorResultAsync(id);

        return Ok(new { message = "Тариф деактивирован." });
    }

    /// <summary> Удаляет тарифный план. </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _subscriptionPlanAdminService.DeleteSubscriptionPlanAsync(id);
        if (!result)
        {
            var plan = await _subscriptionPlanAdminService.GetSubscriptionPlanByIdAsync(id);
            if (plan == null)
            {
                return NotFound(new { message = "Тариф не найден." });
            }

            if (plan.IsDefaultPlan)
            {
                return BadRequest(new { message = "Тариф по умолчанию нельзя удалить." });
            }

            if (plan.HasUsageHistory)
            {
                return BadRequest(new { message = "Тариф уже использовался в подписках. Его можно только отключить, но не удалить." });
            }

            return BadRequest(new { message = "Не удалось удалить тариф." });
        }

        return Ok(new { message = "Тариф удален." });
    }

    /// <summary> Возвращает функции, включённые в план. </summary>
    [HttpGet("{id}/features")]
    public async Task<IActionResult> GetPlanFeatures(int id)
    {
        try
        {
            var features = await _featureAdminService.GetPlanFeaturesAsync(id);
            return Ok(features);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Сохраняет набор функций тарифного плана. </summary>
    [HttpPut("{id}/features")]
    public async Task<IActionResult> SavePlanFeatures(int id, [FromBody] SavePlanFeaturesDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        try
        {
            await _featureAdminService.SavePlanFeaturesAsync(id, dto);
            return Ok(new { message = "Функции тарифа сохранены для всех сроков оплаты." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Формирует ответ с ошибкой недопустимого статуса плана. </summary>
    private async Task<IActionResult> CreateStatusErrorResultAsync(int id)
    {
        var plan = await _subscriptionPlanAdminService.GetSubscriptionPlanByIdAsync(id);
        if (plan == null)
        {
            return NotFound(new { message = "Тариф не найден." });
        }

        if (plan.IsDefaultPlan)
        {
            return BadRequest(new { message = "Тариф по умолчанию нельзя отключить." });
        }

        if (plan.HasUsageHistory && !plan.IsActive)
        {
            return BadRequest(new { message = "Тариф уже находится в неактивном списке." });
        }

        return BadRequest(new { message = "Нельзя отключить последний активный обычный тариф." });
    }
}



