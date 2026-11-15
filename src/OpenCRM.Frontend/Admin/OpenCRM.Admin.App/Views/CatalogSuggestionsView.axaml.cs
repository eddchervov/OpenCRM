using Avalonia.Controls;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

public partial class CatalogSuggestionsView : UserControl
{
    public CatalogSuggestionsView() => this.InitializeComponent();

    private async void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && this.DataContext is CatalogSuggestionsViewModel model)
        {
            await model.LoadSelectedTabAsync();
        }
    }
}
