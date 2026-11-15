using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Проверяет файл до явного подтверждения импорта в выбранную организацию. </summary>
public partial class CrmImportViewModel(AdminSessionClient sessionClient) : CommunicationsViewModel(sessionClient)
{
    private byte[] _fileContent = [];

    [ObservableProperty] private IReadOnlyList<AdminCrmImportUser> _users = [];
    [ObservableProperty] private IReadOnlyList<AdminCrmImportOrganization> _organizations = [];
    [ObservableProperty] private AdminCrmImportUser? _selectedUser;
    [ObservableProperty] private AdminCrmImportOrganization? _selectedOrganization;
    [ObservableProperty] private string _fileName = string.Empty;
    [ObservableProperty] private AdminCrmImportPreview? _preview;
    [ObservableProperty] private AdminCrmImportResult? _result;
    [ObservableProperty] private bool _isConfirmationOpen;
    [ObservableProperty] private bool _isOutcomeUnknown;

    public bool CanSelectFile => this.CanLoad() && this.SelectedOrganization != null;
    public bool CanDownload => this.CanLoad() && this.Preview != null;
    public bool HasPreview => this.Preview != null;
    public bool HasResult => this.Result != null;
    public bool HasResultReport => this.Result != null || this.Preview?.AlreadyImported == true;
    public string Summary => this.Preview is not { } preview ? string.Empty
        : $"Клиенты: {preview.Summary.Clients} · Техника: {preview.Summary.Vehicles} · К добавлению: {preview.Summary.Create} · Дубли: {preview.Summary.Duplicate} · Ошибки: {preview.Summary.Error}";
    public string ResultSummary => this.Result is not { } result ? string.Empty
        : $"Добавлено клиентов: {result.CreatedClients} · Техники: {result.CreatedVehicles} · Пропущено дублей: {result.SkippedDuplicates} · Ошибки: {result.Errors}";

    public void SetFile(string name, byte[] content)
    {
        if (!this.CanSelectFile)
        {
            return;
        }

        if (content.Length is 0 or > 10 * 1024 * 1024)
        {
            this.ErrorMessage = "Выберите файл размером от 1 байта до 10 МБ.";
            return;
        }

        this.FileName = Path.GetFileName(name);
        this._fileContent = content;
        this.ClearPreview();
        this.NotifyCommands();
    }

    [RelayCommand(CanExecute = nameof(CanLoad))]
    private Task SearchUsersAsync() => this.RunAsync(async token =>
    {
        var users = await this.SessionClient.GetCrmImportUsersAsync(this.Search.Trim(), token);
        token.ThrowIfCancellationRequested();
        this.SelectedUser = null;
        this.Users = users;
    });

    private bool CanLoadOrganizations() => this.CanLoad() && this.SelectedUser != null;

    [RelayCommand(CanExecute = nameof(CanLoadOrganizations))]
    private Task LoadOrganizationsAsync() => this.RunAsync(async token =>
    {
        var organizations = await this.SessionClient.GetCrmImportOrganizationsAsync(this.SelectedUser!.Id, token);
        token.ThrowIfCancellationRequested();
        this.Organizations = organizations;
        this.SelectedOrganization = organizations.FirstOrDefault();
    });

    private bool CanPreview() => this.CanSelectFile && this._fileContent.Length > 0;

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private Task PreviewFileAsync() => this.RunAsync(async token =>
    {
        var preview = await this.SessionClient.CreateCrmImportPreviewAsync(this.SelectedUser!.Id,
            this.SelectedOrganization!.Id, this.FileName, this._fileContent, token);
        token.ThrowIfCancellationRequested();
        this.ClearPreview();
        this.Preview = preview;
    });

    private bool CanConfirm() => this.CanLoad() && !this.IsOutcomeUnknown
        && this.Preview is { AlreadyImported: false, Summary.Error: 0 } && this.Result == null;

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void RequestConfirmation() => this.IsConfirmationOpen = true;

