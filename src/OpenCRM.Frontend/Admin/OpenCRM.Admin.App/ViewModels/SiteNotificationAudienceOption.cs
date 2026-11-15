using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.ViewModels;

public partial class SiteNotificationAudienceOption(string code, string name, bool isSelected = false) : ObservableObject
{
    public string Code { get; } = code;
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => this.SelectionChanged?.Invoke();

    public event Action? SelectionChanged;
}
