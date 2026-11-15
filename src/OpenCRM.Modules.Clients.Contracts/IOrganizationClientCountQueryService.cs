namespace OpenCRM.Modules.Clients.Contracts;

/// <summary>Сводное чтение клиентов для рабочего пространства после проверки доступа вызывающим сценарием.</summary>
public interface IOrganizationClientCountQueryService
{
    /// <summary> Возвращает количество активных клиентов организации. </summary>
    Task<int> GetActiveCountAsync(Guid organizationId);
}
