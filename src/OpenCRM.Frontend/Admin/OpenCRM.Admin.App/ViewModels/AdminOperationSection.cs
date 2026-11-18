using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Раздел административных операций с общей отменой загрузок. </summary>
public partial class AdminOperationSection : ObservableObject
{
    public AdminOperationSection(string id, string titleKey, string permission, CommunicationsViewModel model)
    {
        this.Id = id;
        this.TitleKey = titleKey;
        this.Permission = permission;
        this.Model = model;
    }

    public string Id { get; }
    public string TitleKey { get; }
    public string Title => AdminLocalization.Instance[this.TitleKey];
    public string Permission { get; }
    public CommunicationsViewModel Model { get; }

    public void RefreshTitle() => this.OnPropertyChanged(nameof(this.Title));

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badgeCount;

    public bool HasBadge => this.BadgeCount > 0;
}
