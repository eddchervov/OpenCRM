using System.Globalization;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports;

/// <summary> Сверяет план переноса до подтверждения, не читая рабочие финансовые данные. </summary>
public static class ImportReconciliationBuilder
{
    public static ImportReconciliationDto Build(IReadOnlyList<ImportCandidate> rows)
    {
        var included = rows.Where(row => !row.Excluded).ToList();
        var created = included.Where(row => row.Status == "create").ToList();
        var completed = created.Where(row => row.Kind == "order" && row.OrderStatus?.Trim().ToLowerInvariant() == "completed").ToList();
        var payments = created.Where(row => row.Kind == "payment" && string.IsNullOrWhiteSpace(row.CancelledAt))
            .ToLookup(row => Key(row.OrderExternalId));
        var summary = new ImportReconciliationDto
        {
            NewClients = created.Count(row => row.Kind == "client"),
            LinkedClients = included.Count(row => row.Kind == "client" && row.Status == "link"),
            NewVehicles = created.Count(row => row.Kind == "vehicle"),
            ExistingVehicles = included.Count(row => row.Kind == "vehicle" && row.Status == "duplicate"),
            NewOrders = created.Count(row => row.Kind == "order"),
            NewPayments = created.Count(row => row.Kind == "payment"),
            NewHistoryEvents = created.Count(row => row.Kind == "history")
        };
        foreach (var order in completed)
        {
            var total = Money(order.Amount);
            var paid = payments[Key(order.ExternalId)].Sum(payment => Money(payment.Amount));
            summary.OrderTotal += total;
            summary.ActivePaymentsTotal += paid;
            summary.OutstandingTotal += Math.Max(0, total - paid);
        }

        return summary;
    }

    private static string Key(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private static decimal Money(string? value) => decimal.TryParse(value, NumberStyles.AllowDecimalPoint,
        CultureInfo.InvariantCulture, out var amount) && amount is >= 0 and <= 9_999_999_999_999_999.99m ? amount : 0;
}
