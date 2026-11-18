using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Группа разрешений для отображения. </summary>
public sealed class RolePermissionGroup(string name, IReadOnlyList<RolePermissionItem> permissions) : ObservableObject
{
    public string Name => GroupKey(name) is { } key ? AdminLocalization.Instance[key] : name;

    public IReadOnlyList<RolePermissionItem> Permissions { get; } = permissions;

    public void RefreshLocalizedValues()
    {
        this.OnPropertyChanged(nameof(this.Name));
        foreach (var permission in this.Permissions)
        {
            permission.RefreshLocalizedValues();
        }
    }

    private static string? GroupKey(string group) => group switch
    {
        "Общее" => "native.roles.permissionGroup.common",
        "Дашборд" => "native.roles.permissionGroup.dashboard",
        "Пользователи" => "native.roles.permissionGroup.users",
        "Платежи" => "native.roles.permissionGroup.billing",
        "Контент" => "native.roles.permissionGroup.content",
        "Коммуникации" => "native.roles.permissionGroup.communications",
        "Настройки" => "native.roles.permissionGroup.configuration",
        _ => null
    };
}
