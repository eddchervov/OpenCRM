using OpenCRM.WebApp.Infrastructure.Security;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Modules.Billing.Contracts.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.WebApp.Security;
using OpenCRM.Services.Interfaces.Payments;

namespace OpenCRM.WebApp.Controllers.Admin.Api;

/// <summary> API администрирования платежей, подписок и чеков НПД. </summary>
[Route("api/v1/admin/billing")]
[ApiController]
[Authorize(Policy = AdminAuthorizationPolicies.ViewBilling)]
[CommercialModule]
public sealed class BillingApiController(
    IAdminBillingQueryService billingQuery,
    IAdminNpdReceiptService receiptService,
    IAdminPaymentCommandService paymentCommands,
    IAdminSubscriptionCommandService subscriptionCommands,
    IAuthTokenService authTokenService,
    INpdReceiptDeliveryService npdReceiptDeliveryService) : ControllerBase
{
    /// <summary> Возвращает общие данные биллинга для администратора. </summary>
    [HttpGet]
    public async Task<AdminBillingDto> Get()
    {
        return await billingQuery.GetAsync();
    }

    /// <summary> Возвращает страницу платежей с учётом параметров запроса. </summary>
    [HttpGet("payments")]
    public async Task<AdminBillingPaymentsPageDto> GetPayments([FromQuery] AdminBillingPageQuery query)
    {
        return await billingQuery.GetPaymentsPageAsync(query);
    }

    /// <summary> Возвращает страницу подписок организаций. </summary>
    [HttpGet("subscriptions/organizations")]
    public async Task<AdminBillingOrganizationSubscriptionsPageDto> GetOrganizationSubscriptions(
        [FromQuery] AdminBillingPageQuery query)
    {
        return await billingQuery.GetOrganizationSubscriptionsPageAsync(query);
    }

    /// <summary> Возвращает платеж по его идентификатору. </summary>
    [HttpGet("payments/{paymentId:int}")]
    public async Task<ActionResult<AdminBillingPaymentDto>> GetPayment(int paymentId)
    {
        var payment = await billingQuery.GetPaymentAsync(paymentId);

        return payment == null ? NotFound() : Ok(payment);
    }

    /// <summary> Возвращает доступные действия и журнал административных операций платёжного сервиса. </summary>
    [HttpGet("payments/{paymentId:int}/service-actions")]
    public async Task<IActionResult> GetServiceActions(int paymentId, [FromServices] IAdminPaymentServiceActions paymentServiceActions, CancellationToken cancellationToken)
    {
        var state = await paymentServiceActions.GetAsync(paymentId, cancellationToken);

        return state == null ? NotFound(new { message = "Покупка платёжного сервиса не найдена." }) : Ok(state);
    }

    /// <summary> Принимает идемпотентное действие администратора с платёжным сервисом. </summary>
    [HttpPost("payments/{paymentId:int}/service-actions")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> ExecuteServiceAction(int paymentId, [FromBody] AdminPaymentServiceActionRequest request,
        [FromServices] IAdminPaymentServiceActions paymentServiceActions, CancellationToken cancellationToken)
    {
        var actor = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(actor))
        {
            return Unauthorized();
        }

        var result = await paymentServiceActions.ExecuteAsync(paymentId, request, actor, cancellationToken);

        return result.Operation != null
            ? StatusCode(result.StatusCode, result.Operation)
            : StatusCode(result.StatusCode, new { message = result.Message, code = result.Code ?? (result.StatusCode == 409 ? "state_changed" : "invalid_request") });
    }

    /// <summary> Возвращает данные, требующие внимания администратора, за период. </summary>
    [HttpGet("attention")]
    public async Task<AdminBillingAttentionDto> GetAttention([FromQuery] int? periodDays = null)
    {
        return await billingQuery.GetAttentionAsync(periodDays);
    }

    /// <summary> Возвращает список покупок. </summary>
    [HttpGet("purchases")]
    public async Task<IReadOnlyList<AdminBillingPurchaseDto>> GetPurchases()
    {
        return await billingQuery.GetPurchasesAsync();
    }

    /// <summary> Отмечает наличие чека НПД у платежа. </summary>
    [HttpPut("payments/{paymentId:int}/npd-receipt")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> MarkNpdReceipt(int paymentId, [FromBody] AdminMarkNpdReceiptRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await receiptService.MarkNpdReceiptAsync(
            paymentId, request.Issued, request.ExpectedIssued, request.Reason, adminUserId);
        return MapError(result.Succeeded, result.NotFound, result.ErrorMessage) ?? Ok(new { result.Message });
    }

    /// <summary> Обновляет детали чека НПД для платежа. </summary>
    [HttpPut("payments/{paymentId:int}/npd-receipt/details")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> UpdateNpdReceiptDetails(int paymentId, [FromBody] AdminNpdReceiptDetailsRequest request)
    {
        var result = await receiptService.UpdateNpdReceiptDetailsAsync(paymentId, request.ReceiptUrl, request.Comment);
        return MapError(result.Succeeded, result.NotFound, result.ErrorMessage) ?? Ok(new { result.Message });
    }

    /// <summary> Загружает файл чека НПД для платежа. </summary>
    [HttpPost("payments/{paymentId:int}/npd-receipt/file")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    [RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> UploadNpdReceiptFile(int paymentId, IFormFile file)
    {
        if (file == null || file.Length is 0 or > 5_242_880)
        {
            return BadRequest(new { message = "Выберите файл PDF, PNG или JPG размером до 5 МБ." });
        }

        await using var content = file.OpenReadStream();
        var result = await receiptService.UploadNpdReceiptFileAsync(
            paymentId, content, file.FileName, file.ContentType);
        return MapError(result.Succeeded, result.NotFound, result.ErrorMessage) ?? Ok(new { result.Message });
    }

    /// <summary> Возвращает файл чека НПД для платежа. </summary>
    [HttpGet("payments/{paymentId:int}/npd-receipt/file")]
    public async Task<IActionResult> GetNpdReceiptFile(int paymentId)
    {
        var file = await receiptService.GetNpdReceiptFileAsync(paymentId);
        return file == null ? NotFound() : File(file.Content, file.ContentType, file.FileName);
    }

    /// <summary> Удаляет файл чека НПД у платежа. </summary>
    [HttpDelete("payments/{paymentId:int}/npd-receipt/file")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> DeleteNpdReceiptFile(int paymentId)
    {
        var result = await receiptService.DeleteNpdReceiptFileAsync(paymentId);
        return MapError(result.Succeeded, result.NotFound, result.ErrorMessage) ?? Ok(new { result.Message });
    }

    /// <summary> Отправляет чек НПД по платежу. </summary>
    [HttpPost("payments/{paymentId:int}/npd-receipt/send")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> SendNpdReceipt(int paymentId)
    {
        var result = await npdReceiptDeliveryService.QueueAsync(paymentId);

        return MapError(result.Succeeded, result.NotFound, result.ErrorMessage) ?? Ok(new { result.Message });
    }

    /// <summary> Возвращает список доступных тарифных планов. </summary>
    [HttpGet("available-plans")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IReadOnlyList<AdminBillingAvailablePlanDto>> GetAvailablePlans()
    {
        return await billingQuery.GetAvailablePlansAsync();
    }

    /// <summary> Возвращает историю изменений статуса платежа. </summary>
    [HttpGet("payments/{paymentId:int}/status-audits")]
    public async Task<IReadOnlyList<AdminPaymentStatusAuditDto>> GetPaymentStatusAudits(int paymentId)
    {
        return await billingQuery.GetPaymentStatusAuditsAsync(paymentId);
    }

    /// <summary> Возвращает историю изменений подписки. </summary>
    [HttpGet("subscriptions/{subscriptionScope}/{subscriptionId:int}/audits")]
    public async Task<IReadOnlyList<AdminSubscriptionAuditDto>> GetSubscriptionAudits(string subscriptionScope, int subscriptionId)
    {
        return await billingQuery.GetSubscriptionAuditsAsync(subscriptionScope, subscriptionId);
    }

    /// <summary> Синхронизирует статус платежа с YooKassa. </summary>
    [HttpPost("payments/{paymentId:int}/sync-yookassa")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> SyncPaymentWithYooKassa(int paymentId)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized();
        }

        var result = await paymentCommands.SyncPaymentWithYooKassaAsync(paymentId, currentUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);
        if (error != null)
        {
            return error;
        }

        return Ok(new
        {
            result.Message,
            result.Status,
            result.StatusName,
            result.StatusGroup,
            result.RemoteStatus,
            result.Paid,
            result.Amount
        });
    }

    /// <summary> Принудительно устанавливает статус платежа. </summary>
    [HttpPost("payments/{paymentId:int}/mark-status")]
    [Authorize(Roles = AdminRoleNames.SystemOnly)]
    public async Task<IActionResult> MarkPaymentStatus(
        int paymentId,
        [FromBody] AdminMarkPaymentStatusRequest request)
    {
        var currentUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(currentUserId))
        {
            return Unauthorized();
        }

        var result = await paymentCommands.MarkPaymentStatusAsync(
            paymentId,
            request.Status,
            request.ExpectedCurrentStatus,
            request.Reason,
            currentUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);
        if (error != null)
        {
            return error;
        }

        return Ok(new
        {
            result.Message,
            result.Status,
            result.StatusName,
            result.StatusGroup
        });
    }

    /// <summary> Останавливает подписку по идентификатору и области. </summary>
    [HttpPost("subscriptions/{subscriptionScope}/{subscriptionId:int}/stop")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> StopSubscription(
        string subscriptionScope,
        int subscriptionId,
        [FromBody] AdminSubscriptionActionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.StopSubscriptionAsync(
            subscriptionScope,
            subscriptionId,
            request.Reason,
            request.ExpectedEndDate,
            adminUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);
        if (error != null)
        {
            return error;
        }

        return Ok(new { result.Message });
    }

    /// <summary> Выдаёт организации демонстрационный доступ. </summary>
    [HttpPost("users/{userId}/organizations/{organizationId:guid}/demo")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> GrantOrganizationDemo(
        string userId,
        Guid organizationId,
        [FromBody] AdminSubscriptionActionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.GrantOrganizationDemoAsync(userId, organizationId, adminUserId, request.Reason);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);
        if (error != null)
        {
            return error;
        }

        return Ok(new { result.Message });
    }

    /// <summary> Выдаёт пользователю демонстрационный доступ. </summary>
    [HttpPost("users/{userId}/demo")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> GrantUserDemo(string userId, [FromBody] AdminSubscriptionActionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.GrantUserDemoAsync(userId, request.Reason, adminUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);

        return error ?? Ok(new { result.Message, result.EndDate });
    }

    /// <summary> Назначает пользователю тарифный план. </summary>
    [HttpPost("users/{userId}/plan")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> GrantUserPlan(string userId, [FromBody] AdminGrantSubscriptionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.GrantUserPlanAsync(
            userId, request.PlanId, request.Days, request.Reason, adminUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);

        return error ?? Ok(new { result.Message, result.EndDate });
    }

    /// <summary> Назначает организации тарифный план. </summary>
    [HttpPost("organizations/{organizationId:guid}/plan")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> GrantOrganizationPlan(
        Guid organizationId,
        [FromBody] AdminGrantSubscriptionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.GrantOrganizationPlanAsync(
            organizationId, request.PlanId, request.Days, request.Reason, adminUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);

        return error ?? Ok(new { result.Message, result.EndDate });
    }

    /// <summary> Отзывает демонстрационный доступ пользователя. </summary>
    [HttpDelete("users/{userId}/demo")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> RevokeUserDemo(string userId, [FromBody] AdminSubscriptionActionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.RevokeUserDemoAsync(userId, request.Reason, adminUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);

        return error ?? Ok(new { result.Message });
    }

    /// <summary> Отзывает демонстрационный доступ организации. </summary>
    [HttpDelete("users/{userId}/organizations/{organizationId:guid}/demo")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> RevokeOrganizationDemo(
        string userId,
        Guid organizationId,
        [FromBody] AdminSubscriptionActionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.RevokeOrganizationDemoAsync(userId, organizationId, adminUserId, request.Reason);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);
        if (error != null)
        {
            return error;
        }

        return Ok(new { result.Message });
    }

    /// <summary> Продлевает подписку на указанный срок. </summary>
    [HttpPost("subscriptions/{subscriptionScope}/{subscriptionId:int}/extend")]
    [Authorize(Policy = AdminAuthorizationPolicies.ManageBilling)]
    public async Task<IActionResult> ExtendSubscription(
        string subscriptionScope,
        int subscriptionId,
        [FromBody] AdminExtendSubscriptionRequest request)
    {
        var adminUserId = await GetCurrentUserIdAsync();
        if (string.IsNullOrWhiteSpace(adminUserId))
        {
            return Unauthorized();
        }

        var result = await subscriptionCommands.ExtendSubscriptionAsync(
            subscriptionScope,
            subscriptionId,
            request.Days,
            request.Reason,
            request.ExpectedEndDate,
            adminUserId);
        var error = MapError(result.Succeeded, result.NotFound, result.ErrorMessage);
        if (error != null)
        {
            return error;
        }

        return Ok(new
        {
            result.Message,
            result.EndDate
        });
    }

    /// <summary> Преобразует результат операции в HTTP-ответ с ошибкой. </summary>
    private IActionResult? MapError(bool succeeded, bool notFound, string? errorMessage)
    {
        if (succeeded)
        {
            return null;
        }

        if (notFound)
        {
            return NotFound(new { message = errorMessage });
        }

        return BadRequest(new { message = errorMessage });
    }

    /// <summary> Возвращает идентификатор текущего пользователя. </summary>
    private async Task<string?> GetCurrentUserIdAsync()
    {
        var currentUser = await authTokenService.GetCurrentUserAsync(User);

        return currentUser?.Id;
    }
}
