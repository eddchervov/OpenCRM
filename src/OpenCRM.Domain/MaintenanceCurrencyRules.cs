namespace OpenCRM.Domain;

/// <summary> Правила денежного учёта записи обслуживания. </summary>
public static class MaintenanceCurrencyRules
{
    /// <summary> Запрещает финансовые изменения записи в валюте, отличной от текущей валюты организации. </summary>
    public static void EnsureMatchesOrganizationCurrency(Maintenance maintenance, string organizationCurrencyCode)
    {
        if (!string.Equals(maintenance.CurrencyCode, organizationCurrencyCode, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Валюта записи отличается от валюты организации. Финансовые изменения недоступны; конвертация не выполняется.");
        }
    }
}
