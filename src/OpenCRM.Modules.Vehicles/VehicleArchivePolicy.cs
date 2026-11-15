namespace OpenCRM.Modules.Vehicles;

/// <summary> Правила хранения и очистки архива техники. </summary>
internal static class VehicleArchivePolicy
{
    /// <summary> Срок хранения техники в архиве в днях. </summary>
    internal const int RetentionDays = 30;

    /// <summary> Проверяет, истёк ли срок хранения записи архива. </summary>
    internal static bool IsExpired(DateTime archiveDate, DateTime now) => (now - archiveDate).TotalDays >= VehicleArchivePolicy.RetentionDays;

    /// <summary> Возвращает число дней до истечения срока хранения. </summary>
    internal static int? GetDaysLeft(DateTime? archiveDate, DateTime now) => archiveDate.HasValue
        ? Math.Max(0, VehicleArchivePolicy.RetentionDays - (int)(now - archiveDate.Value).TotalDays)
        : null;
}
