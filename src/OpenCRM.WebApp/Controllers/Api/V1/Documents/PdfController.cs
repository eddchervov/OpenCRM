using OpenCRM.Services.Interfaces.Documents.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Documents;
using OpenCRM.Services.Interfaces.Pdf;
using System;
using System.Threading.Tasks;

namespace OpenCRM.WebApp.Controllers.Api.V1.Documents;

/// <summary> API экспорта документов в PDF. </summary>
[Authorize]
[ApiController]
[Route("api/v1/documents/pdf")]
public class PdfController : ControllerBase
{
    /// <summary> Сервис экспорта PDF-документов. </summary>
    private readonly IPdfDocumentExportService _pdfDocumentExportService;

    /// <summary> Менеджер пользователей Identity. </summary>
    private readonly UserManager<ApplicationUser> _userManager;

    /// <summary> Создаёт контроллер с сервисом экспорта PDF. </summary>
    public PdfController(IPdfDocumentExportService pdfDocumentExportService, UserManager<ApplicationUser> userManager)
    {
        this._pdfDocumentExportService = pdfDocumentExportService;
        this._userManager = userManager;
    }

    /// <summary> Формирует PDF-карточку автомобиля. </summary>
    [HttpGet("vehicle/{vehicleId:guid}")]
    public async Task<IActionResult> GenerateVehiclePdf(Guid vehicleId, [FromQuery] bool includePhotos = false, [FromQuery] Guid? organizationId = null)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._pdfDocumentExportService.GenerateVehiclePdf(userId, vehicleId, includePhotos, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (PdfController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (PdfController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать PDF." });
        }
    }

    /// <summary> Формирует PDF-документ обслуживания. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}")]
    public async Task<IActionResult> GenerateMaintenancePdf(Guid maintenanceId, [FromQuery] bool includePhotos = false, [FromQuery] Guid? organizationId = null)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._pdfDocumentExportService.GenerateMaintenancePdf(userId, maintenanceId, includePhotos, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (PdfController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (PdfController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать PDF." });
        }
    }

    /// <summary> Формирует печатную форму приёмки обслуживания. </summary>
    [HttpGet("/api/v1/maintenance/{maintenanceId:guid}/intake/print")]
    public async Task<IActionResult> PrintMaintenanceIntake(Guid maintenanceId, [FromQuery] Guid organizationId)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._pdfDocumentExportService.PrintMaintenanceIntake(userId, maintenanceId, organizationId);

            return this.ToActionResult(result, notFoundAsMessage: true);
        }
        catch (Exception ex) when (PdfController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (PdfController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
    }

    /// <summary> Формирует PDF акта выполненных работ. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/work-act")]
    public async Task<IActionResult> GenerateMaintenanceWorkActPdf(Guid maintenanceId, [FromQuery] Guid organizationId)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._pdfDocumentExportService.GenerateMaintenanceWorkActPdf(userId, maintenanceId, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (PdfController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (PdfController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать акт выполненных работ." });
        }
    }

    /// <summary> Формирует PDF заказ-наряда на обслуживание. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/work-order")]
    public async Task<IActionResult> GenerateMaintenanceWorkOrderPdf(Guid maintenanceId, [FromQuery] Guid organizationId)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._pdfDocumentExportService.GenerateMaintenanceWorkOrderPdf(userId, maintenanceId, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (PdfController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (PdfController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать заказ-наряд." });
        }
    }

    /// <summary> Формирует PDF документа обслуживания по шаблону. </summary>
    [HttpGet("maintenance/{maintenanceId:guid}/scenario/{templateId:guid}")]
    public async Task<IActionResult> GenerateScenarioMaintenanceDocumentPdf(Guid maintenanceId, Guid templateId, [FromQuery] Guid organizationId)
    {
        var userId = this._userManager.GetUserId(this.User) ?? throw new Exception("Не найден текущий пользователь");

        try
        {
            var result = await this._pdfDocumentExportService.GenerateScenarioMaintenanceDocumentPdf(userId, maintenanceId, templateId, organizationId);

            return this.ToActionResult(result);
        }
        catch (Exception ex) when (PdfController.IsAccessDeniedError(ex))
        {
            return this.Forbid();
        }
        catch (Exception ex) when (PdfController.IsNotFoundError(ex))
        {
            return this.NotFound();
        }
        catch (Exception)
        {
            return this.StatusCode(500, new { message = "Не удалось сформировать сценарный PDF-документ." });
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
