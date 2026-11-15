using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

/// <summary> Представление списка пользователей. </summary>
public partial class UsersView : UserControl
{
    /// <summary> Создаёт представление пользователей и инициализирует компоненты. </summary>
    public UsersView() => this.InitializeComponent();

    /// <summary> Обрабатывает нажатия клавиш в поле поиска. </summary>
    private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter && this.DataContext is UsersViewModel model && model.ApplyFiltersCommand.CanExecute(null))
        {
            model.ApplyFiltersCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    /// <summary> Обрабатывает нажатия клавиш в списке пользователей. </summary>
    private void OnUsersKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            this.OpenSelectedUser();
            eventArgs.Handled = true;
        }
    }

    /// <summary> Открывает пользователя по двойному нажатию. </summary>
    private void OnUserDoubleTapped(object? sender, RoutedEventArgs eventArgs) => this.OpenSelectedUser();

    /// <summary> Открывает выбранного пользователя. </summary>
    private void OpenSelectedUser()
    {
        if (this.DataContext is UsersViewModel model && model.OpenCardCommand.CanExecute(model.SelectedUser))
        {
            model.OpenCardCommand.Execute(model.SelectedUser);
        }
    }
}
