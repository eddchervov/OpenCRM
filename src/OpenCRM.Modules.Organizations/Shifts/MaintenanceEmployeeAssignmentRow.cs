namespace OpenCRM.Modules.Organizations.Shifts;

/// <summary> Строка назначения сотрудника на работу по обслуживанию. </summary>
internal sealed class MaintenanceEmployeeAssignmentRow
{
    /// <summary> Идентификатор обслуживания. </summary>
    public Guid MaintenanceId { get; set; }

    /// <summary> Идентификатор строки обслуживания. </summary>
    public Guid DetailId { get; set; }

    /// <summary> Идентификатор сотрудника. </summary>
    public Guid EmployeeId { get; set; }
}
