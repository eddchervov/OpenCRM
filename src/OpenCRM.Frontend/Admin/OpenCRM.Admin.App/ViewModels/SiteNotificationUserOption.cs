using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public partial class SiteNotificationUserOption(AdminSiteNotificationUser user) : ObservableObject
{
    public string Id { get; } = user.Id;
    public string Email { get; } = user.Email;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => this.SelectionChanged?.Invoke(this);

    public event Action<SiteNotificationUserOption>? SelectionChanged;
}
