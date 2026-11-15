namespace OpenCRM.Modules.Maintenance.Contracts.Imports;

/// <summary>Проверяет и пакетно применяет исторические данные заказов организации.</summary>
public interface IImportBusinessRecordProcessor
{
    /// <summary>Проверяет ссылки, даты, статусы и финансовые итоги до применения пакета.</summary>
    Task ValidateAsync(
        string userId,
        Guid organizationId,
        IReadOnlyList<ImportBusinessRecord> records,
        IReadOnlyDictionary<string, Guid> vehicleIds,
        IReadOnlyDictionary<string, Guid> clientIds,
        IReadOnlyDictionary<string, Guid> orderIds,
        CancellationToken cancellationToken);

    /// <summary>Создаёт исторические агрегаты заказов, не выполняя доменные побочные эффекты.</summary>
    Task<ImportBusinessRecordResult> ApplyAsync(
        string userId,
        Guid organizationId,
        IReadOnlyList<ImportBusinessRecord> records,
        IReadOnlyDictionary<string, Guid> vehicleIds,
        IReadOnlyDictionary<string, Guid> clientIds,
        IDictionary<string, Guid> orderIds,
        CancellationToken cancellationToken);
}
