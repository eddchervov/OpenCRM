using OpenCRM.Services.Interfaces.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Word;
using System;
using System.Threading.Tasks;

namespace OpenCRM.WebApp.Controllers.Api.V1.Documents;

/// <summary> API экспорта документов в Word. </summary>
[Authorize]
[ApiController]
[Route("api/v1/documents/word")]
public class WordController : ControllerBase
{
    /// <summary> Сервис экспорта Word-документов. </summary>
    private readonly IWordDocumentExportService _wordDocumentExportService;

    /// <summary> Менеджер пользователей Identity. </summary>
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary> Создаёт контроллер с сервисом экспорта Word. </summary>
    public WordController(IWordDocumentExportService wordDocumentExportService, UserManager<ApplicationUser> userManager)
    {
        this._wordDocumentExportService = wordDocumentExportService;
        this._userManager = userManager;
    }

    /// <summary> Формирует Word-карточку автомобиля. </summary>
    [HttpGet("vehicle/{vehicleId:guid}")]
    public async Task<IActionResult> GenerateVehicleWord(Guid vehicleId, [FromQuery] bool includePhotos = false, [FromQuery] Guid? organizationId = null)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._wordDocumentExportService.GenerateVehicleWord(userId, vehicleId, includePhotos, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (WordController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (WordController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать Word-документ." });
        }
    }

    /// <summary> Формирует Word-документ обслуживания. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}")]
    public async Task<IActionResult> GenerateMaintenanceWord(Guid maintenanceId, [FromQuery] bool includePhotos = false, [FromQuery] Guid? organizationId = null)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._wordDocumentExportService.GenerateMaintenanceWord(userId, maintenanceId, includePhotos, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (WordController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (WordController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать Word-документ." });
        }
    }

    /// <summary> Формирует Word-акт выполненных работ. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/work-act")]
    public async Task<IActionResult> GenerateMaintenanceWorkActWord(Guid maintenanceId, [FromQuery] Guid organizationId)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._wordDocumentExportService.GenerateMaintenanceWorkActWord(userId, maintenanceId, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (WordController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (WordController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать акт выполненных работ." });
        }
    }

    /// <summary> Формирует Word-наряд на работы. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/work-order")]
    public async Task<IActionResult> GenerateMaintenanceWorkOrderWord(Guid maintenanceId, [FromQuery] Guid organizationId)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._wordDocumentExportService.GenerateMaintenanceWorkOrderWord(userId, maintenanceId, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (WordController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (WordController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать заказ-наряд." });
        }
    }

    /// <summary> Формирует Word-документ обслуживания по шаблону. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/scenario/{templateId:guid}")]
    public async Task<IActionResult> GenerateScenarioMaintenanceDocumentWord(Guid maintenanceId, Guid templateId, [FromQuery] Guid organizationId)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._wordDocumentExportService.GenerateScenarioMaintenanceDocumentWord(userId, maintenanceId, templateId, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (WordController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (WordController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать сценарный Word-документ." });
        }
    }

    /// <summary> Преобразует результат экспорта в HTTP-ответ. </summary>
    private IActionResult ToActionResult(DocumentExportResult result, bool notFoundAsMessage = false)
    {
        return result.Outcome switch
        {
            DocumentExportOutcome.File when result.FileName is null => this.File(result.Content!, result.ContentType!),
            DocumentExportOutcome.File => this.File(result.Content!, result.ContentType!, result.FileName!),
            DocumentExportOutcome.AccessDenied => this.StatusCode(StatusCodes.Status403Forbidden, new { message = result.Message }),
            DocumentExportOutcome.NotFound when notFoundAsMessage => this.NotFound(new { message = result.Message }),
            DocumentExportOutcome.NotFound => this.NotFound(result.Message),
            DocumentExportOutcome.InvalidRequest => this.BadRequest(new { message = result.Message }),
            _ => throw new InvalidOperationException("Неизвестный результат экспорта документа.")
        };
    }

    /// <summary> Определяет, является ли исключение ошибкой доступа. </summary>
    private static bool IsAccessDeniedError(Exception exception)
    {
        return exception.Message.Contains("нет доступа", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только на просмотр", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("только для чтения", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary> Определяет, является ли исключение ошибкой отсутствия данных. </summary>
    private static bool IsNotFoundError(Exception exception)
    {
        return exception.Message.Contains("не найден", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }
}
