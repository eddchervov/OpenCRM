using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

/// <summary> Представление организаций административной панели. </summary>
public partial class OrganizationsView : UserControl
{
    public OrganizationsView() => this.InitializeComponent();

    private void OnFilterKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter && this.DataContext is OrganizationsViewModel model && model.ApplyFiltersCommand.CanExecute(null))
        {
            model.ApplyFiltersCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnListKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            this.OpenSelectedOrganization();
            eventArgs.Handled = true;
        }
    }

    private void OnOrganizationDoubleTapped(object? sender, RoutedEventArgs eventArgs) => this.OpenSelectedOrganization();

    private void OpenSelectedOrganization()
    {
        if (this.DataContext is OrganizationsViewModel model && model.OpenCardCommand.CanExecute(model.SelectedOrganization))
        {
            model.OpenCardCommand.Execute(model.SelectedOrganization);
        }
    }
}
