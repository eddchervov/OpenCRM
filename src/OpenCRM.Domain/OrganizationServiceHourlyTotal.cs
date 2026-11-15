namespace OpenCRM.Domain;

/// <summary>
/// Хранит количество и сумму положительных строк услуги за час UTC для быстрого рейтинга.
/// C# обновляет эту восстанавливаемую сводку в транзакции обслуживания; источником остаются строки работ.
/// Полное исходное название сохраняется для прежних правил группировки и выбора отображаемого имени.
/// </summary>
public sealed class OrganizationServiceHourlyTotal
{
    public Guid OrganizationId { get; set; }

    public int UtcHour { get; set; }

    /// <summary> SHA-256 исходного имени ограничивает размер ключа; равенство проверяется по полному имени. </summary>
    public string NameHash { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public long Count { get; set; }

    public decimal Amount { get; set; }

    /// <summary> Минимальный ключ исходной строки сохраняет порядок до объединения имён в .NET. </summary>
    public string FirstKey { get; set; } = string.Empty;
}
