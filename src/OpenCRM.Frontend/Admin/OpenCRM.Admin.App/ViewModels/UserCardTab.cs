using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Вкладка карточки пользователя. </summary>
public sealed partial class UserCardTab(
    string title,
    IReadOnlyList<UserCardItem> items,
    string emptyMessage,
    string? limitNote = null) : ObservableObject
{
    /// <summary> Заголовок вкладки. </summary>
    public string Title { get; } = title;

    /// <summary> Заголовок вкладки с количеством элементов. </summary>
    public string DisplayTitle => this.Title.Split('·')[0].Trim();

    /// <summary> Элементы вкладки. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItems))]
    private IReadOnlyList<UserCardItem> _items = items;

    /// <summary> Признак вкладки поддержки. </summary>
    public bool IsSupport => this.DisplayTitle == "Обращения";

    /// <summary> Признак вкладки со списком записей. </summary>
    public bool IsRecordList => !this.IsOverview && !this.IsSupport && !this.IsManagement;

    /// <summary> Признак вкладки административных действий. </summary>
    public bool IsManagement => this.DisplayTitle == "Управление";

    /// <summary> Признак вкладки обзора. </summary>
    public bool IsOverview => this.DisplayTitle == "Обзор";

    /// <summary> Признак вкладки подписок. </summary>
    public bool IsSubscriptions => this.DisplayTitle == "Подписки";

    /// <summary> Признак вкладки организаций. </summary>
    public bool IsOrganizations => this.DisplayTitle == "Организации";

    /// <summary> Сообщение при отсутствии элементов. </summary>
    public string EmptyMessage { get; } = emptyMessage;

    /// <summary> Примечание об ограничении количества. </summary>
    public string? LimitNote { get; } = limitNote;

    /// <summary> Признак наличия примечания об ограничении. </summary>
    public bool HasLimitNote => !string.IsNullOrWhiteSpace(this.LimitNote);

    /// <summary> Признак наличия элементов. </summary>
    public bool HasItems => this.Items.Count > 0;
}
