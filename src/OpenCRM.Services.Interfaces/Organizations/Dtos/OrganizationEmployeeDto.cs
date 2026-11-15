namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Сотрудник организации. </summary>
public class OrganizationEmployeeDto
{
    /// <summary> Идентификатор сотрудника. </summary>
    public Guid Id { get; set; }

    /// <summary> Имя сотрудника. </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary> Телефон сотрудника. </summary>
    public string? Phone { get; set; }

    /// <summary> Идентификатор участника организации, связанного с сотрудником. </summary>
    public Guid? MemberId { get; set; }

    /// <summary> Участник для привязки при сохранении данных сотрудника. </summary>
    public Guid? MemberIdToLink { get; set; }

    /// <summary> Признак активности сотрудника. </summary>
    public bool IsActive { get; set; } = true;

    /// <summary> Версия записи сотрудника. </summary>
    public uint Version { get; set; }
}
