using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Роль в списке экрана управления правами. </summary>
public sealed partial class RoleOption(string id, string name, int usersCount, bool isProtected, bool isLocked, int permissionCount) : ObservableObject
{
    public string Id { get; } = id;

    public string OriginalName { get; } = name;

    public string Name => this.IsProtected && string.Equals(this.OriginalName, "System", StringComparison.OrdinalIgnoreCase)
        ? AdminLocalization.Instance["native.roles.systemRoleName"]
        : this.OriginalName;

    public int UsersCount { get; } = usersCount;

    public bool IsProtected { get; } = isProtected;

    public bool IsLocked { get; } = isLocked;

    [ObservableProperty]
    private int _permissionCount = permissionCount;

    public void RefreshLocalizedValues() => this.OnPropertyChanged(nameof(this.Name));
}
