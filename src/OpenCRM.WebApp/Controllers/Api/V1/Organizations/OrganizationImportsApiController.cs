using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> Пользовательский мастер переноса данных выбранной организации. </summary>
[ApiController]
[Authorize]
[AutoValidateAntiforgeryToken]
[Route("api/v1/organizations/{organizationId:guid}/imports")]
public sealed class OrganizationImportsApiController(
    IOrganizationImportService imports,
    IOrganizationImportBatchService? batches = null) : ControllerBase
{
    private IOrganizationImportBatchService Batches => batches ?? imports as IOrganizationImportBatchService
        ?? throw new InvalidOperationException("Не зарегистрирована фоновая обработка импорта.");

    public sealed class SourceRequest
    {
        public int Revision { get; set; }

        public string? SourceKey { get; set; }
    }

    [HttpGet("profiles")]
    public Task<IActionResult> Profiles(Guid organizationId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.GetProfilesAsync(userId, organizationId, cancellationToken)));

    [HttpPost("profiles")]
    [RequestSizeLimit(1024 * 1024)]
    public Task<IActionResult> SaveProfile(Guid organizationId, ImportProfileDto request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.SaveProfileAsync(userId, organizationId, request, cancellationToken)));

    [HttpDelete("profiles/{id:guid}")]
    public Task<IActionResult> DeleteProfile(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId =>
        {
            await this.Batches.DeleteProfileAsync(userId, organizationId, id, cancellationToken);

            return NoContent();
        });

    [HttpPut("batches/{id:guid}/source")]
    public Task<IActionResult> Source(Guid organizationId, Guid id, SourceRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.SetSourceAsync(
            userId, organizationId, id, request.Revision, request.SourceKey, cancellationToken)));

    /// <summary> Сохраняемые соответствия исходных столбцов. </summary>
    public sealed class MappingRequest
    {
        /// <summary> Редактируемая версия черновика. </summary>
        public int Revision { get; set; }

        /// <summary> Настройки исходных таблиц. </summary>
        public List<ImportTableMapping> Mappings { get; set; } = [];
    }

    /// <summary> Изменённые пользователем строки. </summary>
    public sealed class SaveRequest
    {
        /// <summary> Редактируемая версия черновика. </summary>
        public int Revision { get; set; }

        /// <summary> Полный состав строк с ручными правками. </summary>
        public List<ImportCandidate> Rows { get; set; } = [];
    }

    /// <summary> Подтверждение просмотренной версии. </summary>
    public sealed class ConfirmRequest
    {
        /// <summary> Подтверждаемая версия черновика. </summary>
        public int Revision { get; set; }
    }

    /// <summary> Возвращает доступность действий для отображения в настройках. </summary>
    [HttpGet("access")]
    public Task<IActionResult> Access(Guid organizationId) => this.ExecuteAsync(async userId =>
        Ok(new { canImport = await imports.CanImportAsync(userId, organizationId) }));

    /// <summary> Возвращает последние черновики и завершённые загрузки организации. </summary>
    [HttpGet]
    public Task<IActionResult> List(Guid organizationId, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await imports.ListAsync(userId, organizationId, cancellationToken)));

    /// <summary> Открывает исходные данные и редактируемый результат. </summary>
    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await imports.GetAsync(userId, organizationId, id, cancellationToken)));

    /// <summary> Создаёт черновик из выгрузки CSV или XLSX. </summary>
    [HttpPost]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public Task<IActionResult> Create(Guid organizationId, IFormFile file, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId =>
        {
            if (file.Length == 0 || file.Length > 10 * 1024 * 1024)
            {
                return BadRequest(new { message = "Выберите непустой файл CSV или XLSX размером до 10 МБ." });
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);

            return Ok(await imports.CreateAsync(userId, organizationId, file.FileName, stream.ToArray(), cancellationToken));
        });

    /// <summary> Пересоздаёт редактируемые записи из исходных столбцов. </summary>
    [HttpPost("{id:guid}/mapping")]
    public Task<IActionResult> Map(Guid organizationId, Guid id, MappingRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await imports.MapAsync(
            userId, organizationId, id, request.Revision, request.Mappings, cancellationToken)));

    /// <summary> Сохраняет и проверяет правки перед подтверждением. </summary>
    [HttpPut("{id:guid}")]
    [RequestSizeLimit(30 * 1024 * 1024)]
    public Task<IActionResult> Save(Guid organizationId, Guid id, SaveRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await imports.SaveAsync(
            userId, organizationId, id, request.Revision, request.Rows, cancellationToken)));

    /// <summary> Выполняет перенос после повторной серверной проверки. </summary>
    [HttpPost("{id:guid}/confirm")]
    public Task<IActionResult> Confirm(Guid organizationId, Guid id, ConfirmRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await imports.ConfirmAsync(
            userId, organizationId, id, request.Revision, cancellationToken)));

    /// <summary> Выгружает результаты проверки или завершённого импорта. </summary>
    [HttpGet("{id:guid}/report")]
    public Task<IActionResult> Report(Guid organizationId, Guid id, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => File(
            await imports.GetReportAsync(userId, organizationId, id, cancellationToken),
            "text/csv; charset=utf-8", $"opencrm-import-{id}.csv"));

    /// <summary> Сохраняет файл для фонового разбора без ожидания подготовки в HTTP-запросе. </summary>
    [HttpPost("batches")]
    [RequestSizeLimit(51 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 51 * 1024 * 1024)]
    public Task<IActionResult> UploadBatch(Guid organizationId, IFormFile file, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId =>
        {
            if (file.Length is 0 or > 50 * 1024 * 1024)
            {
                return BadRequest(new { message = "Выберите непустой файл размером до 50 МБ." });
            }
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, cancellationToken);

            return Ok(await this.Batches.UploadAsync(userId, organizationId, file.FileName, stream.ToArray(), cancellationToken));
        });

    /// <summary> Возвращает одну страницу подготовленных строк и общую сводку. </summary>
    [HttpGet("batches/{id:guid}")]
    public Task<IActionResult> GetBatch(
        Guid organizationId, Guid id, CancellationToken cancellationToken,
        int page = 1, int pageSize = 50, string? kind = null, string? status = null, string? search = null) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.GetPageAsync(
            userId, organizationId, id, page, pageSize, kind, status, search, cancellationToken)));

    [HttpPost("batches/{id:guid}/mapping")]
    public Task<IActionResult> MapBatch(Guid organizationId, Guid id, MappingRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.QueueMappingAsync(
            userId, organizationId, id, request.Revision, request.Mappings, cancellationToken)));

    [HttpPut("batches/{id:guid}")]
    [RequestSizeLimit(5 * 1024 * 1024)]
    public Task<IActionResult> PatchBatch(Guid organizationId, Guid id, SaveRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.PatchRowsAsync(
            userId, organizationId, id, request.Revision, request.Rows, cancellationToken)));

    [HttpPost("batches/{id:guid}/confirm")]
    public Task<IActionResult> StartBatch(Guid organizationId, Guid id, ConfirmRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.StartAsync(
            userId, organizationId, id, request.Revision, false, cancellationToken)));

    [HttpPost("batches/{id:guid}/resume")]
    public Task<IActionResult> ResumeBatch(Guid organizationId, Guid id, ConfirmRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.StartAsync(
            userId, organizationId, id, request.Revision, true, cancellationToken)));

    [HttpPost("batches/{id:guid}/pause")]
    public Task<IActionResult> PauseBatch(Guid organizationId, Guid id, ConfirmRequest request, CancellationToken cancellationToken) =>
        this.ExecuteAsync(async userId => Ok(await this.Batches.PauseAsync(
            userId, organizationId, id, request.Revision, cancellationToken)));

    private async Task<IActionResult> ExecuteAsync(Func<string, Task<IActionResult>> action)
    {
        var userId = this.GetApiUserId();
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        try
        {
            return await action(userId);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Черновик не найден в выбранной организации." });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = "Черновик изменён в другой вкладке. Откройте его заново." });
        }
        catch (InvalidDataException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
