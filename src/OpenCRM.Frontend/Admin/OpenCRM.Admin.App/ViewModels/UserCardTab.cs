using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Вкладка карточки пользователя. </summary>
public sealed partial class UserCardTab(
    string id,
    string titleKey,
    IReadOnlyList<UserCardItem> items,
    string? emptyMessageKey,
    IReadOnlyList<string>? limitNoteKeys = null,
    bool showItemCount = true) : ObservableObject
{
    public string Id { get; } = id;

    public string TitleKey { get; } = titleKey;

    /// <summary> Заголовок вкладки. </summary>
    public string Title => this.ShowItemCount
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture,
            AdminLocalization.Instance["userCard.tab.itemCount"], this.DisplayTitle, this.Items.Count)
        : this.DisplayTitle;

    /// <summary> Заголовок вкладки с количеством элементов. </summary>
    public string DisplayTitle => AdminLocalization.Instance[this.TitleKey];

    public bool ShowItemCount { get; } = showItemCount;

    /// <summary> Элементы вкладки. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    [NotifyPropertyChangedFor(nameof(Title))]
    private IReadOnlyList<UserCardItem> _items = items;

    /// <summary> Признак вкладки поддержки. </summary>
    public bool IsSupport => this.Id == "support";

    /// <summary> Признак вкладки со списком записей. </summary>
    public bool IsRecordList => !this.IsOverview && !this.IsSupport && !this.IsManagement;

    /// <summary> Признак вкладки административных действий. </summary>
    public bool IsManagement => this.Id == "management";

    /// <summary> Признак вкладки обзора. </summary>
    public bool IsOverview => this.Id == "overview";

    /// <summary> Признак вкладки подписок. </summary>
    public bool IsSubscriptions => this.Id == "subscriptions";

    /// <summary> Признак вкладки организаций. </summary>
    public bool IsOrganizations => this.Id == "organizations";

    /// <summary> Сообщение при отсутствии элементов. </summary>
    public string EmptyMessage => emptyMessageKey == null ? string.Empty : AdminLocalization.Instance[emptyMessageKey];

    /// <summary> Примечание об ограничении количества. </summary>
    public string? LimitNote => limitNoteKeys is { Count: > 0 }
        ? string.Join(" ", limitNoteKeys.Select(key => AdminLocalization.Instance[key]))
        : null;

    /// <summary> Признак наличия примечания об ограничении. </summary>
    public bool HasLimitNote => !string.IsNullOrWhiteSpace(this.LimitNote);

    /// <summary> Признак наличия элементов. </summary>
    public bool HasItems => this.Items.Count > 0;

    public void RefreshLocalizedValues()
    {
        this.OnPropertyChanged(nameof(this.Title));
        this.OnPropertyChanged(nameof(this.DisplayTitle));
        this.OnPropertyChanged(nameof(this.EmptyMessage));
        this.OnPropertyChanged(nameof(this.LimitNote));
    }
}
