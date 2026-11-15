namespace OpenCRM.Admin.Api.Models;

/// <summary> Организация для административной панели. </summary>
public sealed class AdminDashboardOrganization
{
    /// <summary> Идентификатор организации. </summary>
    public Guid Id { get; init; }

    /// <summary> Название организации. </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary> Код основного сценария организации. </summary>
    public string PrimaryScenarioCode { get; init; } = string.Empty;

    /// <summary> Момент создания организации. </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary> Владелец организации. </summary>
    public string Owner { get; init; } = string.Empty;

    /// <summary> Количество участников. </summary>
    public int MembersCount { get; init; }
}
