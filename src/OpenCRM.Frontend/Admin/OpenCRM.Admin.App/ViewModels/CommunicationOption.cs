using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Значение фильтра с понятной пользователю подписью. </summary>
public sealed class CommunicationOption(string code, string name, string? localizationKey = null) : ObservableObject
{
    private readonly string _name = name;

    public string Code { get; } = code;

    public string Name => localizationKey is null ? this._name : AdminLocalization.Instance[localizationKey];

    public static CommunicationOption Localized(string code, string key) => new(code, string.Empty, key);

    public void RefreshLocalizedValues() => this.OnPropertyChanged(nameof(this.Name));

    public override string ToString() => this.Name;

    public override bool Equals(object? obj) => obj is CommunicationOption other
        && string.Equals(this.Code, other.Code, StringComparison.Ordinal);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Code);
}
