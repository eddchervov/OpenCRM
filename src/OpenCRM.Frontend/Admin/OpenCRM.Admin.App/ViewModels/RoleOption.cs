using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Роль в списке экрана управления правами. </summary>
public sealed partial class RoleOption(string id, string name, int usersCount, bool isProtected, bool isLocked, int permissionCount) : ObservableObject
{
    public string Id { get; } = id;

    public string Name { get; } = name;

    public int UsersCount { get; } = usersCount;

    public bool IsProtected { get; } = isProtected;

    public bool IsLocked { get; } = isLocked;

    [ObservableProperty]
    private int _permissionCount = permissionCount;
}
