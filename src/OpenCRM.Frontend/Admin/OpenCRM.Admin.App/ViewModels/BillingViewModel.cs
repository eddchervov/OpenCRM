using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель представления экрана биллинга. </summary>
public partial class BillingViewModel : ObservableObject
{
    /// <summary> Культура форматирования сумм и дат. </summary>
    private static readonly CultureInfo s_culture = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary> Клиент сессии администратора. </summary>
    private readonly AdminSessionClient _sessionClient;

    /// <summary> Загруженные строки биллинга. </summary>
    private IReadOnlyList<BillingRow> _loadedRows = [];

    /// <summary> Признак активного экрана. </summary>
    private bool _isActive;

    /// <summary> Признак изменения фильтров. </summary>
    private bool _isChangingFilters;

    /// <summary> Версия запроса для отбраковки устаревших ответов. </summary>
    private int _requestVersion;

    /// <summary> Версия перезапуска запроса. </summary>
    private int _queryRestartVersion;

    /// <summary> Номер загруженной страницы списка. </summary>
    private int _loadedPage = 1;

    /// <summary> Номер страницы платежей. </summary>
    private int _paymentPage = 1;

    /// <summary> Номер страницы подписок. </summary>
    private int _subscriptionPage = 1;

    /// <summary> Общее число платежей. </summary>
    private int _paymentTotalCount;

    /// <summary> Общее число подписок. </summary>
    private int _subscriptionTotalCount;

    /// <summary> Фильтр статуса платежей. </summary>
    private string _paymentStatus = "Все";

    /// <summary> Фильтр статуса подписок. </summary>
    private string _subscriptionStatus = "Активные";

    /// <summary> Ключ сортировки платежей. </summary>
    private string _paymentSortKey = "date";

    /// <summary> Ключ сортировки подписок. </summary>
    private string _subscriptionSortKey = "date";

    /// <summary> Признак сортировки платежей по убыванию. </summary>
    private bool _paymentSortDescending = true;

    /// <summary> Признак сортировки подписок по убыванию. </summary>
    private bool _subscriptionSortDescending = true;

    /// <summary> Идентификатор открытой детализации. </summary>
    private int? _detailId;

    /// <summary> Признак того, что детализация относится к платежу. </summary>
    private bool _detailIsPayment;

    /// <summary> Признак загрузки списка. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    [NotifyCanExecuteChangedFor(nameof(ApplyFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(ResetFiltersCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(SortCommand))]
    private bool _isLoading;

    /// <summary> Признак загрузки детализации. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenDetailCommand))]
    private bool _isLoadingDetail;

    /// <summary> Признак активной вкладки платежей. </summary>
    [ObservableProperty]
    private bool _isPaymentsTab = true;

    /// <summary> Признак открытой детализации. </summary>
    [ObservableProperty]
    private bool _isDetailOpen;

    /// <summary> Текст ошибки загрузки списка. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    /// <summary> Текст ошибки загрузки детализации. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetailError))]
    private string _detailErrorMessage = string.Empty;

    /// <summary> Текущий номер страницы. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _page = 1;

    /// <summary> Общее количество записей. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyPropertyChangedFor(nameof(TotalLabel))]
    [NotifyCanExecuteChangedFor(nameof(NextPageCommand))]
    private int _totalCount;

    /// <summary> Поисковый запрос. </summary>
    [ObservableProperty]
    private string _search = string.Empty;

    /// <summary> Выбранный фильтр статуса. </summary>
    [ObservableProperty]
    private string _selectedStatus = "Все";

    /// <summary> Выбранный фильтр периода. </summary>
    [ObservableProperty]
    private string _selectedPeriod = "30 дней";

    /// <summary> Размер страницы. </summary>
    [ObservableProperty]
    private int _pageSize = 50;

    /// <summary> Строки текущей страницы списка. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRows))]
    private IReadOnlyList<BillingRow> _rows = [];

    /// <summary> Выбранная строка списка. </summary>
    [ObservableProperty]
    private BillingRow? _selectedRow;

    /// <summary> Модель детализации выбранной записи. </summary>
    [ObservableProperty]
    private BillingDetailViewModel? _detail;

    /// <summary> Доступные варианты периода. </summary>
    public IReadOnlyList<string> PeriodOptions { get; } = ["30 дней", "7 дней", "Сегодня", "За всё время"];

    /// <summary> Доступные варианты статуса платежей. </summary>
    public IReadOnlyList<string> PaymentStatusOptions { get; } =
        ["Все", "Оплачены", "Ожидают оплаты", "Отменены", "Возвраты", "Ошибка активации"];

    /// <summary> Доступные варианты статуса подписок. </summary>
    public IReadOnlyList<string> SubscriptionStatusOptions { get; } = ["Активные", "Завершённые", "Ещё не начались", "Все"];

