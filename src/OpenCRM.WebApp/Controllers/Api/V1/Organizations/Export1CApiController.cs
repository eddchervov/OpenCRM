using System.Text.Json;
using System.Text.Unicode;
using Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Services.Interfaces.Export1C;
using OpenCRM.Services.Interfaces.Export1C.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.WebApp.Controllers.Api.V1.Organizations;

/// <summary> API-контроллер выгрузки данных организации в 1С. </summary>
[ApiController]
[Authorize]
public sealed class Export1CApiController(
    UserManager<ApplicationUser> userManager,
    IOrganizationSubscriptionService organizationSubscriptionService,
    IOrganizationPermissionService organizationPermissionService,
    IExport1CService export1CService,
    ILogger<Export1CApiController> logger) : ControllerBase
{
    /// <summary> Возвращает доступные возможности выгрузки в 1С для организации. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/accounting-export/capabilities")]
    public async Task<IActionResult> Capabilities(Guid organizationId)
    {
        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            var access = await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organizationId);
            var hasFinance = await organizationSubscriptionService.HasFeatureAsync(
                user.Id,
                organizationId,
                FeatureCodes.FinanceModule);
            var canExport = access.ExportsEnabled && hasFinance
                && (await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.FinanceView)
                    || await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.FinanceManage))
                && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.DocumentsExport);

            return Ok(new
            {
                CanExport = canExport,
                CanExportBp3 = canExport && await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.FinanceExport1C),
                CanExportOrders = canExport && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MaintenanceView),
                CanExportClients = canExport && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.ClientsView),
                CanExportVehicles = canExport && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.VehiclesView),
                CanExportCatalog = canExport && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.WorkspaceCatalogView),
                CanExportMaterials = canExport && await organizationPermissionService.HasPermissionAsync(user.Id, organizationId, OrganizationPermissionCodes.MaterialsView)
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException)
        {
            return Forbid();
        }
    }

    /// <summary> Возвращает предпросмотр данных выгрузки в 1С. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/accounting-export/preview")]
    public async Task<IActionResult> Preview(Guid organizationId, [FromQuery] Export1COptionsDto options)
    {
        if (options.DestinationCode is not ("bp3" or "full"))
        {
            return this.BadRequest(new { message = "Укажите корректное назначение выгрузки." });
        }

        if (options.DateFrom == default || options.DateTo == default
            || options.DateTo == DateOnly.MaxValue || options.DateTo < options.DateFrom
            || !TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out _))
        {
            return BadRequest(new { message = "Укажите корректный период и часовой пояс." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            var access = await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organizationId);
            if (!access.ExportsEnabled || !await this.CanExportAsync(user.Id, organizationId, options))
            {
                return Forbid();
            }

            if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.FinanceModule))
            {
                return NotFound();
            }

            var package = await export1CService.CreateAsync(organizationId, options);
            var completedOrders = package.Orders
                .Where(order => !order.IsPaymentContextOnly && order.Status == "Completed")
                .ToArray();
            var orderIdsWithIssues = package.ValidationIssues
                .Where(issue => issue.EntityType is "order" or "order_line")
                .Select(issue => issue.OrderId)
                .ToHashSet();
            var paymentIdsWithIssues = package.ValidationIssues
                .Where(issue => issue.EntityType == "payment")
                .Select(issue => issue.EntityId)
                .ToHashSet();

            return Ok(new
            {
                package.SchemaVersion,
                OrdersCount = package.Orders.Count(order => !order.IsPaymentContextOnly),
                CompletedOrdersCount = completedOrders.Length,
                OrdersNeedingReviewCount = completedOrders.Count(order => orderIdsWithIssues.Contains(order.Id)),
                PaymentsCount = package.Payments.Count,
                PaymentsNeedingReviewCount = package.Payments.Count(payment => paymentIdsWithIssues.Contains(payment.Id)),
                package.ValidationIssues
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException)
        {
            return Forbid();
        }
    }

    /// <summary> Формирует и отдаёт файл выгрузки данных в 1С. </summary>
    [HttpGet("/api/v1/organizations/{organizationId:guid}/accounting-export")]
    public async Task<IActionResult> Download(Guid organizationId, [FromQuery] Export1COptionsDto options)
    {
        if (options.DestinationCode is not ("bp3" or "full"))
        {
            return this.BadRequest(new { message = "Укажите корректное назначение выгрузки." });
        }

        if (options.DateFrom == default || options.DateTo == default
            || options.DateTo == DateOnly.MaxValue || options.DateTo < options.DateFrom)
        {
            return BadRequest(new { message = "Укажите корректный период выгрузки." });
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(options.TimeZoneId, out _))
        {
            return BadRequest(new { message = "Неизвестный часовой пояс выгрузки." });
        }

        var user = await userManager.GetUserAsync(User);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            var access = await organizationSubscriptionService.GetWorkspaceAccessAsync(user.Id, organizationId);
            if (!access.ExportsEnabled)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "Выгрузка недоступна в режиме чтения." });
            }

            if (!await this.CanExportAsync(user.Id, organizationId, options))
            {
                return Forbid();
            }

            if (!await organizationSubscriptionService.HasFeatureAsync(user.Id, organizationId, FeatureCodes.FinanceModule))
            {
                return NotFound();
            }

            var package = await export1CService.CreateAsync(organizationId, options);
            var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(UnicodeRanges.All)
            };
            var content = JsonSerializer.SerializeToUtf8Bytes(package, jsonOptions);
            var fileName = $"opencrm-accounting-{organizationId:N}-{options.DateFrom:yyyyMMdd}-{options.DateTo:yyyyMMdd}.json";

            Response.Headers["X-Accounting-Issues"] = package.ValidationIssues.Count.ToString();

            return File(content, "application/json; charset=utf-8", fileName);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException)
        {
            return Forbid();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Не удалось сформировать бухгалтерскую выгрузку. OrganizationId: {OrganizationId}, UserId: {UserId}, TraceId: {TraceId}",
                organizationId,
                user.Id,
                HttpContext.TraceIdentifier);

            return StatusCode(StatusCodes.Status500InternalServerError, new { message = "Не удалось сформировать выгрузку." });
        }
    }

    /// <summary> Проверяет, разрешена ли пользователю выгрузка в 1С. </summary>
    private async Task<bool> CanExportAsync(string userId, Guid organizationId, Export1COptionsDto options)
    {
        if (options.DestinationCode == "bp3"
            && !await organizationSubscriptionService.HasFeatureAsync(userId, organizationId, FeatureCodes.FinanceExport1C))
        {
            return false;
        }

        var canViewFinance = await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.FinanceView)
            || await organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.FinanceManage);
        if (!canViewFinance)
        {
            return false;
        }

        var requiredPermissions = new List<string> { OrganizationPermissionCodes.DocumentsExport };

        if (options.IncludeOrders || options.IncludePayments)
        {
            requiredPermissions.Add(OrganizationPermissionCodes.MaintenanceView);
        }

        if (options.IncludeClients)
        {
            requiredPermissions.Add(OrganizationPermissionCodes.ClientsView);
        }

        if (options.IncludeVehicles)
        {
            requiredPermissions.Add(OrganizationPermissionCodes.VehiclesView);
        }

        if (options.IncludeCatalog)
        {
            requiredPermissions.Add(OrganizationPermissionCodes.WorkspaceCatalogView);
        }

        if (options.IncludeMaterials)
        {
            requiredPermissions.Add(OrganizationPermissionCodes.MaterialsView);
        }

        foreach (var permission in requiredPermissions)
        {
            if (!await organizationPermissionService.HasPermissionAsync(userId, organizationId, permission))
            {
                return false;
            }
        }

        return true;
    }
}
