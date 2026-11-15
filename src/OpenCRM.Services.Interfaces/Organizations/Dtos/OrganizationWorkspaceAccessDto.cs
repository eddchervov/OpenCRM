namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Данные о доступе пользователя к рабочему пространству организации. </summary>
public class OrganizationWorkspaceAccessDto
{
    /// <summary> Признак включённого коммерческого режима установки. </summary>
    public bool CommercialEnabled { get; set; }

    /// <summary> Признак бесплатного тарифа с базовыми действиями владельца. </summary>
    public bool IsFreePlan { get; set; }

    /// <summary> Признак основного рабочего пространства. </summary>
    public bool IsBaseWorkspace { get; set; }

    /// <summary> Признак активной подписки. </summary>
    public bool HasActiveSubscription { get; set; }

    /// <summary> Признак доступа только для чтения. </summary>
    public bool IsReadOnly { get; set; }

    /// <summary> Признак доступности экспорта данных. </summary>
    public bool ExportsEnabled { get; set; }

    /// <summary> Признак права редактирования техники. </summary>
    public bool CanEditVehicles { get; set; }

    /// <summary> Признак права архивации техники и восстановления из архива. </summary>
    public bool CanArchiveVehicles { get; set; }

    /// <summary> Код роли пользователя в организации. </summary>
    public string RoleCode { get; set; } = string.Empty;

    /// <summary> Сообщение о режиме только для чтения, если есть. </summary>
    public string? ReadOnlyMessage { get; set; }
}
