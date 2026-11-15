using Avalonia.Controls;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

public partial class SiteNotificationsView : UserControl
{
    public SiteNotificationsView() => this.InitializeComponent();

    private async void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender) || this.DataContext is not SiteNotificationsViewModel viewModel)
        {
            return;
        }

        await viewModel.LoadSelectedTabAsync();
    }
}