    [RelayCommand]
    private void CancelConfirmation() => this.IsConfirmationOpen = false;

    private bool CanExecuteImport() => this.CanConfirm() && this.IsConfirmationOpen;

    [RelayCommand(CanExecute = nameof(CanExecuteImport))]
    private Task ConfirmAsync() => this.RunAsync(async token =>
    {
        this.IsConfirmationOpen = false;
        this.IsOutcomeUnknown = true;
        var result = await this.SessionClient.ConfirmCrmImportAsync(this.Preview!.Id, token);
        token.ThrowIfCancellationRequested();
        this.Result = result;
        this.IsOutcomeUnknown = false;
    });

    public async Task<byte[]?> DownloadReportAsync(bool result)
    {
        byte[]? content = null;
        if (!this.CanDownload || (result && !this.HasResultReport))
        {
            return null;
        }

        await this.RunAsync(async token =>
        {
            var report = await this.SessionClient.DownloadCrmImportReportAsync(this.Preview!.Id, result, token);
            token.ThrowIfCancellationRequested();
            content = report;
        });

        return content;
    }

    protected override async Task LoadPageAsync(CancellationToken cancellationToken)
    {
        if (this.Preview != null)
        {
            var preview = await this.SessionClient.GetCrmImportPreviewAsync(this.Preview.Id, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            this.Preview = preview;
            this.IsOutcomeUnknown = false;
            return;
        }

        var users = await this.SessionClient.GetCrmImportUsersAsync(this.Search.Trim(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        this.Users = users;
    }

    protected override void ApplyFilters() { }
    protected override void ResetFilters() { }

    protected override void ClearData()
    {
        this.Users = [];
        this.SelectedUser = null;
        this.Organizations = [];
        this.SelectedOrganization = null;
        this.FileName = string.Empty;
        this._fileContent = [];
        this.ClearPreview();
    }

    private void ClearPreview()
    {
        this.Preview = null;
        this.Result = null;
        this.IsConfirmationOpen = false;
        this.IsOutcomeUnknown = false;
    }

    protected override void NotifyCommands()
    {
        base.NotifyCommands();
        this.SearchUsersCommand.NotifyCanExecuteChanged();
        this.LoadOrganizationsCommand.NotifyCanExecuteChanged();
        this.PreviewFileCommand.NotifyCanExecuteChanged();
        this.RequestConfirmationCommand.NotifyCanExecuteChanged();
        this.ConfirmCommand.NotifyCanExecuteChanged();
        this.OnPropertyChanged(nameof(CanSelectFile));
        this.OnPropertyChanged(nameof(CanDownload));
    }

    partial void OnSelectedUserChanged(AdminCrmImportUser? value)
    {
        this.Organizations = [];
        this.SelectedOrganization = null;
        this.ClearPreview();
        this.NotifyCommands();
    }

    partial void OnSelectedOrganizationChanged(AdminCrmImportOrganization? value)
    {
        this.FileName = string.Empty;
        this._fileContent = [];
        this.ClearPreview();
        this.NotifyCommands();
    }

    partial void OnPreviewChanged(AdminCrmImportPreview? value)
    {
        this.OnPropertyChanged(nameof(HasPreview));
        this.OnPropertyChanged(nameof(Summary));
        this.OnPropertyChanged(nameof(HasResultReport));
        this.NotifyCommands();
    }

    partial void OnResultChanged(AdminCrmImportResult? value)
    {
        this.OnPropertyChanged(nameof(HasResult));
        this.OnPropertyChanged(nameof(ResultSummary));
        this.OnPropertyChanged(nameof(HasResultReport));
        this.NotifyCommands();
    }

    partial void OnIsOutcomeUnknownChanged(bool value) => this.NotifyCommands();
    partial void OnIsConfirmationOpenChanged(bool value) => this.NotifyCommands();
}
