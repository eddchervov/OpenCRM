namespace OpenCRM.Domain;

/// <summary> Коды статуса оплаты обслуживания. </summary>
public static class MaintenancePaymentStatus
{
    public const string NoPaymentRequired = "no_payment_required";

    /// <summary> Определяет статус по суммам и стоимости строк обслуживания. </summary>
    public static string Resolve(
        decimal paidAmount,
        decimal netAmount,
        bool hasPricedDetail,
        bool areAllDetailsPriced)
    {
        if (paidAmount == 0 && netAmount == 0 && hasPricedDetail && areAllDetailsPriced)
        {
            return MaintenancePaymentStatus.NoPaymentRequired;
        }

        if (paidAmount <= 0)
        {
            return "unpaid";
        }

        if (paidAmount < netAmount)
        {
            return "partial";
        }

        return paidAmount == netAmount ? "paid" : "overpaid";
    }
}
