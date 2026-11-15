using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Разрешение для редактирования в административной роли. </summary>
public partial class RolePermissionItem(AdminPermission permission, bool isSelected) : ObservableObject
{
    public string Code { get; } = permission.Code;

    public string Name { get; } = permission.Name;

    [ObservableProperty]
    private bool _isSelected = isSelected;
}
