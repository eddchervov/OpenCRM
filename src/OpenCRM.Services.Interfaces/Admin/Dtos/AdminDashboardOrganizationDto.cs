namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> DTO организации для дашборда. </summary>
public sealed class AdminDashboardOrganizationDto
{
    /// <summary> Идентификатор организации. </summary>
    public Guid Id { get; init; }

    /// <summary> Название организации. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Код основного сценария. </summary>
    public string PrimaryScenarioCode { get; init; } = string.Empty;

    /// <summary> Время создания организации. </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary> Имя владельца. </summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary> Количество участников. </summary>
    public int MembersCount { get; init; }
}

