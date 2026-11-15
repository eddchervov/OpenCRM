namespace OpenCRM.Services.Interfaces.Organizations.Dtos;

/// <summary> Результат обзора рабочего пространства. </summary>
public sealed class WorkspaceOverviewResultDto
{
    /// <summary> Данные обзора, если доступны. </summary>
    public OrganizationWorkspaceOverviewDto? Overview { get; init; }
}
