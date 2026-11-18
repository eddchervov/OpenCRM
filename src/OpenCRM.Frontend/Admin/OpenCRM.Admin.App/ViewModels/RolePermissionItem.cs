using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Разрешение для редактирования в административной роли. </summary>
public partial class RolePermissionItem(AdminPermission permission, bool isSelected) : ObservableObject
{
    public string Code { get; } = permission.Code;

    public string Name => PermissionKey(this.Code) is { } key ? AdminLocalization.Instance[key] : permission.Name;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    public void RefreshLocalizedValues() => this.OnPropertyChanged(nameof(this.Name));

    private static string? PermissionKey(string code) => code switch
    {
        "Admin.Access" => "native.roles.permission.access",
        "Admin.Dashboard.View" => "native.roles.permission.dashboardView",
        "Admin.Users.View" => "native.roles.permission.usersView",
        "Admin.Users.Manage" => "native.roles.permission.usersManage",
        "Admin.Users.Impersonate" => "native.roles.permission.usersImpersonate",
        "Admin.Billing.View" => "native.roles.permission.billingView",
        "Admin.Billing.Manage" => "native.roles.permission.billingManage",
        "Admin.Content.View" => "native.roles.permission.contentView",
        "Admin.Content.Manage" => "native.roles.permission.contentManage",
        "Admin.Communications.View" => "native.roles.permission.communicationsView",
        "Admin.Communications.Manage" => "native.roles.permission.communicationsManage",
        "Admin.Configuration.Manage" => "native.roles.permission.configurationManage",
        _ => null
    };
}
