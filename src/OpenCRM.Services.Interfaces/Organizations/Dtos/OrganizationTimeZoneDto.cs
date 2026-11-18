namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Часовой пояс организации и доступ текущего пользователя к его изменению. </summary>
public sealed class OrganizationTimeZoneDto
{
    /// <summary> Идентификатор часового пояса IANA либо null, если он ещё не выбран. </summary>
    public string? TimeZoneId { get; init; }

    /// <summary> Может ли пользователь изменить часовой пояс организации. </summary>
    public bool CanManage { get; init; }

    /// <summary> Можно ли изменить часовой пояс до появления расписаний с заданным временем. </summary>
    public bool CanChange { get; init; } = true;
}
