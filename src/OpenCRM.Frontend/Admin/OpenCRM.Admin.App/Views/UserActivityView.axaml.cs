using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Views;

public partial class UserActivityView : UserControl
{
    public UserActivityView() => this.InitializeComponent();

    private async void OnOpenUserPicker(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.DataContext is not UserActivityViewModel model)
        {
            return;
        }

        this.ActivityUserPicker.Focus();
        await model.PopulateUsers(string.Empty, CancellationToken.None);
        if (this.DataContext == model && this.IsEffectivelyVisible)
        {
            this.ActivityUserPicker.PopulateComplete();
            this.ActivityUserPicker.IsDropDownOpen = true;
        }
    }
}
