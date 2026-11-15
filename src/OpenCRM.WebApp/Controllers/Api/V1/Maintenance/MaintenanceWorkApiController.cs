using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using OpenCRM.Modules.Maintenance.Contracts.Photos;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> API-контроллер выполнения работ по операциям обслуживания. </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[Route("api/v1/organizations/{organizationId:guid}/work")]
public class MaintenanceWorkApiController(
    IMaintenanceWorkQueryService workQueryService,
    IMaintenanceWorkExecutionService workExecutionService,
    IMaintenanceWorkReportService workReportService,
    IMaintenanceWorkTimeService workTimeService,
    IOperationMaterialQueryService operationMaterialQueryService,
    IOperationMaterialCommandService operationMaterialCommandService,
    IMaintenancePhotoService maintenancePhotoService,
    IFileStorageService fileStorageService) : ControllerBase
{
    /// <summary> Возвращает фотографии по операции обслуживания. </summary>
    [HttpGet("{maintenanceId:guid}/operations/{detailId:guid}/photos")]
    public async Task<IActionResult> GetPhotos(Guid organizationId, Guid maintenanceId, Guid detailId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            var photos = await maintenancePhotoService.GetOperationPhotosAsync(userId, organizationId, maintenanceId, detailId);
            var result = new List<object>();
            foreach (var photo in photos)
            {
                result.Add(new
                {
                    photoId = photo.PhotoId,
                    stage = photo.Stage,
                    createdAt = photo.CreatedAt,
                    detailId = photo.DetailId,
                    url = await fileStorageService.GeneratePresignedUrlAsync(photo.Path)
                });
            }

            return Ok(result);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary> Загружает фотографии для операции обслуживания. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/photos")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhotos(
        Guid organizationId, Guid maintenanceId, Guid detailId,
        [FromForm] IFormFileCollection files, [FromQuery] MaintenancePhotoStage stage = MaintenancePhotoStage.Unspecified)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }
        if (files.Count is < 1 or > 10 || files.Sum(file => file.Length) > 100 * 1024 * 1024
            || files.Any(file => file.Length == 0 || !file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { message = "Выберите до 10 изображений общим размером не более 100 МБ." });
        }

        var streams = files.Select(file => file.OpenReadStream()).ToList();
        try
        {
            await maintenancePhotoService.UploadOperationPhotosAsync(
                userId, organizationId, maintenanceId, detailId,
                files.Zip(streams, (file, stream) => (Stream: stream, FileName: file.FileName)), stage);

            return NoContent();
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        finally
        {
            foreach (var stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    /// <summary> Возвращает отчёты по работам обслуживания, опционально по операции. </summary>
    [HttpGet("{maintenanceId:guid}/reports")]
    public async Task<IActionResult> GetReports(Guid organizationId, Guid maintenanceId, [FromQuery] Guid? detailId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workQueryService.GetReportsAsync(userId, organizationId, maintenanceId, detailId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает производственный отчёт по обслуживанию. </summary>
    [HttpGet("{maintenanceId:guid}/production-report")]
    public async Task<IActionResult> GetProductionReport(Guid organizationId, Guid maintenanceId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workQueryService.GetProductionReportAsync(userId, organizationId, maintenanceId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Корректирует трудозатраты по операции обслуживания. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/time-adjustments")]
    public async Task<IActionResult> AdjustTime(
        Guid organizationId, Guid maintenanceId, Guid detailId, [FromBody] AdjustMaintenanceWorkTimeDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workTimeService.AdjustTimeAsync(userId, organizationId, maintenanceId, detailId, command));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Время уже изменено. Обновите карточку." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Добавляет отчёт по выполнению операции обслуживания. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/reports")]
    public async Task<IActionResult> AddReport(
        Guid organizationId, Guid maintenanceId, Guid detailId, [FromBody] CreateMaintenanceWorkReportDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workReportService.AddReportAsync(userId, organizationId, maintenanceId, detailId, command));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Сообщение уже сохранено. Обновите карточку." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Принимает решение по отчёту по работе обслуживания. </summary>
    [HttpPost("{maintenanceId:guid}/reports/{reportId:guid}/decision")]
    public async Task<IActionResult> DecideReport(
        Guid organizationId, Guid maintenanceId, Guid reportId, [FromBody] DecideMaintenanceWorkReportDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workReportService.DecideReportAsync(userId, organizationId, maintenanceId, reportId, command));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Решение уже принято. Обновите карточку." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает доступные для выдачи материалы операции. </summary>
    [HttpGet("{maintenanceId:guid}/operations/{detailId:guid}/materials/available")]
    public async Task<IActionResult> GetAvailableMaterials(Guid organizationId, Guid maintenanceId, Guid detailId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await operationMaterialQueryService.GetAvailableForOperationAsync(
                userId, organizationId, maintenanceId, detailId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Оформляет выдачу материала по операции обслуживания. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/materials")]
    public async Task<IActionResult> IssueMaterial(
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        [FromBody] IssueOperationMaterialDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await operationMaterialCommandService.IssueForOperationAsync(
                userId, organizationId, maintenanceId, detailId, command));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Остаток или работа уже изменились. Обновите список." });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Остаток или работа уже изменились. Обновите список." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает выданные материалы по операции обслуживания. </summary>
    [HttpGet("{maintenanceId:guid}/operations/{detailId:guid}/materials")]
    public async Task<IActionResult> GetIssuedMaterials(Guid organizationId, Guid maintenanceId, Guid detailId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await operationMaterialQueryService.GetIssuedForOperationAsync(
                userId, organizationId, maintenanceId, detailId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Оформляет возврат выданного материала по операции. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/materials/{movementId:guid}/return")]
    public async Task<IActionResult> ReturnMaterial(
        Guid organizationId, Guid maintenanceId, Guid detailId, Guid movementId,
        [FromBody] ReverseOperationMaterialDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await operationMaterialCommandService.ReverseOperationMaterialAsync(
                userId, organizationId, maintenanceId, detailId, movementId, command));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Списание уже изменено. Обновите список." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает работы, назначенные исполнителю в организации. </summary>
    [HttpGet("assigned")]
    public async Task<IActionResult> GetAssigned(Guid organizationId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workQueryService.GetAssignedAsync(userId, organizationId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает доступные к выполнению работы организации. </summary>
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailable(Guid organizationId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workQueryService.GetAvailableAsync(userId, organizationId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Берёт операцию обслуживания в работу. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/claim")]
    public async Task<IActionResult> Claim(Guid organizationId, Guid maintenanceId, Guid detailId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workExecutionService.ClaimAsync(userId, organizationId, maintenanceId, detailId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Работа уже назначена. Обновите список." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает прогресс выполнения работ по обслуживанию. </summary>
    [HttpGet("{maintenanceId:guid}/progress")]
    public async Task<IActionResult> GetProgress(Guid organizationId, Guid maintenanceId)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workQueryService.GetProgressAsync(userId, organizationId, maintenanceId));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Возвращает операцию обслуживания на доработку. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/return")]
    public async Task<IActionResult> ReturnForRework(
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        [FromBody] MaintenanceWorkCommandDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            await workExecutionService.ReturnForReworkAsync(userId, organizationId, maintenanceId, detailId, command);

            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Работа уже изменена. Обновите карточку." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Переводит операцию обслуживания в следующее состояние. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/transition")]
    public async Task<IActionResult> Transition(
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        [FromBody] MaintenanceWorkCommandDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workExecutionService.ExecuteAsync(userId, organizationId, maintenanceId, detailId, command));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Работа уже изменена. Обновите список." });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Работа уже изменена. Обновите список." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    /// <summary> Завершает работу за назначенного сотрудника. </summary>
    [HttpPost("{maintenanceId:guid}/operations/{detailId:guid}/complete-for-employee")]
    public async Task<IActionResult> CompleteForEmployee(
        Guid organizationId,
        Guid maintenanceId,
        Guid detailId,
        [FromBody] MaintenanceWorkCommandDto command)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await workExecutionService.CompleteForEmployeeAsync(
                userId, organizationId, maintenanceId, detailId, command));
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Работа уже изменена. Обновите карточку." });
        }
        catch (DbUpdateException)
        {
            return Conflict(new { message = "Работа уже изменена. Обновите карточку." });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
