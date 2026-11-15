using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;

namespace OpenCRM.Modules.Maintenance;

/// <summary> Определяет покупателя и плательщика по обслуживанию. </summary>
internal sealed class MaintenanceBuyerResolver(
    OpenCRMContext context)
{
    /// <summary> Устанавливает покупателя записи обслуживания. </summary>
    internal async Task SetBuyerAsync(MaintenanceEntity maintenance, Guid organizationId, Guid? clientId)
    {
        if (!clientId.HasValue)
        {
            maintenance.BuyerClientId = null;
            maintenance.BuyerName = null;
            maintenance.BuyerPartyTypeCode = null;
            maintenance.BuyerInn = null;
            maintenance.BuyerKpp = null;
            maintenance.BuyerLegalAddress = null;

            return;
        }

        var client = await context.OrganizationClients.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == clientId.Value && item.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new InvalidOperationException("Покупатель не найден в этой организации.");

        maintenance.BuyerClientId = client.Id;
        maintenance.BuyerName = client.Name;
        maintenance.BuyerPartyTypeCode = client.PartyTypeCode;
        maintenance.BuyerInn = client.Inn;
        maintenance.BuyerKpp = client.Kpp;
        maintenance.BuyerLegalAddress = client.LegalAddress;
    }

    /// <summary> Устанавливает плательщика оплаты по обслуживанию. </summary>
    internal async Task SetPayerAsync(MaintenancePayment payment, MaintenanceEntity maintenance, Guid organizationId, Guid? clientId)
    {
        if (!clientId.HasValue || maintenance.BuyerClientId == clientId.Value)
        {
            payment.PayerClientId = maintenance.BuyerClientId;
            payment.PayerName = maintenance.BuyerClientId.HasValue ? maintenance.BuyerName : null;
            payment.PayerPartyTypeCode = maintenance.BuyerClientId.HasValue ? maintenance.BuyerPartyTypeCode : null;
            payment.PayerInn = maintenance.BuyerClientId.HasValue ? maintenance.BuyerInn : null;
            payment.PayerKpp = maintenance.BuyerClientId.HasValue ? maintenance.BuyerKpp : null;
            payment.PayerLegalAddress = maintenance.BuyerClientId.HasValue ? maintenance.BuyerLegalAddress : null;

            return;
        }

        var client = await context.OrganizationClients.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == clientId.Value && item.OrganizationId == organizationId && !item.IsDeleted)
            ?? throw new InvalidOperationException("Плательщик не найден в этой организации.");

        payment.PayerClientId = client.Id;
        payment.PayerName = client.Name;
        payment.PayerPartyTypeCode = client.PartyTypeCode;
        payment.PayerInn = client.Inn;
        payment.PayerKpp = client.Kpp;
        payment.PayerLegalAddress = client.LegalAddress;
    }
}
