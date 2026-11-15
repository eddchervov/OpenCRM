using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Imports;
using OpenCRM.WebApp.Controllers.Api.V1;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API импорта данных из CRM под управлением администратора. </summary>
[ApiController]
[Route("api/v1/admin/crm-import")]
[Authorize(Policy = AdminAuthorizationPolicies.ManagePermissions)]
public sealed class CrmImportApiController(ICrmImportService crmImportService) : ControllerBase
{
    /// <summary> Ищет пользователей по строке поиска. </summary>
    [HttpGet("users")]
    public async Task<IActionResult> SearchUsers(string? search, CancellationToken cancellationToken)
    {
        return Ok(await crmImportService.SearchUsersAsync(search, cancellationToken));
    }

    /// <summary> Возвращает организации пользователя. </summary>
    [HttpGet("users/{userId}/organizations")]
    public async Task<IActionResult> GetOrganizations(string userId, CancellationToken cancellationToken)
    {
        return Ok(await crmImportService.GetOrganizationsAsync(userId, cancellationToken));
    }

    /// <summary> Создаёт предпросмотр импорта из загруженного файла. </summary>
    [HttpPost("previews")]
    [RequestSizeLimit(10 * 1024 * 1024 + 1024)]
    public async Task<IActionResult> CreatePreview(
        [FromForm] string userId,
        [FromForm] Guid organizationId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0 || file.Length > 10 * 1024 * 1024)
        {
            return BadRequest(new { message = "Выберите файл размером не более 10 МБ." });
        }

        try
        {
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);
            var preview = await crmImportService.CreatePreviewAsync(
                userId, organizationId, file.FileName, stream.ToArray(), cancellationToken);

            return Ok(preview);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or
            System.Xml.XmlException or Microsoft.VisualBasic.FileIO.MalformedLineException or
            DecoderFallbackException or ArgumentException)
        {
            return BadRequest(new { message = "Файл повреждён или его структура не поддерживается." });
        }
    }

    /// <summary> Возвращает предпросмотр импорта по идентификатору. </summary>
    [HttpGet("previews/{id:guid}")]
    public async Task<IActionResult> GetPreview(Guid id, CancellationToken cancellationToken)
    {
        var preview = await crmImportService.GetPreviewAsync(id, cancellationToken);

        return preview == null ? NotFound() : Ok(preview);
    }

    /// <summary> Возвращает отчёт по предпросмотру импорта. </summary>
    [HttpGet("previews/{id:guid}/report")]
    public async Task<IActionResult> GetPreviewReport(Guid id, CancellationToken cancellationToken)
    {
        var report = await crmImportService.GetReportAsync(id, false, cancellationToken);

        return report == null ? NotFound() : File(report, "text/csv; charset=utf-8", $"crm-import-preview-{id}.csv");
    }

    /// <summary> Подтверждает выполнение импорта по предпросмотру. </summary>
    [HttpPost("previews/{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await crmImportService.ConfirmAsync(id, actorUserId, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Предпросмотр не найден." });
        }
        catch (InvalidDataException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException exception) when (
            exception.InnerException is Npgsql.PostgresException databaseException &&
            databaseException.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            return Conflict(new { message = "Эти данные уже импортированы в организацию." });
        }
    }

    /// <summary> Возвращает отчёт о результатах импорта. </summary>
    [HttpGet("previews/{id:guid}/result-report")]
    public async Task<IActionResult> GetResultReport(Guid id, CancellationToken cancellationToken)
    {
        var report = await crmImportService.GetReportAsync(id, true, cancellationToken);

        return report == null ? NotFound() : File(report, "text/csv; charset=utf-8", $"crm-import-result-{id}.csv");
    }
}
