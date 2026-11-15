namespace OpenCRM.Modules.Catalog.Contracts;

/// <summary>Инициализация справочников для внутреннего процесса после проверки доступа вызывающим модулем.</summary>
public interface ICatalogInitializationService
{
    /// <summary> Инициализирует справочники организации значениями по умолчанию. </summary>
    Task EnsureDefaultsAsync(Guid organizationId);
}
