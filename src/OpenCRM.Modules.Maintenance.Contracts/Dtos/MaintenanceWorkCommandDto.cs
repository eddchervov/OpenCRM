namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Команда выполнения работы по обслуживанию. </summary>
public sealed class MaintenanceWorkCommandDto
{
    /// <summary> Идентификатор команды. </summary>
    public Guid CommandId { get; set; }

    /// <summary> Версия записи для контроля конкурентности. </summary>
    public uint Version { get; set; }

    /// <summary> Действие над работой. </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary> Причина действия. </summary>
    public string? Reason { get; set; }
}
