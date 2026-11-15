using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

/// <summary> Представление коммуникационного раздела. </summary>
public partial class EmailHistoryView : UserControl
{
    public EmailHistoryView() => this.InitializeComponent();

    private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter && this.DataContext is EmailHistoryViewModel model && model.ApplyFiltersCommand.CanExecute(null))
        {
            model.ApplyFiltersCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key != Key.Enter)
        {
            return;
        }

        this.OpenSelectedRow();
        eventArgs.Handled = true;
    }

    private void OnRowDoubleTapped(object? sender, RoutedEventArgs eventArgs) => this.OpenSelectedRow();

    private async void OnOpenRowClick(object? sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (sender is not Button { DataContext: EmailHistoryRow row }
            || this.DataContext is not EmailHistoryViewModel model
            || !model.Rows.Contains(row))
        {
            return;
        }

        model.SelectedRow = row;
        if (model.OpenDetailCommand.CanExecute(null))
        {
            await model.OpenDetailCommand.ExecuteAsync(null);
        }
    }

    private void OpenSelectedRow()
    {
        if (this.DataContext is EmailHistoryViewModel model && model.OpenDetailCommand.CanExecute(null))
        {
            model.OpenDetailCommand.Execute(null);
        }
    }
}
