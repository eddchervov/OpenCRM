using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenCRM.Admin.App.Localization;

/// <summary> Вариант системного фильтра с неизменным кодом и переводимой подписью. </summary>
public sealed class AdminLocalizedOption(string code, string titleKey, string legacyTitle = "") : ObservableObject
{
    public string Code { get; } = code;

    public string TitleKey { get; } = titleKey;

    public string LegacyTitle { get; } = legacyTitle;

    public string Title => AdminLocalization.Instance[this.TitleKey];

    public void RefreshTitle() => this.OnPropertyChanged(nameof(this.Title));
}
