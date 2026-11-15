namespace OpenCRM.Domain;

/// <summary>
/// Коды типов складских движений материалов организации.
/// </summary>
public static class OrganizationMaterialMovementTypeCodes
{
    /// <summary>Ручной приход материала на склад.</summary>
    public const string Receipt = "receipt";

    /// <summary>Ручное списание материала.</summary>
    public const string Issue = "issue";

    /// <summary>Корректировка фактического остатка.</summary>
    public const string Adjustment = "adjustment";

    /// <summary>Автоматическое списание материалов из обслуживания.</summary>
    public const string MaintenanceIssue = "maintenance_issue";

    /// <summary>Резервирование материала под будущую работу или заказ.</summary>
    public const string Reserve = "reserve";

    /// <summary>Снятие резерва материала.</summary>
    public const string ReleaseReserve = "release_reserve";

    /// <summary>Перемещение материала между складами или адресами хранения.</summary>
    public const string Transfer = "transfer";

    /// <summary>
    /// Нормализует внешний код типа движения к поддерживаемому набору.
    /// </summary>
    public static string Normalize(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            Receipt => Receipt,
            Issue => Issue,
            Adjustment => Adjustment,
            MaintenanceIssue => MaintenanceIssue,
            Reserve => Reserve,
            ReleaseReserve => ReleaseReserve,
            Transfer => Transfer,
            _ => Receipt
        };
    }

    /// <summary>
    /// Нормализует код фильтра, но оставляет пустое значение пустым.
    /// </summary>
    public static string? NormalizeNullable(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized switch
        {
            Receipt => Receipt,
            Issue => Issue,
            Adjustment => Adjustment,
            MaintenanceIssue => MaintenanceIssue,
            Reserve => Reserve,
            ReleaseReserve => ReleaseReserve,
            Transfer => Transfer,
            _ => null
        };
    }
}