    /// <summary> Доступные размеры страницы. </summary>
    public IReadOnlyList<int> PageSizeOptions { get; } = [25, 50, 100];

    /// <summary> Доступные варианты статуса для текущей вкладки. </summary>
    public IReadOnlyList<string> StatusOptions => this.IsPaymentsTab
        ? this.PaymentStatusOptions
        : this.SubscriptionStatusOptions;

    /// <summary> Признак наличия ошибки списка. </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    /// <summary> Признак наличия ошибки детализации. </summary>
    public bool HasDetailError => !string.IsNullOrWhiteSpace(this.DetailErrorMessage);

    /// <summary> Признак наличия строк в списке. </summary>
    public bool HasRows => this.Rows.Count > 0;

    /// <summary> Подпись текущей страницы. </summary>
    public string PageLabel => $"{this.Page} / {Math.Max(1, (int)Math.Ceiling((double)this.TotalCount / this.PageSize))}";

    /// <summary> Подпись общего количества записей. </summary>
    public string TotalLabel => $"Всего: {this.TotalCount:N0}";

    /// <summary> Формирует заголовок колонки с учётом сортировки. </summary>
    public string SortHeader(string key, string title)
    {
        var currentKey = this.IsPaymentsTab ? this._paymentSortKey : this._subscriptionSortKey;
        var descending = this.IsPaymentsTab ? this._paymentSortDescending : this._subscriptionSortDescending;

        return currentKey == key ? title + (descending ? " ↓" : " ↑") : title;
    }

    /// <summary> Заголовок колонки плательщика. </summary>
    public string CustomerHeader => this.SortHeader("customer", this.IsPaymentsTab ? "Пользователь" : "Организация");

    /// <summary> Заголовок колонки тарифа. </summary>
    public string PlanHeader => this.SortHeader("plan", "Тариф");

    /// <summary> Заголовок колонки суммы. </summary>
    public string AmountHeader => this.SortHeader(this.IsPaymentsTab ? "amount" : "source", this.IsPaymentsTab ? "Сумма" : "Источник");

    /// <summary> Заголовок колонки даты. </summary>
    public string DateHeader => this.SortHeader("date", this.IsPaymentsTab ? "Создан" : "Начало");

    /// <summary> Заголовок колонки окончания. </summary>
    public string EndHeader => this.SortHeader("expires", "Окончание");

    /// <summary> Заголовок колонки статуса. </summary>
    public string StatusHeader => this.SortHeader("status", "Статус");

    /// <summary> Уведомляет об изменении заголовков колонок. </summary>
    private void NotifyHeaders()
    {
        this.OnPropertyChanged(nameof(CustomerHeader));
        this.OnPropertyChanged(nameof(PlanHeader));
        this.OnPropertyChanged(nameof(AmountHeader));
        this.OnPropertyChanged(nameof(DateHeader));
        this.OnPropertyChanged(nameof(EndHeader));
        this.OnPropertyChanged(nameof(StatusHeader));
    }

    /// <summary> Событие истечения сессии. </summary>
    public event Action? SessionExpired;

    /// <summary> Событие отказа в доступе. </summary>
    public event Func<Task>? AccessDenied;

    /// <summary> Активирует экран биллинга и загружает данные. </summary>
    public async Task ActivateAsync()
    {
        this._isActive = true;
        this.RefreshAccess();
        if (this.IsPurchasesTab)
        {
            await this.Purchases.ActivateAsync();
            return;
        }

        this.NotifyCommands();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Останавливает обновление данных. </summary>
    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this.PaymentActions.Stop(clear);
        this.Purchases.Stop(clear);
        this.RefreshCommand.Cancel();
        this.OpenDetailCommand.Cancel();
        this.NotifyCommands();
        if (!clear)
        {
            return;
        }

        this.OperationMessage = string.Empty;
        this.IsPurchasesTab = false;
        this._requestVersion++;
        this._loadedRows = [];
        this._loadedPage = 1;
        this._detailId = null;
        this.Rows = [];
        this.SelectedRow = null;
        this.Detail = null;
        this.IsDetailOpen = false;
        this.IsLoading = false;
        this.IsLoadingDetail = false;
        this.ErrorMessage = string.Empty;
        this.DetailErrorMessage = string.Empty;
        this.Search = string.Empty;
        this.SelectedPeriod = "30 дней";
        this.PageSize = 50;
        this.Page = 1;
        this.TotalCount = 0;
        this._paymentPage = 1;
        this._subscriptionPage = 1;
        this._paymentTotalCount = 0;
        this._subscriptionTotalCount = 0;
        this._paymentStatus = "Все";
        this._subscriptionStatus = "Активные";
        this._paymentSortKey = "date";
        this._subscriptionSortKey = "date";
        this._paymentSortDescending = true;
        this._subscriptionSortDescending = true;
        this.IsPaymentsTab = true;
        this.SelectedStatus = "Все";
        this.OnPropertyChanged(nameof(StatusOptions));
        this.NotifyCommands();
    }

