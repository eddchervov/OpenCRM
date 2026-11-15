using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

/// <summary> Представление раздела биллинга. </summary>
public partial class BillingView : UserControl
{
    /// <summary> Создаёт представление биллинга и инициализирует компоненты. </summary>
    public BillingView() => this.InitializeComponent();

    /// <summary> Обрабатывает нажатия клавиш в поле поиска. </summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter && this.DataContext is BillingViewModel model && model.ApplyFiltersCommand.CanExecute(null))
        {
            model.ApplyFiltersCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    /// <summary> Обрабатывает нажатия клавиш в списке строк. </summary>
    private void OnRowsKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            this.OpenSelectedRow();
            eventArgs.Handled = true;
        }
    }

    /// <summary> Открывает строку по двойному нажатию. </summary>
    private void OnRowDoubleTapped(object? sender, RoutedEventArgs eventArgs) => this.OpenSelectedRow();

    /// <summary> Обрабатывает сортировку по сумме. </summary>
    private void OnAmountSort(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.DataContext is BillingViewModel model)
        {
            var key = model.IsPaymentsTab ? "amount" : "source";
            if (model.SortCommand.CanExecute(key))
            {
                model.SortCommand.Execute(key);
            }
        }
    }

    /// <summary> Открывает выбранную строку. </summary>
    private void OpenSelectedRow()
    {
        if (this.DataContext is BillingViewModel model && model.SelectedRow is { } row && model.OpenDetailCommand.CanExecute(row))
        {
            model.OpenDetailCommand.Execute(row);
        }
    }
}
