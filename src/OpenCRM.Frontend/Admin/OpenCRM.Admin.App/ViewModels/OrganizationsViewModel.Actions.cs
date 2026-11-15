using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;

namespace OpenCRM.Admin.App.ViewModels;

public partial class OrganizationsViewModel
{
    private bool _requiresStatusRefresh;

    [ObservableProperty]
    private bool _isChangingStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingStatusChange))]
    [NotifyPropertyChangedFor(nameof(StatusChangeWarning))]
    private string _pendingStatusAction = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmStatusChangeCommand))]
    private string _statusChangeReason = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmStatusChangeCommand))]
    private bool _isStatusChangeConfirmed;

    [ObservableProperty]
    private string _statusChangeMessage = string.Empty;

    public bool CanManageOrganizations => this._sessionClient.CurrentProfile is { } profile
        && (profile.IsSystem || profile.Permissions.Contains("Admin.Users.Manage", StringComparer.Ordinal));

    public bool HasPendingStatusChange => this.PendingStatusAction.Length != 0;

    public string StatusChangeWarning => this.PendingStatusAction == "archive"
        ? "Организация станет недоступна для работы всем участникам. Через 30 дней штатная очистка пометит её удалённой. Архив не освобождает слот создания организации."
        : "Организация снова станет доступна участникам. Тариф, права и сохранённые данные останутся прежними.";

    public void RefreshAccess()
    {
        this.OnPropertyChanged(nameof(CanManageOrganizations));
        if (!this.CanManageOrganizations)
        {
            this.ResetStatusChange();
        }

        this.NotifyStatusCommands();
    }

    private bool CanArchive() => this.CanChangeStatus() && this.Organization is { IsArchived: false };

    private bool CanRestore() => this.CanChangeStatus() && this.Organization is { IsArchived: true };

    private bool CanChangeStatus() => this._isActive && this.IsCardOpen && !this.IsLoading
        && !this.IsChangingStatus && !this._requiresStatusRefresh
        && this.CanManageOrganizations && this.Organization is { IsDeleted: false };

    private bool CanConfirmStatusChange() => (this.PendingStatusAction == "archive" ? this.CanArchive() : this.CanRestore())
        && this.HasPendingStatusChange
        && this.IsStatusChangeConfirmed && !string.IsNullOrWhiteSpace(this.StatusChangeReason)
        && this.StatusChangeReason.Trim().Length <= 2000;

    [RelayCommand(CanExecute = nameof(CanArchive))]
    private void RequestArchive() => this.PrepareStatusChange("archive");

    [RelayCommand(CanExecute = nameof(CanRestore))]
    private void RequestRestore() => this.PrepareStatusChange("restore");

    private void PrepareStatusChange(string action)
    {
        this.ResetStatusChange();
        this.PendingStatusAction = action;
        this.StatusChangeMessage = string.Empty;
        this.CardErrorMessage = string.Empty;
        this.NotifyStatusCommands();
    }

    [RelayCommand(CanExecute = nameof(CanCancelStatusChange))]
    private void CancelStatusChange() => this.ResetStatusChange();

    private bool CanCancelStatusChange() => !this.IsChangingStatus;

    [RelayCommand(CanExecute = nameof(CanConfirmStatusChange))]
    private async Task ConfirmStatusChangeAsync(CancellationToken cancellationToken)
    {
        var id = this.Organization!.Id;
        var action = this.PendingStatusAction;
        var reason = this.StatusChangeReason.Trim();
        this.IsChangingStatus = true;
        this._requiresStatusRefresh = true;
        this.CardErrorMessage = string.Empty;
        this.StatusChangeMessage = string.Empty;
        try
        {
            var result = action == "archive"
                ? await this._sessionClient.ArchiveOrganizationAsync(id, reason, cancellationToken)
                : await this._sessionClient.RestoreOrganizationAsync(id, reason, cancellationToken);

            if (!this._isActive || cancellationToken.IsCancellationRequested || this._selectedId != id)
            {
                return;
            }

            this.ResetStatusChange();
            this.StatusChangeMessage = result.Message;
            var organization = await this._sessionClient.GetOrganizationAsync(id, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested || this._selectedId != id)
            {
                return;
            }

            this.Organization = organization;
            this._requiresStatusRefresh = false;
            this.Rows = this.Rows.Select(row => row.Id == id ? new AdminOrganizationRow(organization) : row).ToArray();
            this.SelectedOrganization = this.Rows.FirstOrDefault(row => row.Id == id);
        }
        catch (ApiException exception)
        {
            this.ResetStatusChange();
            await this.HandleErrorAsync(exception, true);
            if (exception.StatusCode == null && this._isActive && this._selectedId == id)
            {
                this.CardErrorMessage = "Ответ сервера не получен. Обновите карточку и проверьте статус перед новым действием.";
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            this.ResetStatusChange();
            if (this._isActive && this._selectedId == id)
            {
                this.CardErrorMessage = "Ответ сервера не получен. Обновите карточку и проверьте статус перед новым действием.";
            }
        }
        finally
        {
            this.IsChangingStatus = false;
        }
    }

    private void ResetStatusChange()
    {
        this.PendingStatusAction = string.Empty;
        this.StatusChangeReason = string.Empty;
        this.IsStatusChangeConfirmed = false;
        this.NotifyStatusCommands();
    }

    private void NotifyStatusCommands()
    {
        this.RequestArchiveCommand.NotifyCanExecuteChanged();
        this.RequestRestoreCommand.NotifyCanExecuteChanged();
        this.ConfirmStatusChangeCommand.NotifyCanExecuteChanged();
        this.CancelStatusChangeCommand.NotifyCanExecuteChanged();
        this.BackToListCommand.NotifyCanExecuteChanged();
        this.RefreshCardCommand.NotifyCanExecuteChanged();
        this.OpenCardCommand.NotifyCanExecuteChanged();
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.ApplyFiltersCommand.NotifyCanExecuteChanged();
        this.ResetFiltersCommand.NotifyCanExecuteChanged();
        this.SortCommand.NotifyCanExecuteChanged();
        this.PreviousPageCommand.NotifyCanExecuteChanged();
        this.NextPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsChangingStatusChanged(bool value) => this.NotifyStatusCommands();

    partial void OnIsLoadingCardChanged(bool value) => this.NotifyStatusCommands();

    partial void OnIsLoadingListChanged(bool value) => this.NotifyStatusCommands();
}
