namespace OpenCRM.Services.Interfaces.Imports;

/// <summary> Предварительная сверка новых записей без дублей, исключённых и отменённых операций. </summary>
public sealed class ImportReconciliationDto
{
    public int NewClients { get; set; }

    public int LinkedClients { get; set; }

    public int NewVehicles { get; set; }

    public int ExistingVehicles { get; set; }

    public int NewOrders { get; set; }

    public int NewPayments { get; set; }

    public int NewHistoryEvents { get; set; }

    public decimal OrderTotal { get; set; }

    public decimal ActivePaymentsTotal { get; set; }

    public decimal OutstandingTotal { get; set; }
}
