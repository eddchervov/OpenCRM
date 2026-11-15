namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Элемент карточки пользователя с полями. </summary>
public sealed record UserCardItem(
    string Title,
    string Subtitle,
    IReadOnlyList<DashboardMetric> Fields)
{
    /// <summary> Признак наличия подзаголовка. </summary>
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(this.Subtitle);
}
