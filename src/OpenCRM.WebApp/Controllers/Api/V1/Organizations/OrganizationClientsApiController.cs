using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер управления клиентами организации. </summary>
[ApiController]
[Authorize]
public class OrganizationClientsApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationClientQueryService organizationClientQueries,
    IOrganizationClientDetailsQueryService organizationClientDetails,
    IOrganizationClientMaintenancePageQueryService clientMaintenancePages,
    IOrganizationClientCommandService organizationClientCommands,
    IOrganizationSubscriptionService organizationSubscriptionService,
    ILogger<OrganizationClientsApiController> logger) : ControllerBase
{
    /// <summary> Сообщение об неожиданной ошибке при загрузке клиентов. </summary>
    private const string UnexpectedLoadErrorMessage = "Не удалось загрузить клиентов. Попробуйте позже или обратитесь в поддержку.";

    /// <summary> Сообщение об неожиданной ошибке при создании клиента. </summary>
    private const string UnexpectedCreateErrorMessage = "Не удалось сохранить клиента. Попробуйте позже или обратитесь в поддержку.";

    /// <summary> Запрос на создание или обновление клиента организации. </summary>
    public sealed class SaveOrganizationClientRequest
    {
        /// <summary> Наименование клиента. </summary>
        public string? Name { get; set; }

        /// <summary> Фамилия физического лица. </summary>
        public string? LastName { get; set; }

        /// <summary> Имя физического лица. </summary>
        public string? FirstName { get; set; }

        /// <summary> Отчество физического лица. </summary>
        public string? MiddleName { get; set; }

        /// <summary> Вид организации: legal_entity или entrepreneur. </summary>
        public string? CompanyTypeCode { get; set; }

        /// <summary> Телефон клиента. </summary>
        public string? Phone { get; set; }

        /// <summary> Код типа контрагента клиента. </summary>
        public string? PartyTypeCode { get; set; }

        /// <summary> ИНН клиента. </summary>
        public string? Inn { get; set; }

        /// <summary> КПП клиента. </summary>
        public string? Kpp { get; set; }

        /// <summary> Юридический адрес клиента. </summary>
        public string? LegalAddress { get; set; }

        /// <summary> Комментарий к клиенту. </summary>
        public string? Comment { get; set; }

        /// <summary> Код статуса клиента. </summary>
        public string? StatusCode { get; set; }

        /// <summary> Идентификатор ответственного пользователя. </summary>
        public string? ResponsibleUserId { get; set; }

        /// <summary> Прежние заметки, объединяемые с комментарием для совместимости старых клиентов. </summary>
        public string? Notes { get; set; }

        /// <summary> Текстовый список имён тегов клиента. </summary>
        public string? TagNamesText { get; set; }
    }

    /// <summary> Возвращает постраничный список клиентов организации с фильтрами. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/clients/{page:int}/{pageSize:int}")]
    public async Task<IActionResult> Load(
        Guid organizationId,
        int page,
        int pageSize,
        string? searchQuery = null,
        string? sort = null,
        bool isArchived = false,
        string? statusCode = null,
        string? responsibleUserId = null,
        Guid? tagId = null,
        string? segmentCode = null,
        string? partyTypeCode = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await this.CanUseClientsModuleAsync(user.Id, organizationId))
        {
            return Forbid();
        }

        try
        {
            return Ok(await organizationClientQueries.GetPageAsync(user.Id, organizationId, searchQuery, sort, page, pageSize, isArchived, statusCode, responsibleUserId, tagId, segmentCode, partyTypeCode));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load clients page data for organization {OrganizationId} by user {UserId}", organizationId, user.Id);
            return BadRequest(new { message = UnexpectedLoadErrorMessage });
        }
    }

    /// <summary> Возвращает карточку клиента организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}")]
    public async Task<IActionResult> Details(Guid organizationId, Guid clientId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await this.CanUseClientsModuleAsync(user.Id, organizationId))
        {
            return Forbid();
        }

        try
        {
            var details = await organizationClientDetails.GetDetailsAsync(user.Id, organizationId, clientId);
            return details == null ? NotFound() : Ok(details);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load client details for organization {OrganizationId}, client {ClientId} by user {UserId}", organizationId, clientId, user.Id);
            return BadRequest(new { message = UnexpectedLoadErrorMessage });
        }
    }

    /// <summary> Возвращает постраничную историю работ клиента. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/maintenances")]
    public async Task<IActionResult> Maintenances(
        Guid organizationId,
        Guid clientId,
        int page = 1,
        int pageSize = 10,
        string? searchQuery = null,
        string? status = null)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await this.CanUseClientsModuleAsync(user.Id, organizationId))
        {
            return Forbid();
        }

        try
        {
            var result = await clientMaintenancePages.GetPageAsync(user.Id, organizationId, clientId, page, pageSize, searchQuery, status);

            return result == null ? NotFound() : Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Ошибка загрузки истории клиента {ClientId} организации {OrganizationId}", clientId, organizationId);

            return BadRequest(new { message = "Не удалось загрузить историю работ клиента. Попробуйте позже." });
        }
    }

    /// <summary> Создаёт клиента организации. </summary>
    [HttpPost("/api/v1/organizations/{organizationId:guid}/clients")]
    public async Task<IActionResult> Create(Guid organizationId, [FromBody] SaveOrganizationClientRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await this.CanUseClientsModuleAsync(user.Id, organizationId))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.FirstName))
        {
            return BadRequest(new { message = "Укажите имя или название клиента." });
        }

        try
        {
            var clientId = await organizationClientCommands.CreateAsync(user.Id, organizationId, new CreateOrganizationClientDto
            {
                Name = request.Name?.Trim() ?? string.Empty,
                LastName = request.LastName,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                CompanyTypeCode = request.CompanyTypeCode,
                Phone = NormalizeOptional(request.Phone),
                PartyTypeCode = request.PartyTypeCode,
                Inn = request.Inn,
                Kpp = request.Kpp,
                LegalAddress = request.LegalAddress,
                Comment = NormalizeOptional(request.Comment),
                StatusCode = request.StatusCode,
                ResponsibleUserId = NormalizeOptional(request.ResponsibleUserId),
                Notes = NormalizeOptional(request.Notes),
                TagNames = ParseTagNames(request.TagNamesText)
            });

            var details = await organizationClientDetails.GetDetailsAsync(user.Id, organizationId, clientId);
            return Ok(details);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to create client for organization {OrganizationId} by user {UserId}", organizationId, user.Id);
            return BadRequest(new { message = UnexpectedCreateErrorMessage });
        }
    }

    /// <summary> Обновляет данные клиента организации. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}")]
    public async Task<IActionResult> Update(Guid organizationId, Guid clientId, [FromBody] SaveOrganizationClientRequest request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await this.CanUseClientsModuleAsync(user.Id, organizationId))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.FirstName))
        {
            return BadRequest(new { message = "Укажите имя или название клиента." });
        }

        try
        {
            await organizationClientCommands.UpdateAsync(user.Id, organizationId, clientId, new UpdateOrganizationClientDto
            {
                Name = request.Name?.Trim() ?? string.Empty,
                LastName = request.LastName,
                FirstName = request.FirstName,
                MiddleName = request.MiddleName,
                CompanyTypeCode = request.CompanyTypeCode,
                Phone = NormalizeOptional(request.Phone),
                PartyTypeCode = request.PartyTypeCode,
                Inn = request.Inn,
                Kpp = request.Kpp,
                LegalAddress = request.LegalAddress,
                Comment = NormalizeOptional(request.Comment),
                StatusCode = request.StatusCode,
                ResponsibleUserId = NormalizeOptional(request.ResponsibleUserId),
                Notes = NormalizeOptional(request.Notes),
                TagNames = ParseTagNames(request.TagNamesText)
            });

            var details = await organizationClientDetails.GetDetailsAsync(user.Id, organizationId, clientId);
            return details == null ? NotFound() : Ok(details);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to update client {ClientId} for organization {OrganizationId} by user {UserId}", clientId, organizationId, user.Id);
            return BadRequest(new { message = UnexpectedCreateErrorMessage });
        }
    }

    /// <summary> Обновляет только комментарий клиента организации. </summary>
    [HttpPut("/api/v1/organizations/{organizationId:guid}/clients/{clientId:guid}/comment")]
    public async Task<IActionResult> UpdateComment(
        Guid organizationId,
        Guid clientId,
        [FromBody] UpdateOrganizationClientCommentDto request)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return NotFound();
        }

        if (!await this.CanUseClientsModuleAsync(user.Id, organizationId))
        {
            return Forbid();
        }

        try
        {
            await organizationClientCommands.UpdateCommentAsync(user.Id, organizationId, clientId, request);
            var details = await organizationClientDetails.GetDetailsAsync(user.Id, organizationId, clientId);

            return details == null ? NotFound() : Ok(details);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to update client comment {ClientId} for organization {OrganizationId} by user {UserId}", clientId, organizationId, user.Id);

            return BadRequest(new { message = UnexpectedCreateErrorMessage });
        }
    }

    /// <summary> Возвращает краткий список клиентов организации для выбора. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/clients/options")]
    public async Task<ActionResult<IReadOnlyList<OrganizationClientOptionDto>>> Options(Guid organizationId)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!await this.CanUseClientsModuleAsync(userId, organizationId))
        {
            return Forbid();
        }

        try
        {
            return Ok(await organizationClientQueries.GetOptionsAsync(userId, organizationId));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Unable to load client options for organization {OrganizationId} by user {UserId}", organizationId, userId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary> Ищет клиентов организации для выбора с постраничной загрузкой. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/clients/lookup")]
    public async Task<ActionResult<OrganizationClientLookupPageDto>> Lookup(
        Guid organizationId,
        string? searchQuery,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized();
        }

        if (!await this.CanUseClientsModuleAsync(userId, organizationId))
        {
            return Forbid();
        }

        try
        {
            return Ok(await organizationClientQueries.LookupAsync(userId, organizationId, searchQuery, page, cancellationToken));
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException)
        {
            return Forbid();
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Unable to search clients for organization {OrganizationId} by user {UserId}", organizationId, userId);

            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary> Преобразует отказ доступа к рабочему пространству в запрет клиентского API. </summary>
    private async Task<bool> CanUseClientsModuleAsync(string userId, Guid organizationId)
    {
        try
        {
            return await organizationSubscriptionService.HasFeatureAsync(userId, organizationId, FeatureCodes.ClientsModule);
        }
        catch (InvalidOperationException exception)
        {
            logger.LogDebug(exception, "Модуль клиентов организации {OrganizationId} недоступен пользователю {UserId}", organizationId, userId);

            return false;
        }
    }

    /// <summary> Нормализует необязательную строку, заменяя пустое значение на null. </summary>
    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary> Разбирает строку с именами тегов в список. </summary>
    private static IReadOnlyList<string> ParseTagNames(string? value)
    {
        return (value ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();
    }
}
