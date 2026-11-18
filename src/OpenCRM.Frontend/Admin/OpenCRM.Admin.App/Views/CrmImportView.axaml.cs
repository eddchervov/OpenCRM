using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.Views;

public partial class CrmImportView : UserControl
{
    public CrmImportView() => this.InitializeComponent();

    private async void OnChooseFile(object? sender, RoutedEventArgs args)
    {
        if (this.DataContext is not CrmImportViewModel model || !model.CanSelectFile
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var organization = model.SelectedOrganization;
        try
        {
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = AdminLocalization.Instance["native.crmImport.vybrat.fayl.crm"], AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType(AdminLocalization.Instance["native.crmImport.fayl.crm"]) { Patterns = ["*.csv", "*.xlsx", "*.json", "*.xml"] }]
            });
            if (files.Count == 0)
            {
                return;
            }

            using var file = files[0];
            await using var stream = await file.OpenReadAsync();
            using var content = new MemoryStream();
            var buffer = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                if (content.Length + read > 10 * 1024 * 1024)
                {
                    model.ErrorMessage = AdminLocalization.Instance["native.crmImport.fayl.prevyshaet.10.mb"];
                    return;
                }

                content.Write(buffer, 0, read);
            }

            if (ReferenceEquals(model.SelectedOrganization, organization))
            {
                model.SetFile(file.Name, content.ToArray());
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            model.ErrorMessage = AdminLocalization.Instance["native.crmImport.ne.udalos.prochitat.fayl.proverte.dostup"];
        }
    }

    private async void OnSavePreview(object? sender, RoutedEventArgs args) => await this.SaveReportAsync(false);
    private async void OnSaveResult(object? sender, RoutedEventArgs args) => await this.SaveReportAsync(true);

    private async Task SaveReportAsync(bool result)
    {
        if (this.DataContext is not CrmImportViewModel model || !model.CanDownload
            || TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        var preview = model.Preview;
        try
        {
            var content = await model.DownloadReportAsync(result);
            if (content == null || !model.CanDownload || !ReferenceEquals(preview, model.Preview))
            {
                return;
            }

            using var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = AdminLocalization.Instance["native.crmImport.sokhranit.otchyot.crm"], SuggestedFileName = result ? "crm-import-result.csv" : "crm-import-preview.csv",
                ShowOverwritePrompt = true
            });
            if (file == null || !model.CanDownload || !ReferenceEquals(preview, model.Preview))
            {
                return;
            }

            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(content);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            model.ErrorMessage = AdminLocalization.Instance["native.crmImport.ne.udalos.sokhranit.otchyot.proverte.dostup"];
        }
    }
}
