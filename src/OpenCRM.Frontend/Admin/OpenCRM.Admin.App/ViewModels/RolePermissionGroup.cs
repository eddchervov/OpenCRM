namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Группа разрешений для отображения. </summary>
public sealed class RolePermissionGroup(string name, IReadOnlyList<RolePermissionItem> permissions)
{
    public string Name { get; } = name;

    public IReadOnlyList<RolePermissionItem> Permissions { get; } = permissions;
}
