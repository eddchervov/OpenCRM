using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;
using Microsoft.AspNetCore.Http;
using OpenCRM.Services.Interfaces.Common;
using OpenCRM.Services.Interfaces.FileStorages;

namespace OpenCRM.WebApp.Controllers.Api.V1.Maintenance;

/// <summary> API работы с документами обслуживания. </summary>
[ApiController]
[Authorize]
public class MaintenanceDocumentApiController(
    IFileStorageService fileStorageService,
    IMaintenanceDocumentService maintenanceDocumentService) : MaintenanceApiControllerBase
{
    /// <summary> Максимальный размер загружаемого подписанного документа в байтах. </summary>
    private const long MaxSignedDocumentSizeBytes = 25 * 1024 * 1024;

    /// <summary> Множество допустимых расширений подписанных документов. </summary>
    private static readonly HashSet<string> AllowedSignedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf",
        ".doc",
        ".docx",
        ".jpg",
        ".jpeg",
        ".png"
    };

    /// <summary> Возвращает список документов, относящихся к записи об обслуживании. </summary>
    [HttpGet("api/v1/maintenance/{maintenanceId:guid}/documents")]
    public async Task<ActionResult<IReadOnlyList<MaintenanceDocumentDto>>> GetDocuments(Guid maintenanceId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            return Ok(await maintenanceDocumentService.GetDocuments(userId, organizationId, maintenanceId));
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Загружает подписанный документ для записи об обслуживании. </summary>
    [HttpPost("api/v1/maintenance/{maintenanceId:guid}/documents/upload")]
    [RequestSizeLimit(MaxSignedDocumentSizeBytes)]
    public async Task<ActionResult<MaintenanceDocumentDto>> UploadSignedDocument(Guid maintenanceId, [FromForm] IFormFile? formFile, [FromQuery] Guid? organizationId = null)
    {
        var validationError = ValidateSignedDocument(formFile);
        if (validationError != null) return validationError;

        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var originalFileName = SanitizeDownloadFileName(formFile!.FileName);
            var extension = Path.GetExtension(originalFileName);
            var storagePath = BuildSignedDocumentStoragePath(organizationId, maintenanceId, extension);

            await using var stream = formFile.OpenReadStream();
            var savedPath = await fileStorageService.SaveFileAsync(storagePath, stream, new Dictionary<string, string>
            {
                ["document-kind"] = "signed_document",
                ["maintenance-id"] = maintenanceId.ToString("D")
            });

            var document = await maintenanceDocumentService.AddDocument(userId, organizationId, maintenanceId, new CreateMaintenanceDocumentDto
            {
                Kind = "signed_document",
                Format = NormalizeDocumentFormat(extension),
                IncludePhotos = false,
                Title = Path.GetFileNameWithoutExtension(originalFileName),
                FileName = originalFileName,
                StoragePath = savedPath,
                ContentType = string.IsNullOrWhiteSpace(formFile.ContentType)
                    ? "application/octet-stream"
                    : formFile.ContentType,
                Size = formFile.Length
            });

            await maintenanceDocumentService.TrackDocumentGenerated(
                userId,
                organizationId,
                maintenanceId,
                "Загружен подписанный документ",
                $"Файл: {originalFileName}");

            return Ok(document);
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex))
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary> Скачивает файл документа об обслуживании по его идентификатору. </summary>
    [HttpGet("api/v1/maintenance/documents/{documentId:guid}/download")]
    public async Task<IActionResult> DownloadDocument(Guid documentId, [FromQuery] Guid? organizationId = null)
    {
        var userIdResult = GetCurrentUserId();
        if (userIdResult.Result != null) return userIdResult.Result;
        var userId = userIdResult.Value!;

        try
        {
            var document = await maintenanceDocumentService.GetDocumentForDownload(userId, organizationId, documentId);
            var stream = await fileStorageService.GetFileAsync(document.StoragePath);
            return File(stream, document.ContentType, document.FileName);
        }
        catch (Exception ex) when (IsAccessDeniedError(ex))
        {
            return Forbid();
        }
        catch (Exception ex) when (IsNotFoundError(ex) || ex is FileNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary> Проверяет корректность загружаемого файла подписанного документа. </summary>
    private ActionResult? ValidateSignedDocument(IFormFile? file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "Файл не выбран." });
        }

        if (file.Length > MaxSignedDocumentSizeBytes)
        {
            return BadRequest(new { message = "Файл слишком большой. Максимальный размер: 25 МБ." });
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedSignedDocumentExtensions.Contains(extension))
        {
            return BadRequest(new { message = "Можно загрузить PDF, Word или изображение." });
        }

        return null;
    }

    /// <summary> Формирует путь хранения подписанного документа в файловом хранилище. </summary>
    private static string BuildSignedDocumentStoragePath(Guid? organizationId, Guid maintenanceId, string extension)
    {
        var workspacePart = organizationId?.ToString("N") ?? "personal";
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? ".bin" : extension.ToLowerInvariant();
        return $"maintenance-signed-documents/{workspacePart}/{maintenanceId:N}/{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{safeExtension}";
    }

    /// <summary> Очищает имя файла для безопасной выдачи при скачивании. </summary>
    private static string SanitizeDownloadFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(Path.GetFileName(fileName)
            .Select(ch => invalidChars.Contains(ch) ? '_' : ch)
            .ToArray());

        return string.IsNullOrWhiteSpace(sanitized) ? "signed-document.pdf" : sanitized;
    }

    /// <summary> Приводит расширение документа к нормализованному формату. </summary>
    private static string NormalizeDocumentFormat(string extension)
    {
        return extension.TrimStart('.').ToUpperInvariant();
    }
}
