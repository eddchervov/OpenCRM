namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Вариант выбора ответственного из участников организации. </summary>
public sealed class OrganizationSchedulerMemberOptionDto
{
    /// <summary> Идентификатор пользователя. </summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary> Отображаемое имя пользователя. </summary>
    public string DisplayName { get; init; } = string.Empty;
}
