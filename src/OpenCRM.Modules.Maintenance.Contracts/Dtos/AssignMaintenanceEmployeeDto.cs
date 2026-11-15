namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Данные для назначения исполнителя на обслуживание. </summary>
public class AssignMaintenanceEmployeeDto
{
    /// <summary> Идентификатор сотрудника, если назначается. </summary>
    public Guid? EmployeeId { get; set; }

    /// <summary> Версия записи для контроля конкурентности. </summary>
    public uint Version { get; set; }
}
