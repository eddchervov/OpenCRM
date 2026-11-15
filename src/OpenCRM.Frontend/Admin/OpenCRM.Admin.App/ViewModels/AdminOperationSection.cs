using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Раздел административных операций с общей отменой загрузок. </summary>
public partial class AdminOperationSection(string title, string permission, CommunicationsViewModel model) : ObservableObject
{
    public string Title { get; } = title;
    public string Permission { get; } = permission;
    public CommunicationsViewModel Model { get; } = model;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badgeCount;

    public bool HasBadge => this.BadgeCount > 0;
}
