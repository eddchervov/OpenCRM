namespace OpenCRM.Domain;

/// <summary> Сотрудник организации. </summary>
public class OrganizationEmployee
{
    /// <summary> Идентификатор сотрудника. </summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary> Идентификатор организации. </summary>
    public Guid OrganizationId { get; set; }

    /// <summary> Организация, в которой числится сотрудник. </summary>
    public Organization Organization { get; set; } = null!;

    /// <summary> Идентификатор связанного участника организации. </summary>
    public Guid? MemberId { get; set; }

    /// <summary> Связанный участник организации. </summary>
    public OrganizationMember? Member { get; set; }

    /// <summary> Имя сотрудника. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Телефон сотрудника. </summary>
    public string? Phone { get; set; }

    /// <summary> Признак активности сотрудника. </summary>
    public bool IsActive { get; set; } = true;

    /// <summary> Версия записи сотрудника для управления параллельными изменениями. </summary>
    public uint Version { get; set; }
}
