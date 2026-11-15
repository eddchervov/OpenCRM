using OpenCRM.Domain;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Общие расчеты итогов и дат снимка обслуживания. </summary>
internal static class MaintenanceReadModelRules
{
    /// <summary> Нормализует дату обслуживания для модели чтения. </summary>
    internal static DateTime NormalizeMaintenanceDate(DateTime value) =>
        DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    /// <summary> Нормализует необязательную дату обслуживания для модели чтения. </summary>
    internal static DateTime? NormalizeMaintenanceDate(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return MaintenanceReadModelRules.NormalizeMaintenanceDate(value.Value);
    }

    /// <summary> Вычисляет итоговые суммы, задолженность и статус оплаты. </summary>
    internal static (decimal GrossAmount, decimal DebtAmount, decimal ProfitAmount, string PaymentStatus) BuildFinanceSummary(MaintenanceEntity maintenance)
    {
        var grossAmount = Math.Round(maintenance.Details.Sum(x => (decimal)(x.Price ?? 0)), 2, MidpointRounding.AwayFromZero);
        var netAmount = MaintenanceReadModelRules.GetNetAmount(maintenance);
        var debtAmount = maintenance.Status == MaintenanceStatus.Cancelled
            ? 0m
            : Math.Max(0, netAmount - maintenance.PaidAmount);
        var profitAmount = Math.Round(netAmount - maintenance.MaterialCostAmount - maintenance.LaborCostAmount, 2, MidpointRounding.AwayFromZero);
        var paymentStatus = MaintenancePaymentStatus.Resolve(
            maintenance.PaidAmount,
            netAmount,
            maintenance.Status == MaintenanceStatus.Cancelled || maintenance.Details.Any(detail => detail.Price.HasValue),
            maintenance.Status == MaintenanceStatus.Cancelled || maintenance.Details.All(detail => detail.Price.HasValue));

        return (grossAmount, debtAmount, profitAmount, paymentStatus);
    }

    /// <summary> Возвращает стоимость заказа после скидки с учётом отмены. </summary>
    internal static decimal GetNetAmount(MaintenanceEntity maintenance)
    {
        if (maintenance.Status == MaintenanceStatus.Cancelled)
        {
            return 0m;
        }

        var grossAmount = Math.Round(maintenance.Details.Sum(detail => (decimal)(detail.Price ?? 0)), 2, MidpointRounding.AwayFromZero);

        return Math.Max(0m, grossAmount - maintenance.DiscountAmount);
    }
}