    /// <summary> Деактивирует экран и при необходимости очищает данные. </summary>
    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        await Task.WhenAll(this.PaymentActions.DeactivateAsync(clear), this.Purchases.DeactivateAsync(clear));
        await Task.WhenAll(new[] { this.RefreshCommand.ExecutionTask, this.OpenDetailCommand.ExecutionTask }
            .OfType<Task>());
    }

    /// <summary> Переключает вкладку платежей или подписок. </summary>
    [RelayCommand]
    private async Task SetTabAsync(bool isPaymentsTab)
    {
        if (this.IsPaymentsTab == isPaymentsTab && !this.IsPurchasesTab)
        {
            return;
        }

        var restartVersion = ++this._queryRestartVersion;
        await this.CancelRefreshAsync();
        await Task.WhenAll(this.Purchases.DeactivateAsync(), this.PaymentActions.DeactivateAsync(true));
        if (restartVersion != this._queryRestartVersion)
        {
            return;
        }

        this.OpenDetailCommand.Cancel();
        await Task.WhenAll(new[] { this.OpenDetailCommand.ExecutionTask }.OfType<Task>());
        this.IsPurchasesTab = false;
        this.IsDetailOpen = false;
        this.Detail = null;
        this._detailId = null;
        this._isChangingFilters = true;
        this.IsPaymentsTab = isPaymentsTab;
        this.ClearRows();
        this.SelectedStatus = isPaymentsTab ? this._paymentStatus : this._subscriptionStatus;
        this.Page = isPaymentsTab ? this._paymentPage : this._subscriptionPage;
        this.TotalCount = isPaymentsTab ? this._paymentTotalCount : this._subscriptionTotalCount;
        this.ErrorMessage = string.Empty;
        this._isChangingFilters = false;
        this.OnPropertyChanged(nameof(StatusOptions));
        if (this._isActive)
        {
            await this.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary> Применяет выбранные фильтры. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task ApplyFiltersAsync()
    {
        await this.RefreshWithUpdatedFiltersAsync(resetPage: true);
    }

    /// <summary> Сбрасывает фильтры к значениям по умолчанию. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task ResetFiltersAsync()
    {
        await this.CancelRefreshAsync();
        this._isChangingFilters = true;
        this.Search = string.Empty;
        this.SelectedStatus = this.IsPaymentsTab ? "Все" : "Активные";
        this.SelectedPeriod = "30 дней";
        this.PageSize = 50;
        this._isChangingFilters = false;
        await this.RefreshWithUpdatedFiltersAsync(resetPage: true, cancelPrevious: false);
    }

    /// <summary> Переходит к предыдущей странице. </summary>
    [RelayCommand(CanExecute = nameof(CanPreviousPage))]
    private async Task PreviousPageAsync()
    {
        if (!this.CanPreviousPage())
        {
            return;
        }

        this.Page--;
        this.SetCurrentTabPage();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Переходит к следующей странице. </summary>
    [RelayCommand(CanExecute = nameof(CanNextPage))]
    private async Task NextPageAsync()
    {
        if (!this.CanNextPage())
        {
            return;
        }

        this.Page++;
        this.SetCurrentTabPage();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Сортирует список по указанной колонке. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task SortAsync(string key)
    {
        if (!this.CanLoad() || !BillingViewModel.AllowedSortKeys(this.IsPaymentsTab)
            .Contains(key, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        ref var sortKey = ref (this.IsPaymentsTab ? ref this._paymentSortKey : ref this._subscriptionSortKey);
        ref var sortDescending = ref (this.IsPaymentsTab
            ? ref this._paymentSortDescending
            : ref this._subscriptionSortDescending);
        if (string.Equals(sortKey, key, StringComparison.OrdinalIgnoreCase))
        {
            sortDescending = !sortDescending;
        }
        else
        {
            sortKey = key;
            sortDescending = key is "date" or "amount" or "expires" or "end";
        }

        this.NotifyHeaders();
        await this.RefreshWithUpdatedFiltersAsync(resetPage: true);
    }

    /// <summary> Обновляет список с учётом отмены. </summary>
    [RelayCommand(CanExecute = nameof(CanLoad))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!this._isActive || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        this.IsLoading = true;
        this.ErrorMessage = string.Empty;
        var requestVersion = ++this._requestVersion;
        var isPaymentsTab = this.IsPaymentsTab;
        var page = this.Page;
        var pageSize = this.PageSize;
        var status = this.SelectedStatus;
        try
        {
            if (isPaymentsTab)
            {
                var response = await this._sessionClient.GetBillingPaymentsPageAsync(
                    this.Search.Trim(),
                    BillingViewModel.PaymentStatusCode(status),
                    BillingViewModel.PeriodDays(this.SelectedPeriod),
                    page,
                    pageSize,
                    this._paymentSortKey,
                    this._paymentSortDescending,
                    cancellationToken);
                if (!this.IsCurrentRequest(requestVersion, isPaymentsTab, cancellationToken))
                {
                    return;
                }

                this.ApplyPaymentPage(response);
            }
            else
            {
                var response = await this._sessionClient.GetBillingSubscriptionsPageAsync(
                    this.Search.Trim(),
                    BillingViewModel.SubscriptionStatusCode(status),
                    page,
                    pageSize,
                    this._subscriptionSortKey,
                    this._subscriptionSortDescending,
                    cancellationToken);
                if (!this.IsCurrentRequest(requestVersion, isPaymentsTab, cancellationToken))
                {
                    return;
                }

                this.ApplySubscriptionPage(response);
            }

            if (this.IsDetailOpen && this._detailId is int detailId)
            {
                var row = this._loadedRows.FirstOrDefault(item => item.Id == detailId && item.IsPayment == this._detailIsPayment);
                if (row == null)
                {
                    this.IsDetailOpen = false;
                    this.PaymentActions.Stop(true);
                    this.Detail = null;
                    this._detailId = null;
                }
                else
                {
                    await this.LoadDetailAsync(row, cancellationToken);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Уход со страницы или смена фильтра отменяют текущий запрос.
        }
        catch (ApiException exception)
        {
            if (this.IsCurrentRequest(requestVersion, isPaymentsTab, cancellationToken) && this._loadedRows.Count > 0)
            {
                this.Page = this._loadedPage;
                this.SetCurrentTabPage();
            }

            await this.HandleErrorAsync(exception, false);
        }
        catch (HttpRequestException)
        {
            if (this.IsCurrentRequest(requestVersion, isPaymentsTab, cancellationToken))
            {
                this.ErrorMessage = "Не удалось обновить список. Проверьте подключение к серверу.";
            }
        }
        catch (OperationCanceledException)
        {
            if (this.IsCurrentRequest(requestVersion, isPaymentsTab, cancellationToken))
            {
                this.ErrorMessage = "Не удалось обновить список: сервер не ответил вовремя.";
            }
        }
        finally
        {
            if (requestVersion == this._requestVersion)
            {
                this.IsLoading = false;
            }
        }
    }

    /// <summary> Открывает детализацию выбранной строки. </summary>
    [RelayCommand(CanExecute = nameof(CanOpenDetail))]
    private async Task OpenDetailAsync(BillingRow row, CancellationToken cancellationToken)
    {
        if (!this._isActive || !this._loadedRows.Contains(row) || row.IsPayment != this.IsPaymentsTab)
        {
            return;
        }

        await this.PaymentActions.DeactivateAsync(true);
        this.SelectedRow = row;
        this.IsDetailOpen = true;
        this.Detail = null;
        this.DetailErrorMessage = string.Empty;
        this._detailId = row.Id;
        this._detailIsPayment = row.IsPayment;
        await this.LoadDetailAsync(row, cancellationToken);
    }

    /// <summary> Возвращается к списку из детализации. </summary>
    [RelayCommand]
    private async Task BackToListAsync()
    {
        this.IsDetailOpen = false;
        await this.PaymentActions.DeactivateAsync(true);
        this.OpenDetailCommand.Cancel();
        await Task.WhenAll(new[] { this.OpenDetailCommand.ExecutionTask }.OfType<Task>());
        this.DetailErrorMessage = string.Empty;
    }

    /// <summary> Загружает детализацию выбранной записи. </summary>
    private async Task LoadDetailAsync(BillingRow row, CancellationToken cancellationToken)
    {
        this.IsLoadingDetail = true;
        this.DetailErrorMessage = string.Empty;
        try
        {
            if (row.Data is AdminBillingPayment payment)
            {
                var details = await this._sessionClient.GetBillingPaymentAsync(payment.Id, cancellationToken);
                if (!this.IsCurrentDetail(row, cancellationToken))
                {
                    return;
                }

                IReadOnlyList<AdminPaymentStatusAudit> audits = [];
                var auditError = string.Empty;
                try
                {
                    audits = await this._sessionClient.GetBillingPaymentAuditsAsync(payment.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ApiException exception)
                {
                    if (this.IsAuthorizationFailure(exception))
                    {
                        await this.HandleErrorAsync(exception, true);
                        return;
                    }

                    auditError = exception.Message;
                }

                if (this.IsCurrentDetail(row, cancellationToken))
                {
                    this.DetailErrorMessage = auditError;
                    this.Detail = BillingViewModel.CreatePaymentDetail(details, audits, auditError);
                    this.PaymentActions.SetPayment(details);
                    await this.PaymentActions.LoadServiceActionsAsync(cancellationToken);
                }

                return;
            }

            if (row.Data is AdminBillingOrganizationSubscription subscription)
            {
                IReadOnlyList<AdminSubscriptionAudit> audits = [];
                var auditError = string.Empty;
                try
                {
                    audits = await this._sessionClient.GetBillingSubscriptionAuditsAsync(subscription.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (ApiException exception)
                {
                    if (this.IsAuthorizationFailure(exception))
                    {
                        await this.HandleErrorAsync(exception, true);
                        return;
                    }

                    auditError = exception.Message;
                }

                if (this.IsCurrentDetail(row, cancellationToken))
                {
                    this.DetailErrorMessage = auditError;
                    this.Detail = BillingViewModel.CreateSubscriptionDetail(subscription, audits, auditError);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Возврат в список и уход со страницы отменяют загрузку подробностей.
        }
        catch (ApiException exception)
        {
            await this.HandleErrorAsync(exception, true);
        }
        catch (HttpRequestException)
        {
            if (this.IsCurrentDetail(row, cancellationToken))
            {
                this.DetailErrorMessage = "Не удалось обновить карточку. Проверьте подключение к серверу.";
            }
        }
        catch (OperationCanceledException)
        {
            if (this.IsCurrentDetail(row, cancellationToken))
            {
                this.DetailErrorMessage = "Не удалось обновить карточку: сервер не ответил вовремя.";
            }
        }
        finally
        {
            this.IsLoadingDetail = false;
        }
    }

    /// <summary> Применяет страницу платежей к модели. </summary>
    private void ApplyPaymentPage(AdminBillingPaymentsPage response)
    {
        this._loadedRows = response.Items.Select(payment => new BillingRow(payment)).ToArray();
        this.Rows = this._loadedRows;
        this.Page = Math.Max(1, response.Page);
        this._loadedPage = this.Page;
        this.TotalCount = response.TotalCount;
        this._paymentPage = this.Page;
        this._paymentTotalCount = this.TotalCount;
        this.PreserveSelectedRow();
    }

    /// <summary> Применяет страницу подписок к модели. </summary>
    private void ApplySubscriptionPage(AdminBillingSubscriptionsPage response)
    {
        this._loadedRows = response.Items.Select(subscription => new BillingRow(subscription)).ToArray();
        this.Rows = this._loadedRows;
        this.Page = Math.Max(1, response.Page);
        this._loadedPage = this.Page;
        this.TotalCount = response.TotalCount;
        this._subscriptionPage = this.Page;
        this._subscriptionTotalCount = this.TotalCount;
        this.PreserveSelectedRow();
    }

    /// <summary> Очищает строки списка. </summary>
    private void ClearRows()
    {
        this._loadedRows = [];
        this.Rows = [];
        this.SelectedRow = null;
    }

    /// <summary> Сохраняет выбор текущей строки при обновлении. </summary>
    private void PreserveSelectedRow()
    {
        var selectedId = this.SelectedRow?.Id;
        var selectedType = this.SelectedRow?.IsPayment;
        this.SelectedRow = this._loadedRows.FirstOrDefault(row => row.Id == selectedId && row.IsPayment == selectedType);
    }

    /// <summary> Перезапускает загрузку с обновлёнными фильтрами. </summary>
    private async Task RefreshWithUpdatedFiltersAsync(
        bool resetPage,
        bool cancelPrevious = true,
        int? restartVersion = null)
    {
        if (cancelPrevious)
        {
            await this.CancelRefreshAsync();
        }

        if (restartVersion != null && restartVersion != this._queryRestartVersion)
        {
            return;
        }

        if (resetPage)
        {
            this.ClearRows();
            this.TotalCount = 0;
            this.Page = 1;
            this.SetCurrentTabPage();
        }

        if (this._isActive)
        {
            await this.RefreshCommand.ExecuteAsync(null);
        }
    }

    /// <summary> Отменяет текущую загрузку списка. </summary>
    private async Task CancelRefreshAsync()
    {
        this.RefreshCommand.Cancel();
        var runningTask = this.RefreshCommand.ExecutionTask;
        if (runningTask != null)
        {
            await runningTask;
        }
    }

    /// <summary> Перезапускает загрузку после смены фильтра. </summary>
    private void RestartForChangedFilter()
    {
        if (this._isChangingFilters || !this._isActive)
        {
            return;
        }

        if (this.IsPaymentsTab)
        {
            this._paymentStatus = this.SelectedStatus;
        }
        else
        {
            this._subscriptionStatus = this.SelectedStatus;
        }

        var restartVersion = ++this._queryRestartVersion;
        _ = this.RefreshWithUpdatedFiltersAsync(resetPage: true, restartVersion: restartVersion);
    }

    /// <summary> Устанавливает данные текущей вкладки. </summary>
    private void SetCurrentTabPage()
    {
        if (this.IsPaymentsTab)
        {
            this._paymentPage = this.Page;
        }
        else
        {
            this._subscriptionPage = this.Page;
        }
    }

    /// <summary> Проверяет актуальность запроса списка. </summary>
    private bool IsCurrentRequest(int requestVersion, bool isPaymentsTab, CancellationToken cancellationToken) =>
        this._isActive && requestVersion == this._requestVersion && isPaymentsTab == this.IsPaymentsTab
        && !cancellationToken.IsCancellationRequested;

    /// <summary> Проверяет актуальность запроса детализации. </summary>
    private bool IsCurrentDetail(BillingRow row, CancellationToken cancellationToken) =>
        this._isActive && this.IsDetailOpen && !cancellationToken.IsCancellationRequested
        && this._detailId == row.Id && this._detailIsPayment == row.IsPayment;

    /// <summary> Возвращает код статуса платежа. </summary>
    private static string PaymentStatusCode(string status) => status switch
    {
        "Оплачены" => "success",
        "Ожидают оплаты" => "pending",
        "Отменены" => "canceled",
        "Возвраты" => "refunded",
        "Ошибка активации" => "activationfailed",
        _ => "all"
    };

    /// <summary> Возвращает код статуса подписки. </summary>
    private static string SubscriptionStatusCode(string status) => status switch
    {
        "Завершённые" => "expired",
        "Ещё не начались" => "scheduled",
        "Все" => "all",
        _ => "active"
    };

    /// <summary> Возвращает длительность периода в днях. </summary>
    private static int PeriodDays(string period) => period switch
    {
        "Сегодня" => 1,
        "7 дней" => 7,
        "За всё время" => 0,
        _ => 30
    };

    /// <summary> Возвращает допустимые ключи сортировки для вкладки. </summary>
    private static IReadOnlyList<string> AllowedSortKeys(bool isPaymentsTab) => isPaymentsTab
        ? ["date", "customer", "plan", "amount", "status"]
        : ["date", "start", "customer", "organization", "plan", "status", "expires", "end", "source"];

    /// <summary> Создаёт модель детализации платежа с аудитом. </summary>
    private static BillingDetailViewModel CreatePaymentDetail(
        AdminBillingPayment payment,
        IReadOnlyList<AdminPaymentStatusAudit> audits,
        string auditError)
    {
        if (payment.PaymentProviderCode == "payments-service")
        {
            return BillingViewModel.CreateServicePaymentDetail(payment, auditError);
        }

        var fields = new List<DashboardMetric>
        {
            new("Пользователь", UserDisplayFormatting.Text(payment.User)),
            new("Email", UserDisplayFormatting.Text(payment.UserEmail)),
            new("Телефон", UserDisplayFormatting.Text(payment.UserPhone)),
            new("Тариф", UserDisplayFormatting.Text(payment.PlanName)),
            new("Срок тарифа", $"{payment.PlanDurationDays:N0} дн."),
            new("Сумма", UserDisplayFormatting.Money(payment.Amount)),
            new("Исходная сумма", payment.OriginalAmount is decimal originalAmount ? UserDisplayFormatting.Money(originalAmount) : "—"),
            new("Скидка", payment.PromoDiscountAmount is decimal discount ? UserDisplayFormatting.Money(discount) : "—"),
            new("Промокод", UserDisplayFormatting.Text(payment.PromoCodeSnapshot)),
            new("Способ оплаты", UserDisplayFormatting.Text(payment.PaymentMethodName)),
            new("Создан", UserDisplayFormatting.DateTime(payment.CreatedAt)),
            new("Статус обновлён", UserDisplayFormatting.DateTime(payment.StatusUpdatedAt)),
            new("Статус", UserDisplayFormatting.Text(payment.StatusName)),
            new("Внешний статус", UserDisplayFormatting.Text(payment.PaymentStatus)),
            new("ID транзакции", UserDisplayFormatting.Text(payment.TransactionId)),
            new("ID провайдера", UserDisplayFormatting.Text(payment.ExternalPaymentId))
        };
        var auditGroups = audits.Select(audit => new DashboardStatisticGroup(
            $"{UserDisplayFormatting.Text(audit.PreviousStatus)} → {UserDisplayFormatting.Text(audit.NewStatus)}",
            [
                new("Дата", UserDisplayFormatting.DateTime(audit.ChangedAt)),
                new("Источник", UserDisplayFormatting.Text(audit.Source)),
                new("Кто изменил", UserDisplayFormatting.Text(audit.ChangedBy)),
                new("Причина", UserDisplayFormatting.Text(audit.Reason))
            ])).ToArray();

        return new BillingDetailViewModel(payment.PlanName, $"Платёж № {payment.Id}", fields, auditGroups, auditError);
    }

    /// <summary> Разделяет подтверждённые деньги, исторический доступ и технические события покупки. </summary>
    private static BillingDetailViewModel CreateServicePaymentDetail(AdminBillingPayment payment, string auditError)
    {
        var confirmed = payment.PaidAt.HasValue || payment.ProviderStatus == "Succeeded";
        var granted = payment.AppliedAt.HasValue || payment.ApplicationStatus == "applied";
        var fullyRefunded = payment.RefundedAmount >= payment.Amount && payment.RefundedAmount > 0;
        var access = granted ? "Период по этой покупке выдан"
            : fullyRefunded ? "Не выдан: средства возвращены"
            : payment.ApplicationStatus == "needs_attention" || payment.RefundedAmount > 0 ? "Выдача требует проверки"
            : confirmed ? "Ожидает выдачи" : "Не выдан";
        var serviceFields = new List<DashboardMetric>
        {
            new("Покупатель", UserDisplayFormatting.Text(payment.User)),
            new("Организация", UserDisplayFormatting.Text(payment.OrganizationName)),
            new("Тариф", payment.PlanName),
            new("Сумма покупки", UserDisplayFormatting.Money(payment.Amount)),
            new("Оплата подтверждена", confirmed ? UserDisplayFormatting.Money(payment.Amount) : "Пока нет"),
            new("Возвращено всего", UserDisplayFormatting.Money(payment.RefundedAmount)),
            new("Осталось после возвратов", confirmed ? UserDisplayFormatting.Money(Math.Max(0, payment.Amount - payment.RefundedAmount)) : "—"),
            new("Доступ по этой покупке", access),
            new("Выданный период", granted ? UserDisplayFormatting.DateTime(payment.GrantedFrom) + " — " + UserDisplayFormatting.DateTime(payment.GrantedUntil) : "—"),
            new("О текущем доступе", "Исторический период покупки не показывает текущий доступ. Проверьте подписку получателя отдельно."),
            new("ID платежа ЮKassa", UserDisplayFormatting.Text(payment.ExternalPaymentId)),
            new("Создан", UserDisplayFormatting.DateTime(payment.CreatedAt)),
            new("Оплачен", UserDisplayFormatting.DateTime(payment.PaidAt)),
            new("Тестовый платёж", payment.IsTest == true ? "Да" : payment.IsTest == false ? "Нет" : "Нет данных")
        };
        var technical = new List<DashboardStatisticGroup>
        {
            new("Идентификаторы и состояние", [
                new("ID покупки", payment.PurchaseId?.ToString("D") ?? "—"),
                new("Состояние провайдера", UserDisplayFormatting.Text(payment.ProviderStatus)),
                new("Применение доступа", UserDisplayFormatting.Text(payment.ApplicationStatus)),
                new("Последний код ошибки", UserDisplayFormatting.Text(payment.LastErrorCode))
            ])
        };
        foreach (var item in payment.ProviderEvents)
        {
            var stage = item.Stage switch
            {
                "access_application" => "Выдача доступа",
                "service_reconciliation" => "Сверка платежа",
                "attention" or "inbox_conflict" => "Требуется разбор",
                "admin_action" => "Действие администратора",
                "command_delivery" => "Доставка команды",
                "state" => "Денежный снимок",
                _ when item.Stage.StartsWith("refund_", StringComparison.Ordinal) => "Обработка возврата",
                _ => "Обработка платежа"
            };
            technical.Add(new(stage, [
                new("Дата", UserDisplayFormatting.DateTime(item.CreatedAt)),
                new("Этап", item.Stage),
                new("Код", item.Code),
                new("ID события", item.Id.ToString("D")),
                new("ID команды", item.CommandId?.ToString("D") ?? "—"),
                new("Возвращено всего", item.RefundedAmountMinor.HasValue ? UserDisplayFormatting.Money(item.RefundedAmountMinor.Value / 100m) : "—")
            ]));
        }

        return new BillingDetailViewModel(payment.PlanName, $"Платёж № {payment.Id}", serviceFields, technical, auditError, "Последние технические события (до 20)");
    }

    /// <summary> Создаёт модель детализации подписки с аудитом. </summary>
    private static BillingDetailViewModel CreateSubscriptionDetail(
        AdminBillingOrganizationSubscription subscription,
        IReadOnlyList<AdminSubscriptionAudit> audits,
        string auditError)
    {
        var fields = new List<DashboardMetric>
        {
            new("Организация", UserDisplayFormatting.Text(subscription.OrganizationName)),
            new("Владелец", UserDisplayFormatting.Text(subscription.Owner)),
            new("Тариф", UserDisplayFormatting.Text(subscription.PlanName)),
            new("Тип тарифа", UserDisplayFormatting.Text(subscription.PlanTypeCode)),
            new("Источник", UserDisplayFormatting.Text(subscription.Source)),
            new("Начало", UserDisplayFormatting.DateTime(subscription.StartDate)),
            new("Окончание", UserDisplayFormatting.DateTime(subscription.EndDate)),
            new("Осталось дней", Math.Max(0, subscription.DaysLeft).ToString("N0", s_culture)),
            new("Автопродление", subscription.AutoRenewal ? "Да" : "Нет"),
            new("Статус", BillingRow.SubscriptionStatus(subscription))
        };
        var auditGroups = audits.Select(audit => new DashboardStatisticGroup(
            UserDisplayFormatting.Text(audit.Action),
            [
                new("Дата", UserDisplayFormatting.DateTime(audit.ChangedAt)),
                new("Кто изменил", UserDisplayFormatting.Text(audit.ChangedBy)),
                new("Окончание до", UserDisplayFormatting.DateTime(audit.PreviousEndDate)),
                new("Окончание после", UserDisplayFormatting.DateTime(audit.NewEndDate)),
                new("Причина", UserDisplayFormatting.Text(audit.Reason))
            ])).ToArray();

        return new BillingDetailViewModel(subscription.OrganizationName,
            $"Подписка организации № {subscription.Id}", fields, auditGroups, auditError);
    }

    /// <summary> Определяет доступность загрузки данных. </summary>
    private bool CanLoad() => this._isActive && !this.IsLoading && !this.PaymentActions.IsBusy && !this.IsPurchasesTab;

    /// <summary> Определяет доступность открытия детализации. </summary>
    private bool CanOpenDetail(BillingRow? row) => this._isActive && row != null && !this.IsLoadingDetail
        && !this.PaymentActions.IsBusy && !this.IsPurchasesTab;

    /// <summary> Определяет доступность перехода к предыдущей странице. </summary>
    private bool CanPreviousPage() => this.CanLoad() && this.Page > 1;

    /// <summary> Определяет доступность перехода к следующей странице. </summary>
    private bool CanNextPage() => this.CanLoad() && this.Page * this.PageSize < this.TotalCount;

    /// <summary> Обновляет состояние команд. </summary>
    private void NotifyCommands()
    {
        this.RefreshCommand.NotifyCanExecuteChanged();
        this.ApplyFiltersCommand.NotifyCanExecuteChanged();
        this.ResetFiltersCommand.NotifyCanExecuteChanged();
        this.PreviousPageCommand.NotifyCanExecuteChanged();
        this.NextPageCommand.NotifyCanExecuteChanged();
        this.SortCommand.NotifyCanExecuteChanged();
        this.OpenDetailCommand.NotifyCanExecuteChanged();
    }

    /// <summary> Определяет, связана ли ошибка с авторизацией. </summary>
    private bool IsAuthorizationFailure(ApiException exception) =>
        !this._sessionClient.HasSession || exception.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

    /// <summary> Обрабатывает ошибку API и показывает сообщение. </summary>
    private async Task HandleErrorAsync(ApiException exception, bool isDetail)
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

        if (isDetail)
        {
            this.DetailErrorMessage = exception.Message;
            return;
        }

        this.ErrorMessage = exception.StatusCode == HttpStatusCode.NotFound
            ? "Сервер не поддерживает постраничную загрузку биллинга. Требуется обновление сервера."
            : this.Rows.Count == 0
                ? exception.Message
                : $"Не удалось обновить список. Показана предыдущая страница. {exception.Message}";
    }

    /// <summary> Реагирует на смену вкладки платежей. </summary>
    partial void OnIsPaymentsTabChanged(bool value)
    {
        this.NotifyHeaders();
        this.OnPropertyChanged(nameof(IsPaymentTabSelected));
        this.OnPropertyChanged(nameof(IsSubscriptionTabSelected));
        this.OnPropertyChanged(nameof(StatusOptions));
        if (this._isChangingFilters || !this._isActive)
        {
            return;
        }

        var restartVersion = ++this._queryRestartVersion;
        this.OpenDetailCommand.Cancel();
        this.IsDetailOpen = false;
        this.Detail = null;
        this._detailId = null;
        this.ClearRows();
        this._isChangingFilters = true;
        this.SelectedStatus = value ? this._paymentStatus : this._subscriptionStatus;
        this.Page = value ? this._paymentPage : this._subscriptionPage;
        this.TotalCount = value ? this._paymentTotalCount : this._subscriptionTotalCount;
        this._isChangingFilters = false;
        _ = this.RefreshWithUpdatedFiltersAsync(resetPage: false, restartVersion: restartVersion);
    }

    /// <summary> Реагирует на смену фильтра статуса. </summary>
    partial void OnSelectedStatusChanged(string value)
    {
        this.RestartForChangedFilter();
    }

    /// <summary> Реагирует на смену фильтра периода. </summary>
    partial void OnSelectedPeriodChanged(string value)
    {
        if (this.IsPaymentsTab)
        {
            this.RestartForChangedFilter();
        }
    }

    /// <summary> Реагирует на смену размера страницы. </summary>
    partial void OnPageSizeChanged(int value)
    {
        if (this.PageSizeOptions.Contains(value))
        {
            this.RestartForChangedFilter();
        }
    }
}
