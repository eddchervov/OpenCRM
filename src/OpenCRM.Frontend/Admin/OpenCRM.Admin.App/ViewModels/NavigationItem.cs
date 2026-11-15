namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Элемент навигации с правом доступа. </summary>
public sealed record NavigationItem(string Title, string Permission)
{
    /// <summary> Признак заглушки навигации. </summary>
    public bool IsPlaceholder => this.Permission is not (
        "Admin.Dashboard.View" or "Admin.Users.View" or "Admin.Billing.View"
        or "Admin.Configuration.Manage" or "Admin.Content.View" or "Admin.Communications.View" or "Admin.Permissions.Manage");
}
