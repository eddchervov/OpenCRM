using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Вариант типа бонуса промокода. </summary>
public sealed class PromoCodeOption(string code, string name, string? localizationKey = null) : ObservableObject
{
    public string Code { get; } = code;

    public string Name => localizationKey == null ? name : AdminLocalization.Instance[localizationKey];

    public static PromoCodeOption Localized(string code, string key) => new(code, string.Empty, key);

    public void RefreshLocalizedValues() => this.OnPropertyChanged(nameof(this.Name));

    public override string ToString() => this.Name;
}
