namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Элемент навигации с правом доступа. </summary>
public sealed class NavigationItem : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public NavigationItem(string id, string titleKey, string permission)
    {
        this.Id = id;
        this.TitleKey = titleKey;
        this.Permission = permission;
    }

    public string Id { get; }
    public string TitleKey { get; }
    public string Permission { get; }
    public string Title => OpenCRM.Admin.App.Localization.AdminLocalization.Instance[this.TitleKey];

    public void RefreshTitle() => this.OnPropertyChanged(nameof(this.Title));

    /// <summary> Признак заглушки навигации. </summary>
    public bool IsPlaceholder => this.Permission is not (
        "Admin.Dashboard.View" or "Admin.Users.View" or "Admin.Billing.View"
        or "Admin.Configuration.Manage" or "Admin.Content.View" or "Admin.Communications.View" or "Admin.Permissions.Manage");
}
