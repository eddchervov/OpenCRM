using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.ViewModels;

public partial class SiteNotificationAudienceOption(string code, string name, bool isSelected = false) : ObservableObject
{
    private readonly string _name = name;

    public string Code { get; } = code;
    public string Name => this.LocalizationKey is null ? this._name : AdminLocalization.Instance[this.LocalizationKey];

    public static SiteNotificationAudienceOption Localized(string code, string key, bool isSelected = false) =>
        new(code, string.Empty, isSelected) { LocalizationKey = key };

    private string? LocalizationKey { get; init; }

    public void RefreshLocalizedValues()
    {
        this.OnPropertyChanged(nameof(this.Name));
    }

    public override bool Equals(object? obj) => obj is SiteNotificationAudienceOption other
        && string.Equals(this.Code, other.Code, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Code);

    [ObservableProperty]
    private bool _isSelected = isSelected;

    partial void OnIsSelectedChanged(bool value) => this.SelectionChanged?.Invoke();

    public event Action? SelectionChanged;
}
