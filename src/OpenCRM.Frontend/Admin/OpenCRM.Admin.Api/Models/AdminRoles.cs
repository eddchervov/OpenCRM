namespace OpenCRM.Admin.Api.Models;

/// <summary> Список ролей административной панели. </summary>
public sealed class AdminRoles
{
    /// <summary> Роли, доступные для управления. </summary>
    public IReadOnlyList<AdminRole> Roles { get; init; } = [];
}
