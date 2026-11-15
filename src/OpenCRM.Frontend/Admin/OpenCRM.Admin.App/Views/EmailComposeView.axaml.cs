using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OpenCRM.Admin.App.Views;

public partial class EmailComposeView : UserControl
{
    public EmailComposeView() => this.InitializeComponent();

    private void OnOpenUserPicker(object? sender, RoutedEventArgs eventArgs)
    {
        this.ComposeUserPicker.Focus();
        this.ComposeUserPicker.PopulateComplete();
        this.ComposeUserPicker.IsDropDownOpen = true;
    }
}
