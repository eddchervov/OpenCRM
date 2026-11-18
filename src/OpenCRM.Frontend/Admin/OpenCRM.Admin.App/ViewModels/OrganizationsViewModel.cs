using Avalonia.Controls;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель списка и карточки организаций административной панели. </summary>
public partial class OrganizationsViewModel : ObservableObject
{
    private static readonly string[] s_sortFields = ["name", "owner", "scenario", "status", "subscription", "created", "members", "clients", "vehicles", "orders"];
    private readonly AdminSessionClient _sessionClient;
    private bool _isActive;
    private string _appliedSearch = string.Empty;
    private string _appliedUser = string.Empty;
    private string _sort = "created";
    private bool _descending = true;
    private Guid? _selectedId;
    private int _appliedPageSize = 50;
    private bool _commercialEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private string _errorMessage = string.Empty;

    private string? _listErrorTranslationKey;

    partial void OnErrorMessageChanging(string value) => this._listErrorTranslationKey = null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCardError))]
    private string _cardErrorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(SortCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isLoadingList;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyCanExecuteChangedFor(nameof(OpenCardCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCardCommand))]
    private bool _isLoadingCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private IReadOnlyList<AdminOrganizationRow> _rows = [];

    [ObservableProperty]
    private AdminOrganizationRow? _selectedOrganization;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _page = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    private int _pageSize = 50;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsListOpen))]
    private bool _isCardOpen;

    [ObservableProperty]
    private AdminOrganization? _organization;

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private string _user = string.Empty;

    public OrganizationsViewModel(AdminSessionClient sessionClient) => this._sessionClient = sessionClient;

    public IReadOnlyList<int> AvailablePageSizes { get; } = [25, 50, 100];
    public bool IsLoading => this.IsLoadingList || this.IsLoadingCard;
    public bool IsListOpen => !this.IsCardOpen;
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);
    public bool HasCardError => !string.IsNullOrWhiteSpace(this.CardErrorMessage);
    public bool IsEmpty => !this.IsLoadingList && this.Rows.Count == 0 && !this.HasError && !this.IsCardOpen;
    public bool CanViewCommercial => this._commercialEnabled;
    public GridLength CommercialColumnWidth => this.CanViewCommercial ? new GridLength(170, GridUnitType.Star) : new GridLength(0);
    public double TableMinWidth => this.CanViewCommercial ? 1264 : 1094;
    public string TotalLabel => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        AdminLocalization.Instance["core.organizations.total"], this.TotalCount);
    public string PageLabel => $"{this.Page} / {Math.Max(1, (int)Math.Ceiling((double)this.TotalCount / this.PageSize))}";
    public string NameHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.nameHeader"], "name");
    public string OwnerHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.ownerHeader"], "owner");
    public string ScenarioHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.scenarioHeader"], "scenario");
    public string StatusHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.statusHeader"], "status");
    public string SubscriptionHeader => this.CanViewCommercial ? this.SortLabel(AdminLocalization.Instance["core.organizations.subscriptionHeader"], "subscription") : string.Empty;
    public string MembersHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.membersHeader"], "members");
    public string ClientsHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.clientsHeader"], "clients");
    public string VehiclesHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.vehiclesHeader"], "vehicles");
    public string OrdersHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.ordersHeader"], "orders");
    public string CreatedHeader => this.SortLabel(AdminLocalization.Instance["core.organizations.createdHeader"], "created");
    public string ActiveSubscriptionLabel => !this.CanViewCommercial ? string.Empty : string.IsNullOrWhiteSpace(this.Organization?.ActiveSubscriptionName)
        ? AdminLocalization.Instance["core.organizations.noActivePlan"]
        : this.Organization.ActiveSubscriptionName;
    public string SubscriptionEndLabel => this.CanViewCommercial ? UserDisplayFormatting.Date(this.Organization?.ActiveSubscriptionEndDate) : string.Empty;
    public string CreatedAtLabel => UserDisplayFormatting.DateTime(this.Organization?.CreatedAt);
    public string UpdatedAtLabel => UserDisplayFormatting.DateTime(this.Organization?.UpdatedAt);
    public string ArchiveDateLabel => UserDisplayFormatting.DateTime(this.Organization?.ArchiveDate);
    public string DeletionDateLabel => UserDisplayFormatting.DateTime(this.Organization?.DeletionDate);
    public string WorkOrdersTotalLabel => this.FormatOrganizationTotal(this.Organization?.WorkOrdersTotal ?? 0, total => total.WorkOrdersTotal);
    public string PaymentsTotalLabel => this.FormatOrganizationTotal(this.Organization?.PaymentsTotal ?? 0, total => total.PaymentsTotal);
    public string InventoryValueLabel => this.FormatOrganizationTotal(this.Organization?.InventoryValue ?? 0, total => total.InventoryValue);
    public string OrganizationNameLabel => FormatLabel("organization.card.organization", this.Organization?.Name);
    public string OrganizationScenarioLabel => this.Organization == null ? string.Empty
        : AdminDisplayLabels.OrganizationScenario(this.Organization.PrimaryScenarioCode, this.Organization.ScenarioName);
    public string OwnerLabel => FormatLabel("organization.card.owner", this.Organization?.Owner);
    public string CreatedAtCaption => FormatLabel("organization.card.created", this.CreatedAtLabel);
    public string UpdatedAtCaption => FormatLabel("organization.card.updated", this.UpdatedAtLabel);
    public string InnCaption => FormatLabel("organization.card.inn", this.Organization?.Inn);
    public string SubscriptionEndCaption => FormatLabel("organization.card.subscriptionEnd", this.SubscriptionEndLabel);
    public string AdminsCountCaption => FormatLabel("organization.card.admins", this.Organization?.AdminsCount);
    public string CompletedOrdersCaption => FormatLabel("organization.card.completedOrders", this.Organization?.CompletedWorkOrdersCount);
    public string OverdueTasksCaption => FormatLabel("organization.card.overdueTasks", this.Organization?.OverdueTasksCount);
    public string LowStockCaption => FormatLabel("organization.card.lowStock", this.Organization?.LowStockItemsCount);
    public string ScenarioCodeCaption => FormatLabel("organization.card.scenarioCode", this.Organization?.PrimaryScenarioCode);
    public string DocumentStyleCaption => FormatLabel("organization.card.documentStyle", this.Organization?.DocumentStyleCode);
    public string ArchiveDateCaption => FormatLabel("organization.card.archived", this.ArchiveDateLabel);
    public string DeletionDateCaption => FormatLabel("organization.card.deleted", this.DeletionDateLabel);
    public event Action? SessionExpired;
    public event Func<Task>? AccessDenied;

    public void SetCommercialEnabled(bool commercialEnabled)
    {
        this._commercialEnabled = commercialEnabled;
        this.OnPropertyChanged(nameof(CanViewCommercial));
        this.OnPropertyChanged(nameof(CommercialColumnWidth));
        this.OnPropertyChanged(nameof(TableMinWidth));
        this.OnPropertyChanged(nameof(SubscriptionHeader));
        this.OnPropertyChanged(nameof(ActiveSubscriptionLabel));
        this.OnPropertyChanged(nameof(SubscriptionEndLabel));
        var selectedOrganizationId = this.SelectedOrganization?.Id;
        this.Rows = this.Rows.Select(row => new AdminOrganizationRow(row.Organization, commercialEnabled)).ToArray();
        this.SelectedOrganization = this.Rows.FirstOrDefault(row => row.Id == selectedOrganizationId);
    }

    public void RefreshLocalizedValues()
    {
        this.OnPropertyChanged(nameof(this.TotalLabel));
        this.OnPropertyChanged(nameof(this.NameHeader));
        this.OnPropertyChanged(nameof(this.OwnerHeader));
        this.OnPropertyChanged(nameof(this.ScenarioHeader));
        this.OnPropertyChanged(nameof(this.StatusHeader));
        this.OnPropertyChanged(nameof(this.SubscriptionHeader));
        this.OnPropertyChanged(nameof(this.MembersHeader));
        this.OnPropertyChanged(nameof(this.ClientsHeader));
        this.OnPropertyChanged(nameof(this.VehiclesHeader));
        this.OnPropertyChanged(nameof(this.OrdersHeader));
        this.OnPropertyChanged(nameof(this.CreatedHeader));
        this.OnPropertyChanged(nameof(this.StatusChangeWarning));
        var selectedOrganizationId = this.SelectedOrganization?.Id;
        this.Rows = this.Rows.Select(row => new AdminOrganizationRow(row.Organization, this.CanViewCommercial)).ToArray();
        this.SelectedOrganization = this.Rows.FirstOrDefault(row => row.Id == selectedOrganizationId);
        this.OnPropertyChanged(nameof(this.ActiveSubscriptionLabel));
        this.NotifyLocalizedCardLabels();
        if (this._cardErrorTranslationKey != null)
        {
            this.SetCardErrorKey(this._cardErrorTranslationKey);
        }

        if (this._listErrorTranslationKey != null)
        {
            this.SetListErrorKey(this._listErrorTranslationKey);
        }
    }

    public async Task ActivateAsync()
    {
        this._isActive = true;
        this.IsCardOpen = false;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        await Task.WhenAll(new[] { this.RefreshCommand.ExecutionTask, this.OpenCardCommand.ExecutionTask,
            this.RefreshCardCommand.ExecutionTask, this.ConfirmStatusChangeCommand.ExecutionTask }.OfType<Task>());
    }

    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this.RefreshCommand.Cancel();
        this.OpenCardCommand.Cancel();
        this.RefreshCardCommand.Cancel();
        this.ConfirmStatusChangeCommand.Cancel();
        this.ResetStatusChange();
        this.StatusChangeMessage = string.Empty;
        if (!clear)
        {
            return;
        }

        this.Rows = [];
        this.SelectedOrganization = null;
        this._selectedId = null;
        this.Organization = null;
        this.IsCardOpen = false;
        this.Search = string.Empty;
        this.User = string.Empty;
        this._appliedSearch = string.Empty;
        this._appliedUser = string.Empty;
        this.Page = 1;
        this.TotalCount = 0;
        this.PageSize = 50;
        this._appliedPageSize = 50;
        this.ErrorMessage = string.Empty;
        this.CardErrorMessage = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanLoadList))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        this.IsLoadingList = true;
        this.ErrorMessage = string.Empty;
        try
        {
            var result = await this._sessionClient.GetOrganizationsAsync(this._appliedSearch, this._appliedUser,
                this._sort, this._descending, this.Page, this.PageSize, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            this.Rows = result.Organizations.Select(organization => new AdminOrganizationRow(organization, this.CanViewCommercial)).ToArray();
            this.Page = Math.Max(1, result.Page);
            this.TotalCount = result.TotalCount;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Переход с раздела отменяет загрузку.
        }
        catch (ApiException exception)
        {
            await this.HandleErrorAsync(exception, false);
        }
        finally
        {
            this.IsLoadingList = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanLoadList))]
    private async Task ApplyFiltersAsync()
    {
        this._appliedSearch = this.Search.Trim();
        this._appliedUser = this.User.Trim();
        this.Page = 1;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanLoadList))]
    private async Task ResetFiltersAsync()
    {
        this.Search = string.Empty;
        this.User = string.Empty;
        this._sort = "created";
        this._descending = true;
        this.NotifySortHeaders();
        await this.ApplyFiltersCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanLoadList))]
    private async Task SortAsync(string sort)
    {
        if (!s_sortFields.Contains(sort, StringComparer.Ordinal))
        {
            return;
        }

        this._descending = this._sort == sort && !this._descending;
        this._sort = sort;
        this.NotifySortHeaders();
        this.Page = 1;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync()
    {
        this.Page--;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync()
    {
        this.Page++;
        await this.RefreshCommand.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanOpenCard))]
    private async Task OpenCardAsync(AdminOrganizationRow row, CancellationToken cancellationToken)
    {
        this.ResetStatusChange();
        this.StatusChangeMessage = string.Empty;
        this._selectedId = row.Id;
        this.SelectedOrganization = row;
        this.Organization = null;
        this.IsCardOpen = true;
        await this.LoadCardAsync(row.Id, cancellationToken);
    }

    [RelayCommand(CanExecute = nameof(CanRefreshCard))]
    private Task RefreshCardAsync(CancellationToken cancellationToken) => this._selectedId is { } id
        ? this.LoadCardAsync(id, cancellationToken)
        : Task.CompletedTask;

    [RelayCommand(CanExecute = nameof(CanBackToList))]
    private async Task BackToListAsync()
    {
        this.ResetStatusChange();
        this.StatusChangeMessage = string.Empty;
        this.IsCardOpen = false;
        this.RefreshCardCommand.Cancel();
        this.OpenCardCommand.Cancel();
        await Task.WhenAll(new[] { this.OpenCardCommand.ExecutionTask, this.RefreshCardCommand.ExecutionTask }.OfType<Task>());
        this.CardErrorMessage = string.Empty;
    }

    private async Task LoadCardAsync(Guid id, CancellationToken cancellationToken)
    {
        this.IsLoadingCard = true;
        this.CardErrorMessage = string.Empty;
        try
        {
            var organization = await this._sessionClient.GetOrganizationAsync(id, cancellationToken);
            if (!this._isActive || !this.IsCardOpen || cancellationToken.IsCancellationRequested || this._selectedId != id)
            {
                return;
            }

            this.ResetStatusChange();
            this._requiresStatusRefresh = false;
            this.Organization = organization;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Возврат к списку отменяет загрузку карточки.
        }
        catch (ApiException exception)
        {
            await this.HandleErrorAsync(exception, true);
        }
        finally
        {
            this.IsLoadingCard = false;
        }
    }

    private bool CanLoadList() => this._isActive && !this.IsLoadingList && !this.IsChangingStatus;
    private bool CanPreviousPage() => this.CanLoadList() && this.Page > 1;
    private bool CanOpenCard(AdminOrganizationRow? row) => this._isActive && row != null && !this.IsLoadingCard && !this.IsChangingStatus;
    private bool CanRefreshCard() => this._isActive && this._selectedId != null && !this.IsLoadingCard && !this.IsChangingStatus;
    private bool CanBackToList() => !this.IsChangingStatus;
    private bool CanNextPage() => this.CanLoadList() && this.Page * this.PageSize < this.TotalCount;

    partial void OnOrganizationChanged(AdminOrganization? value)
    {
        this.NotifyStatusCommands();
        this.OnPropertyChanged(nameof(SubscriptionEndLabel));
        this.OnPropertyChanged(nameof(ActiveSubscriptionLabel));
        this.OnPropertyChanged(nameof(CreatedAtLabel));
        this.OnPropertyChanged(nameof(UpdatedAtLabel));
        this.OnPropertyChanged(nameof(ArchiveDateLabel));
        this.OnPropertyChanged(nameof(DeletionDateLabel));
        this.OnPropertyChanged(nameof(WorkOrdersTotalLabel));
        this.OnPropertyChanged(nameof(PaymentsTotalLabel));
        this.OnPropertyChanged(nameof(InventoryValueLabel));
        this.NotifyLocalizedCardLabels();
    }

    private void NotifyLocalizedCardLabels()
    {
        this.OnPropertyChanged(nameof(this.OrganizationNameLabel));
        this.OnPropertyChanged(nameof(this.OrganizationScenarioLabel));
        this.OnPropertyChanged(nameof(this.OwnerLabel));
        this.OnPropertyChanged(nameof(this.CreatedAtCaption));
        this.OnPropertyChanged(nameof(this.UpdatedAtCaption));
        this.OnPropertyChanged(nameof(this.InnCaption));
        this.OnPropertyChanged(nameof(this.SubscriptionEndCaption));
        this.OnPropertyChanged(nameof(this.AdminsCountCaption));
        this.OnPropertyChanged(nameof(this.CompletedOrdersCaption));
        this.OnPropertyChanged(nameof(this.OverdueTasksCaption));
        this.OnPropertyChanged(nameof(this.LowStockCaption));
        this.OnPropertyChanged(nameof(this.ScenarioCodeCaption));
        this.OnPropertyChanged(nameof(this.DocumentStyleCaption));
        this.OnPropertyChanged(nameof(this.ArchiveDateCaption));
        this.OnPropertyChanged(nameof(this.DeletionDateCaption));
    }

    private static string FormatLabel(string key, object? value) => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, AdminLocalization.Instance[key], value ?? string.Empty);

    partial void OnPageSizeChanged(int value)
    {
        if (!AvailablePageSizes.Contains(value) || value == this._appliedPageSize || !this._isActive)
        {
            return;
        }

        this._appliedPageSize = value;
        this.Page = 1;
        this.RefreshCommand.Execute(null);
    }

    private string FormatOrganizationTotal(decimal currentAmount, Func<AdminOrganizationCurrencyTotal, decimal> getAmount)
    {
        var currencyCode = this.Organization?.CurrencyCode ?? "RUB";

        var amounts = new[] { UserDisplayFormatting.Money(currentAmount, currencyCode) }
            .Concat((this.Organization?.OtherCurrencyTotals ?? [])
                .Select(total => UserDisplayFormatting.Money(getAmount(total), total.CurrencyCode)));

        return string.Join(Environment.NewLine, amounts);
    }

    private string SortLabel(string title, string sort) => this._sort == sort ? title + (this._descending ? " ↓" : " ↑") : title;

    private void NotifySortHeaders()
    {
        this.OnPropertyChanged(nameof(NameHeader));
        this.OnPropertyChanged(nameof(OwnerHeader));
        this.OnPropertyChanged(nameof(ScenarioHeader));
        this.OnPropertyChanged(nameof(StatusHeader));
        this.OnPropertyChanged(nameof(SubscriptionHeader));
        this.OnPropertyChanged(nameof(MembersHeader));
        this.OnPropertyChanged(nameof(ClientsHeader));
        this.OnPropertyChanged(nameof(VehiclesHeader));
        this.OnPropertyChanged(nameof(OrdersHeader));
        this.OnPropertyChanged(nameof(CreatedHeader));
    }

    private async Task HandleErrorAsync(ApiException exception, bool isCard)
    {
        if (!this._isActive)
        {
            return;
        }

        if (!this._sessionClient.HasSession || exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            this.SessionExpired?.Invoke();
            return;
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden && this.AccessDenied != null)
        {
            await this.AccessDenied.Invoke();
            return;
        }

        if (AdminDisplayLabels.TransportErrorKey(exception) is { } transportKey)
        {
            if (isCard)
            {
                this.SetCardErrorKey(transportKey);
            }
            else
            {
                this.SetListErrorKey(transportKey);
            }

            return;
        }

        if (isCard)
        {
            this._cardErrorTranslationKey = null;
            this.CardErrorMessage = exception.Message;
        }
        else
        {
            this._listErrorTranslationKey = null;
            this.ErrorMessage = exception.Message;
        }
    }

    private void SetListErrorKey(string key)
    {
        this.ErrorMessage = AdminLocalization.Instance[key];
        this._listErrorTranslationKey = key;
    }
}
