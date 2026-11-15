namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Правила расчёта и нормализации денежных полей обслуживания. </summary>
internal static class MaintenanceMoneyRules
{
    /// <summary> Округляет денежное значение до допустимой точности. </summary>
    internal static decimal NormalizeMoney(decimal? value)
        => Math.Round(Math.Max(0, value ?? 0), 2, MidpointRounding.AwayFromZero);

    /// <summary> Устанавливает финансовые поля обслуживания со значениями по умолчанию. </summary>
    internal static void ApplyFinancialFields(MaintenanceEntity maintenance, decimal? discountAmount, decimal? paidAmount, decimal? laborCostAmount)
    {
        maintenance.DiscountAmount = MaintenanceMoneyRules.NormalizeMoney(discountAmount);
        maintenance.PaidAmount = MaintenanceMoneyRules.NormalizeMoney(paidAmount);
        maintenance.LaborCostAmount = MaintenanceMoneyRules.NormalizeMoney(laborCostAmount);
    }

    /// <summary> Вычисляет итоговые суммы, задолженность и статус оплаты. </summary>
    internal static (decimal GrossAmount, decimal DebtAmount, decimal ProfitAmount, string PaymentStatus) BuildFinanceSummary(MaintenanceEntity maintenance)
    {
        return MaintenanceReadModelRules.BuildFinanceSummary(maintenance);
    }
}
