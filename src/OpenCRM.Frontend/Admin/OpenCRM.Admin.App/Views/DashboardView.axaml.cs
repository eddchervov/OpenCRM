using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

/// <summary> Представление главной страницы администрирования. </summary>
public partial class DashboardView : UserControl
{
    /// <summary> Таймер периодического обновления данных. </summary>
    private readonly DispatcherTimer _timer = new();

    /// <summary> Модель представления главной страницы. </summary>
    private DashboardViewModel? _viewModel;

    /// <summary> Создаёт представление и инициализирует компоненты. </summary>
    public DashboardView()
    {
        this.InitializeComponent();
        this._timer.Tick += (_, _) =>
        {
            if (this.IsEffectivelyVisible && TopLevel.GetTopLevel(this) is Window { IsActive: true, WindowState: not WindowState.Minimized }
                && this._viewModel?.RefreshCommand.CanExecute(null) == true)
            {
                this._viewModel.RefreshCommand.Execute(null);
            }
        };
    }

    /// <summary> Подключается к визуальному дереву и запускает таймер. </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        base.OnAttachedToVisualTree(eventArgs);
        this.UpdateViewModel();
    }

    /// <summary> Отключается от визуального дерева и останавливает таймер. </summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs eventArgs)
    {
        this._timer.Stop();
        if (this._viewModel != null)
        {
            this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        }

        this._viewModel = null;
        base.OnDetachedFromVisualTree(eventArgs);
    }

    /// <summary> Обновляет модель при смене контекста данных. </summary>
    protected override void OnDataContextChanged(EventArgs eventArgs)
    {
        base.OnDataContextChanged(eventArgs);
        this.UpdateViewModel();
    }

    /// <summary> Синхронизирует подписку на события модели представления. </summary>
    private void UpdateViewModel()
    {
        if (this._viewModel != null)
        {
            this._viewModel.PropertyChanged -= this.OnViewModelPropertyChanged;
        }

        this._viewModel = this.DataContext as DashboardViewModel;
        if (this._viewModel != null)
        {
            this._viewModel.PropertyChanged += this.OnViewModelPropertyChanged;
            this._timer.Interval = TimeSpan.FromSeconds(this._viewModel.RefreshSeconds);
            this._timer.Start();
        }
    }

    /// <summary> Реагирует на изменение свойств модели представления. </summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(DashboardViewModel.RefreshSeconds) && this._viewModel != null)
        {
            this._timer.Interval = TimeSpan.FromSeconds(this._viewModel.RefreshSeconds);
        }
    }
}
