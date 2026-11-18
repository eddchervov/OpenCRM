using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Views;

public partial class PurchasesView : UserControl
{
    public PurchasesView() => this.InitializeComponent();

    private void OnSearchKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter && this.DataContext is PurchasesViewModel model && model.ApplyFiltersCommand.CanExecute(null))
        {
            model.ApplyFiltersCommand.Execute(null);
            eventArgs.Handled = true;
        }
    }

    private void OnRowsKeyDown(object? sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Enter)
        {
            this.OpenSelectedRow();
            eventArgs.Handled = true;
        }
    }

    private void OnRowDoubleTapped(object? sender, RoutedEventArgs eventArgs) => this.OpenSelectedRow();

    private void OnOpenRowClick(object? sender, RoutedEventArgs eventArgs)
    {
        eventArgs.Handled = true;
        if (sender is not Button { DataContext: PurchasesRow row }
            || this.DataContext is not PurchasesViewModel model
            || !model.Rows.Contains(row))
        {
            return;
        }

        model.SelectedRow = row;
        if (model.OpenDetailCommand.CanExecute(null))
        {
            model.OpenDetailCommand.Execute(null);
        }
    }

    private void OpenSelectedRow()
    {
        if (this.DataContext is PurchasesViewModel model && model.OpenDetailCommand.CanExecute(null))
        {
            model.OpenDetailCommand.Execute(null);
        }
    }

    private async void OnUploadFile(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.DataContext is not PurchasesViewModel model || !model.CanUpload || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var selectedPurchase = model.Detail;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = AdminLocalization.Instance["native.billing.vybrat.chek.npd"], AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(AdminLocalization.Instance["native.billing.chek.pdf.png.jpg"]) { Patterns = ["*.pdf", "*.png", "*.jpg", "*.jpeg"] }]
            });

            if (files.Count == 0)
            {
                return;
            }

            using var file = files[0];
            if (!model.CanUpload || !ReferenceEquals(model.Detail, selectedPurchase))
            {
                return;
            }

            var contentType = Path.GetExtension(file.Name).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => "application/octet-stream"
            };

            await using var stream = await file.OpenReadAsync();
            await model.UploadFileAsync(stream, file.Name, contentType);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (ReferenceEquals(model.Detail, selectedPurchase))
            {
                model.ErrorMessage = AdminLocalization.Instance["native.billing.ne.udalos.prochitat.fayl.proverte.dostup.i.povtorite.popytku"];
            }
        }
    }

    private async void OnDownloadFile(object? sender, RoutedEventArgs eventArgs)
    {
        if (this.DataContext is not PurchasesViewModel model || !model.CanDownload || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var selectedPurchase = model.Detail;
        try
        {
            var receipt = await model.DownloadFileAsync();
            if (receipt == null || !model.CanDownload || !ReferenceEquals(model.Detail, selectedPurchase))
            {
                return;
            }

            using var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = AdminLocalization.Instance["native.billing.sokhranit.chek.npd"], SuggestedFileName = Path.GetFileName(receipt.FileName), ShowOverwritePrompt = true
            });

            if (file == null || !model.CanDownload || !ReferenceEquals(model.Detail, selectedPurchase))
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(receipt.Content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (ReferenceEquals(model.Detail, selectedPurchase))
            {
                model.ErrorMessage = AdminLocalization.Instance["native.billing.ne.udalos.sokhranit.fayl.proverte.dostup.i.povtorite.popytku"];
            }
        }
    }
}
