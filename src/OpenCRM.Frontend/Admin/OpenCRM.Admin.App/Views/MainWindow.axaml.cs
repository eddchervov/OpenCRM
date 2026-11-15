using Avalonia;
using Avalonia.Controls;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

/// <summary> Главное окно административного приложения. </summary>
public partial class MainWindow : Window
{
    /// <summary> Создаёт главное окно и инициализирует компоненты. </summary>
    public MainWindow() => this.InitializeComponent();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if ((change.Property == Window.WindowStateProperty || change.Property == StyledElement.DataContextProperty)
            && this.DataContext is MainWindowViewModel model)
        {
            model.SetBackgroundPollingPaused(this.WindowState == WindowState.Minimized);
        }
    }
}
