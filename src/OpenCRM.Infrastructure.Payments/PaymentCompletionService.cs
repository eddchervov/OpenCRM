using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Payments;

/// <summary> Сервис формирования состояния завершения платежа. </summary>
public class PaymentCompletionService(
    OpenCRMContext opencrmContext,
    IYooKassaService yooKassaService) : IPaymentCompletionService
{
    /// <summary> Префикс типа операции покупки рабочего пространства. </summary>
    private const string WorkspacePurchasePrefix = "yookassa_workspace_purchase:";

    /// <summary> Префикс типа операции продления рабочего пространства. </summary>
    private const string WorkspaceExtensionPrefix = "yookassa_workspace_extension:";

    /// <summary> Возвращает состояние завершения платежа для пользователя. </summary>
    public async Task<PaymentCompleteDto> GetCompleteStateAsync(string? userId)
    {
        var model = new PaymentCompleteDto
        {
            StatusCode = "returned"
        };

        if (string.IsNullOrWhiteSpace(userId))
        {
            return model;
        }

        var latestPayment = await opencrmContext.SubscriptionPayments
            .AsNoTracking()
            .Where(x => x.UserId == userId &&
                x.ExternalPaymentId != null &&
                x.PaymentProviderCode != "payments-service")
            .OrderByDescending(x => x.Id)
            .FirstOrDefaultAsync();

        if (latestPayment == null)
        {
            return model;
        }

        if (TryParseWorkspacePayment(latestPayment.PaymentMethod, out var organizationId))
        {
            var scenarioCode = await opencrmContext.Organizations
                .AsNoTracking()
                .Where(organization => organization.Id == organizationId)
                .Select(organization => organization.PrimaryScenarioCode)
                .FirstOrDefaultAsync();

            model.PrimaryActionUrl = scenarioCode == null
                ? "/organizations"
                : OrganizationScenarioProcessCatalog.NormalizeScenarioCode(scenarioCode) switch
                {
                    OrganizationTypeCodes.Detailing => $"/detailing/{organizationId:D}#billing",
                    OrganizationTypeCodes.ServiceStation => $"/autoservice/{organizationId:D}#billing",
                    OrganizationTypeCodes.Fleet => $"/fleet/{organizationId:D}#billing",
                    _ => "/organizations"
                };
        }

        var localStatus = latestPayment.PaymentStatus?.ToLowerInvariant();
        if ((localStatus == "pending" || localStatus == "waiting_for_capture") &&
            !string.IsNullOrWhiteSpace(latestPayment.ExternalPaymentId))
        {
            var remoteStatus = await yooKassaService.GetPaymentStatusAsync(latestPayment.ExternalPaymentId);
            if (remoteStatus.IsSuccess)
            {
                if (remoteStatus.Status == "canceled")
                {
                    localStatus = "canceled";
                    await yooKassaService.UpdateLocalPaymentStatusAsync(latestPayment.ExternalPaymentId, "canceled");
                }
                else if (remoteStatus.Paid || remoteStatus.Status == "succeeded")
                {
                    localStatus = "paid_waiting_confirmation";
                }
            }
        }

        ApplyStatus(model, localStatus);
        return model;
    }

    /// <summary> Применяет локальный статус платежа к модели. </summary>
    private static void ApplyStatus(PaymentCompleteDto model, string? localStatus)
    {
        switch (localStatus)
        {
            case "completed":
                model.IsConfirmed = true;
                model.StatusCode = "confirmed";
                break;

            case "paid_waiting_confirmation":
            case "pending":
            case "waiting_for_capture":
                model.StatusCode = "pending";
                break;

            case "canceled":
            case "failed":
                model.IsCanceled = true;
                model.StatusCode = "canceled";
                break;
        }
    }

    /// <summary> Разбирает тип операции рабочего пространства и извлекает идентификатор организации. </summary>
    private static bool TryParseWorkspacePayment(string? paymentMethod, out Guid organizationId)
    {
        organizationId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(paymentMethod))
        {
            return false;
        }

        if (paymentMethod.StartsWith(WorkspacePurchasePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Guid.TryParse(paymentMethod[WorkspacePurchasePrefix.Length..], out organizationId);
        }

        if (paymentMethod.StartsWith(WorkspaceExtensionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Guid.TryParse(paymentMethod[WorkspaceExtensionPrefix.Length..], out organizationId);
        }

        return false;
    }
}
