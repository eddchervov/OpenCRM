using OpenCRM.WebApp.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API управления промокодами. </summary>
[ApiController]
[Route("api/v1/admin/promo-codes")]
[Authorize(Policy = AdminAuthorizationPolicies.ManageConfiguration)]
[CommercialModule]
public class PromoCodesAdminController(IPromoCodeService promoCodeService) : ControllerBase
{
    /// <summary> Возвращает все промокоды. </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await promoCodeService.GetAllAsync());
    }

    /// <summary> Возвращает промокод по идентификатору. </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var promoCode = await promoCodeService.GetByIdAsync(id);

        return promoCode == null ? NotFound() : Ok(promoCode);
    }

    /// <summary> Возвращает список применений промокода. </summary>
    [HttpGet("{id:int}/usages")]
    public async Task<IActionResult> GetUsages(int id)
    {
        if (await promoCodeService.GetByIdAsync(id) == null)
        {
            return NotFound();
        }

        return Ok(await promoCodeService.GetUsagesAsync(id));
    }

    /// <summary> Создаёт новый промокод. </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PromoCodeSaveDto dto)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var promoCode = await promoCodeService.CreateAsync(dto);

            return CreatedAtAction(nameof(GetById), new { id = promoCode.Id }, promoCode);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Обновляет промокод по идентификатору. </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] PromoCodeSaveDto dto)
    {
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var promoCode = await promoCodeService.UpdateAsync(id, dto);

            return promoCode == null ? NotFound() : Ok(promoCode);
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Устанавливает состояние активности промокода. </summary>
    [HttpPatch("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] SetPromoCodeStatusDto dto)
    {
        return await promoCodeService.SetActiveAsync(id, dto.IsActive)
            ? Ok()
            : NotFound();
    }

    /// <summary> Удаляет промокод по идентификатору. </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            return await promoCodeService.DeleteAsync(id)
                ? NoContent()
                : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Данные запроса на изменение состояния промокода. </summary>
    public class SetPromoCodeStatusDto
    {
        /// <summary> Признак активности промокода. </summary>
        public bool IsActive { get; set; }
    }
}
