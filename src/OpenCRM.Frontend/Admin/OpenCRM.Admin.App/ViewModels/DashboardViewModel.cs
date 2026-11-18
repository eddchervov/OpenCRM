using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Модель представления дашборда администратора. </summary>
public partial class DashboardViewModel(AdminSessionClient sessionClient, int refreshSeconds = 60) : ObservableObject
{
    /// <summary> Клиент административной сессии. </summary>
    private readonly AdminSessionClient _sessionClient = sessionClient;

    /// <summary> Признак активности экрана. </summary>
    private bool _isActive;

    /// <summary> Доступ к разделу биллинга. </summary>
    private bool _canViewBilling;

    /// <summary> Загруженный период основного дашборда. </summary>
    private int? _loadedPeriod;

    /// <summary> Данные основного дашборда. </summary>
    private AdminDashboard? _dashboard;

    /// <summary> Данные статистики дашборда. </summary>
    private AdminDashboard? _statisticsDashboard;

    private DateTimeOffset? _updatedAt;
    private DateTimeOffset? _statisticsUpdatedAt;
    private string? _errorMessageTranslationKey;
    private string? _errorMessageDetails;
    private string? _statisticsErrorTranslationKey;
    private string? _statisticsErrorDetails;
    private string? _billingErrorTranslationKey;
    private string? _billingErrorDetails;

    /// <summary> Выбранный период в днях. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsToday))]
    [NotifyPropertyChangedFor(nameof(IsWeek))]
    [NotifyPropertyChangedFor(nameof(IsMonth))]
    private int _periodDays = 1;

    /// <summary> Интервал обновления в секундах. </summary>
    [ObservableProperty]
    private int _refreshSeconds = refreshSeconds is 5 or 10 or 30 or 60 ? refreshSeconds : 60;

    /// <summary> Признак загрузки дашборда. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isLoading;

    /// <summary> Текст ошибки дашборда. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    /// <summary> Подпись времени обновления. </summary>
    [ObservableProperty]
    private string _updatedLabel = string.Empty;

    /// <summary> Метрики регистраций. </summary>
    [ObservableProperty]
    private IReadOnlyList<DashboardMetric> _registrationMetrics = [];

    /// <summary> Метрики платежей. </summary>
    [ObservableProperty]
    private IReadOnlyList<DashboardMetric> _paymentMetrics = [];

    /// <summary> Количество неактивированных подписок. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUrgentPayments))]
    [NotifyPropertyChangedFor(nameof(UrgentPaymentMessage))]
    private int _failedActivationCount;

    /// <summary> Количество записей без чека. </summary>
    private int? _missingReceiptCount;

    /// <summary> Количество просроченных чеков. </summary>
    private int? _overdueReceiptCount;

    /// <summary> Текст ошибки биллинга. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBillingError))]
    private string _billingErrorMessage = string.Empty;

    /// <summary> Признак загрузки статистики. </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadStatisticsCommand))]
    private bool _isLoadingStatistics;

    /// <summary> Подпись времени обновления статистики. </summary>
    [ObservableProperty]
    private string _statisticsUpdatedLabel = string.Empty;

    /// <summary> Текст ошибки статистики. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatisticsError))]
    private string _statisticsErrorMessage = string.Empty;

    /// <summary> Группы статистики. </summary>
    [ObservableProperty]
    private IReadOnlyList<DashboardStatisticGroup> _statisticGroups = [];

    /// <summary> Серии графика активности. </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrowthSeries))]
    private IReadOnlyList<DashboardChartSeries> _activitySeries = [];

    /// <summary> Серии графика платежей. </summary>
    [ObservableProperty]
    private IReadOnlyList<DashboardChartSeries> _paymentSeries = [];

    /// <summary> Событие истечения сессии. </summary>
    public event Action? SessionExpired;

    /// <summary> Событие отказа в доступе. </summary>
    public event Func<Task>? AccessDenied;

    /// <summary> Событие отказа в доступе к биллингу. </summary>
    public event Func<Task>? BillingAccessDenied;

    /// <summary> Доступные интервалы обновления. </summary>
    public IReadOnlyList<int> RefreshIntervals { get; } = [5, 10, 30, 60];

    /// <summary> Доступ к разделу биллинга. </summary>
    public bool CanViewBilling => this._canViewBilling;

    /// <summary> Признак наличия срочных платежей. </summary>
    public bool HasUrgentPayments => this.CanViewBilling && this.FailedActivationCount > 0;

    /// <summary> Сообщение о срочных платежах. </summary>
    public string UrgentPaymentMessage => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        AdminLocalization.Instance["core.dashboard.urgentPayment"], this.FailedActivationCount);

    /// <summary> Признак наличия ошибки биллинга. </summary>
    public bool HasBillingError => !string.IsNullOrWhiteSpace(this.BillingErrorMessage);

    /// <summary> Серии графика роста. </summary>
    public IReadOnlyList<DashboardChartSeries> GrowthSeries => this.ActivitySeries.Take(1).ToArray();

    /// <summary> Подпись выбранного периода. </summary>
    public string PeriodLabel => this.PeriodDays switch
    {
        1 => AdminLocalization.Instance["core.dashboard.period.today"],
        7 => AdminLocalization.Instance["core.dashboard.period.week"],
        _ => AdminLocalization.Instance["core.dashboard.period.month"]
    };

    /// <summary> Признак наличия ошибки статистики. </summary>
    public bool HasStatisticsError => !string.IsNullOrWhiteSpace(this.StatisticsErrorMessage);

    /// <summary> Признак наличия ошибки дашборда. </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(this.ErrorMessage);

    /// <summary> Признак выбора периода «сегодня». </summary>
    public bool IsToday => this.PeriodDays == 1;

    /// <summary> Признак выбора периода «неделя». </summary>
    public bool IsWeek => this.PeriodDays == 7;

    /// <summary> Признак выбора периода «месяц». </summary>
    public bool IsMonth => this.PeriodDays == 30;

    /// <summary> Подпись периода графиков. </summary>
    public string ChartPeriodLabel => this.PeriodDays switch
    {
        1 => AdminLocalization.Instance["core.dashboard.chartPeriod.today"],
        7 => AdminLocalization.Instance["core.dashboard.chartPeriod.week"],
        _ => AdminLocalization.Instance["core.dashboard.chartPeriod.month"]
    };

    /// <summary> Активирует дашборд и загружает данные. </summary>
    public async Task ActivateAsync(bool canViewBilling)
    {
        this._isActive = true;
        this.SetBillingAccess(canViewBilling);
        this.LoadStatisticsCommand.NotifyCanExecuteChanged();
        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Устанавливает доступ к разделу биллинга. </summary>
    public void SetBillingAccess(bool canViewBilling)
    {
        this._canViewBilling = canViewBilling;
        this.OnPropertyChanged(nameof(CanViewBilling));
        this.OnPropertyChanged(nameof(HasUrgentPayments));
        if (!canViewBilling)
        {
            this.FailedActivationCount = 0;
            this._missingReceiptCount = null;
            this._overdueReceiptCount = null;
            this.PaymentMetrics = [];
            this.ClearBillingError();
        }
        if (this._dashboard != null)
        {
            this.ApplyDashboard(this._dashboard);
        }
        else
        {
            this.UpdatePaymentMetrics();
        }

        if (this._statisticsDashboard != null)
        {
            this.ApplyStatistics(this._statisticsDashboard);
        }
    }

    public void RefreshLocalizedValues()
    {
        if (this._updatedAt is { } updatedAt)
        {
            this.UpdatedLabel = DashboardViewModel.FormatTimestamp("core.dashboard.updatedAt", updatedAt);
        }

        if (this._statisticsUpdatedAt is { } statisticsUpdatedAt)
        {
            this.StatisticsUpdatedLabel = DashboardViewModel.FormatTimestamp("core.dashboard.loadedAt", statisticsUpdatedAt);
        }

        DashboardViewModel.RefreshLocalizedError(
            this._errorMessageTranslationKey,
            this._errorMessageDetails,
            value => this.ErrorMessage = value);
        DashboardViewModel.RefreshLocalizedError(
            this._statisticsErrorTranslationKey,
            this._statisticsErrorDetails,
            value => this.StatisticsErrorMessage = value);
        DashboardViewModel.RefreshLocalizedError(
            this._billingErrorTranslationKey,
            this._billingErrorDetails,
            value => this.BillingErrorMessage = value);

        if (this._dashboard != null)
        {
            this.ApplyDashboard(this._dashboard);
        }

        if (this._statisticsDashboard != null)
        {
            this.ApplyStatistics(this._statisticsDashboard);
        }

        this.UpdatePaymentMetrics();
        this.OnPropertyChanged(nameof(this.UrgentPaymentMessage));
        this.OnPropertyChanged(nameof(this.PeriodLabel));
        this.OnPropertyChanged(nameof(this.ChartPeriodLabel));
    }

    /// <summary> Деактивирует дашборд и при необходимости очищает данные. </summary>
    public async Task DeactivateAsync(bool clear = false)
    {
        this.Stop(clear);
        var operations = new[] { this.RefreshCommand.ExecutionTask, this.LoadStatisticsCommand.ExecutionTask }.OfType<Task>();
        await Task.WhenAll(operations);
    }

    /// <summary> Останавливает обновление дашборда. </summary>
    public void Stop(bool clear = false)
    {
        this._isActive = false;
        this.RefreshCommand.Cancel();
        this.LoadStatisticsCommand.Cancel();
        this.LoadStatisticsCommand.NotifyCanExecuteChanged();
        this.RefreshCommand.NotifyCanExecuteChanged();
        if (clear)
        {
            this._dashboard = null;
            this._statisticsDashboard = null;
            this._updatedAt = null;
            this._statisticsUpdatedAt = null;
            this.StatisticsUpdatedLabel = string.Empty;
            this.ClearStatisticsError();
            this._loadedPeriod = null;
            this.RegistrationMetrics = [];
            this.PaymentMetrics = [];
            this.FailedActivationCount = 0;
            this._missingReceiptCount = null;
            this._overdueReceiptCount = null;
            this.ClearBillingError();
            this.StatisticGroups = [];
            this.ActivitySeries = [];
            this.PaymentSeries = [];
            this.UpdatedLabel = string.Empty;
            this.ClearDashboardError();
        }
    }

    /// <summary> Определяет доступность загрузки статистики. </summary>
    private bool CanLoadStatistics() => this._isActive && !this.IsLoadingStatistics;

    /// <summary> Загружает статистику дашборда. </summary>
    [RelayCommand(CanExecute = nameof(CanLoadStatistics))]
    private async Task LoadStatisticsAsync(CancellationToken cancellationToken)
    {
        this.IsLoadingStatistics = true;
        try
        {
            var dashboard = await this._sessionClient.GetDashboardAsync(this.PeriodDays, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            this._statisticsDashboard = dashboard;
            this.ApplyStatistics(dashboard);
            this._statisticsUpdatedAt = dashboard.GeneratedAt;
            this.StatisticsUpdatedLabel = DashboardViewModel.FormatTimestamp("core.dashboard.loadedAt", dashboard.GeneratedAt);
            this.ClearStatisticsError();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Уход со страницы отменяет ручную загрузку статистики.
        }
        catch (ApiException exception)
        {
            if (!this._isActive)
            {
                return;
            }

            await this.HandleApiErrorAsync(exception);
            if (this._isActive)
            {
                var transportKey = AdminDisplayLabels.TransportErrorKey(exception);
                this.SetStatisticsError(
                    transportKey ?? (this._statisticsDashboard == null
                        ? "core.dashboard.statisticsLoadFailed"
                        : "core.dashboard.statisticsRefreshFailed"),
                    transportKey == null ? exception.Message : string.Empty);
            }
        }
        finally
        {
            this.IsLoadingStatistics = false;
        }
    }

    /// <summary> Определяет доступность обновления дашборда. </summary>
    private bool CanRefresh() => this._isActive && !this.IsLoading;

    /// <summary> Обновляет данные дашборда. </summary>
    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        this.IsLoading = true;
        try
        {
            await Task.WhenAll(this.LoadDashboardAsync(cancellationToken), this.LoadBillingAsync(cancellationToken));
        }
        finally
        {
            this.IsLoading = false;
        }
    }

    /// <summary> Загружает данные дашборда. </summary>
    private async Task LoadDashboardAsync(CancellationToken cancellationToken)
    {
        var period = this.PeriodDays;
        try
        {
            var dashboard = await this._sessionClient.GetDashboardAsync(period, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested || period != this.PeriodDays)
            {
                return;
            }

            this._dashboard = dashboard;
            this._loadedPeriod = period;
            this.ApplyDashboard(dashboard);
            this._updatedAt = dashboard.GeneratedAt;
            this.UpdatedLabel = DashboardViewModel.FormatTimestamp("core.dashboard.updatedAt", dashboard.GeneratedAt);
            this.ClearDashboardError();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Уход со страницы и смена периода отменяют запрос без сообщения об ошибке.
        }
        catch (ApiException exception)
        {
            if (!this._isActive)
            {
                return;
            }

            await this.HandleApiErrorAsync(exception);
            if (!this._isActive)
            {
                return;
            }

            var transportKey = AdminDisplayLabels.TransportErrorKey(exception);
            this.SetDashboardError(
                transportKey ?? (this._loadedPeriod == period
                    ? "core.dashboard.refreshFailed"
                    : "core.dashboard.loadFailed"),
                transportKey == null ? exception.Message : string.Empty);
        }
    }

    /// <summary> Загружает данные биллинга. </summary>
    private async Task LoadBillingAsync(CancellationToken cancellationToken)
    {
        if (!this.CanViewBilling)
        {
            return;
        }

        var period = this.PeriodDays;
        try
        {
            var attention = await this._sessionClient.GetBillingAttentionAsync(period, cancellationToken);
            if (!this._isActive || cancellationToken.IsCancellationRequested || !this.CanViewBilling || period != this.PeriodDays)
            {
                return;
            }

            this.FailedActivationCount = attention.FailedActivationCount;
            if (attention.PeriodDays != period || attention.MissingReceiptCount == null)
            {
                this._missingReceiptCount = null;
                this._overdueReceiptCount = null;
                this.SetBillingError("core.dashboard.receiptsUnsupported");
                this.UpdatePaymentMetrics();

                return;
            }

            this._missingReceiptCount = attention.MissingReceiptCount;
            this._overdueReceiptCount = attention.OverdueReceiptCount;
            this.ClearBillingError();
            this.UpdatePaymentMetrics();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Смена периода и уход со страницы не должны заменять актуальные счётчики поздним ответом.
        }
        catch (ApiException exception)
        {
            if (await this.HandleBillingErrorAsync(exception))
            {
                var transportKey = AdminDisplayLabels.TransportErrorKey(exception);
                this.SetBillingError(
                    transportKey ?? (this._missingReceiptCount.HasValue
                        ? "core.dashboard.paymentsRefreshFailed"
                        : "core.dashboard.paymentsLoadFailed"),
                    transportKey == null ? exception.Message : string.Empty);
            }
        }
    }

    /// <summary> Обрабатывает ошибку API биллинга. </summary>
    private async Task<bool> HandleBillingErrorAsync(ApiException exception)
    {
        if (!this._isActive)
        {
            return false;
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            this.SetBillingAccess(false);
            if (this.BillingAccessDenied != null)
            {
                await this.BillingAccessDenied.Invoke();
            }
        }
        else if (!this._sessionClient.HasSession || exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            await this.HandleApiErrorAsync(exception);
        }

        return this._isActive && this.CanViewBilling;
    }

    /// <summary> Устанавливает период дашборда в днях. </summary>
    [RelayCommand]
    private async Task SetPeriodAsync(int days)
    {
        if (days is not (1 or 7 or 30) || days == this.PeriodDays)
        {
            return;
        }

        this.RefreshCommand.Cancel();
        if (this.RefreshCommand.ExecutionTask is { } operation)
        {
            await operation;
        }

        if (!this._isActive)
        {
            return;
        }

        this.PeriodDays = days;
        this._loadedPeriod = null;
        this._missingReceiptCount = null;
        this._overdueReceiptCount = null;
        this.ClearBillingError();
        this._updatedAt = null;
        this.UpdatedLabel = string.Empty;
        this.ClearDashboardError();
        this.OnPropertyChanged(nameof(ChartPeriodLabel));
        this.OnPropertyChanged(nameof(PeriodLabel));
        this.RegistrationMetrics = [];
        this.ActivitySeries = [];
        this.PaymentSeries = [];
        if (this._dashboard != null)
        {
            this.ApplyDashboard(this._dashboard);
        }

        await this.RefreshCommand.ExecuteAsync(null);
    }

    /// <summary> Обрабатывает ошибку API. </summary>
    private async Task HandleApiErrorAsync(ApiException exception)
    {
        if (!this._sessionClient.HasSession || exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            this.SessionExpired?.Invoke();
            return;
        }

        if (exception.StatusCode == HttpStatusCode.Forbidden && this.AccessDenied != null)
        {
            await this.AccessDenied.Invoke();
        }
    }

    /// <summary> Применяет данные дашборда к модели. </summary>
    private void ApplyDashboard(AdminDashboard dashboard)
    {
        var period = dashboard.Period;
        var hasCurrentPeriod = this._loadedPeriod == this.PeriodDays;


        this.UpdatePaymentMetrics();
        this.RegistrationMetrics = hasCurrentPeriod ? [
            new(AdminLocalization.Instance["core.dashboard.newUsers"], DashboardFormatting.Value(period.NewUsers), AdminLocalization.Instance["core.dashboard.registrationsForPeriod"]),
            new(AdminLocalization.Instance["core.dashboard.newOrganizations"], DashboardFormatting.Value(period.NewOrganizations), AdminLocalization.Instance["core.dashboard.createdForPeriod"])
        ] : [];
        var charts = dashboard.Charts;
        this.ActivitySeries = hasCurrentPeriod ? [
            DashboardViewModel.CountSeries(AdminLocalization.Instance["core.dashboard.registrations"], "#3478F6", charts.Registrations),
            DashboardViewModel.CountSeries("Email", "#E67E35", charts.Emails),
            DashboardViewModel.CountSeries(AdminLocalization.Instance["core.dashboard.maintenance"], "#9974E5", charts.Maintenances)] : [];

        this.PaymentSeries = this.CanViewBilling && hasCurrentPeriod
            ? [new(AdminLocalization.Instance["core.dashboard.payments"], "#1B9368", "money", charts.Payments.Select(point =>
                new DashboardChartPoint(point.Label, (double)point.Value)).ToArray())]
            : [];
    }

    /// <summary> Применяет данные статистики к модели. </summary>
    private void ApplyStatistics(AdminDashboard dashboard)
    {
        var kpis = dashboard.Kpis;
        var attention = dashboard.Attention;
        var groups = new List<DashboardStatisticGroup>
        {
            new(AdminLocalization.Instance["core.dashboard.users"], [
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.total"], kpis.UsersTotal),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.newToday"], kpis.NewUsersToday),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.newForWeek"], kpis.NewUsers7Days)]),
            new(AdminLocalization.Instance["core.dashboard.organizations"], [
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.total"], kpis.OrganizationsTotal),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.active"], kpis.ActiveOrganizations),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.newForWeek"], kpis.NewOrganizations7Days),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.members"], kpis.OrganizationMembers),
                .. this.CanViewBilling ? new[] { DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.activeSubscriptions"], kpis.ActiveOrganizationSubscriptions) } : []]),
            new(AdminLocalization.Instance["core.dashboard.vehiclesAndMaintenance"], [
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.vehicles"], kpis.VehiclesTotal),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.maintenance"], kpis.MaintenancesTotal),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.planned"], kpis.PlannedMaintenances),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.documents"], kpis.DocumentsTotal),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.openDefects"], kpis.OpenDefects)])
        };

        if (this.CanViewBilling)
        {
            groups.Add(new DashboardStatisticGroup(AdminLocalization.Instance["core.dashboard.finance"], [
                new(AdminLocalization.Instance["core.dashboard.amountForMonth"], DashboardFormatting.Value((double)kpis.Payments30DaysAmount, "money")),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.paymentsForMonth"], kpis.Payments30DaysCount),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.problemPayments"], attention.FailedPayments)]));
        }

        groups.AddRange([
            new(AdminLocalization.Instance["core.dashboard.communications"], [
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.emailToday"], kpis.EmailsToday),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.emailForWeek"], kpis.Emails7Days),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.sentForWeek"], kpis.SentEmails7Days),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.failedForWeek"], kpis.FailedEmails7Days),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.pendingSend"], kpis.PendingEmails)]),
            new(AdminLocalization.Instance["core.dashboard.otherAttention"], [
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.catalogSuggestions"], attention.PendingCatalogSuggestions),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.pendingInvitations"], attention.PendingInvitations),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.overdueTasks"], attention.OverdueTasks),
                DashboardViewModel.Metric(AdminLocalization.Instance["core.dashboard.lowStock"], attention.LowStockItems)])
        ]);

        this.StatisticGroups = groups;
    }

    /// <summary> Создаёт метрику с числовым значением. </summary>
    private static DashboardMetric Metric(string title, int value) => new(title, DashboardFormatting.Value(value));

    /// <summary> Обновляет метрики платежей. </summary>
    private void UpdatePaymentMetrics()
    {
        var metrics = new List<DashboardMetric>();
        if (!this.CanViewBilling)
        {
            this.PaymentMetrics = metrics;
            return;
        }

        if (this._dashboard is { } dashboard && this._loadedPeriod == this.PeriodDays)
        {
            metrics.AddRange([
                new(AdminLocalization.Instance["core.dashboard.income"], DashboardFormatting.Value((double)dashboard.Period.PaymentsAmount, "money"), AdminLocalization.Instance["core.dashboard.afterRefunds"]),
                new(AdminLocalization.Instance["core.dashboard.paymentsForPeriod"], DashboardFormatting.Value(dashboard.Period.PaymentsCount), this.PeriodLabel)
            ]);
        }

        metrics.AddRange([
            new(AdminLocalization.Instance["core.dashboard.paidWithoutReceipt"], DashboardFormatting.Value(this._missingReceiptCount),
                AdminLocalization.Instance["core.dashboard.receiptNotMarked"], this._missingReceiptCount > 0),
            new(AdminLocalization.Instance["core.dashboard.overdueReceipts"], DashboardFormatting.Value(this._overdueReceiptCount),
                AdminLocalization.Instance["core.dashboard.paidOver24HoursAgo"], this._overdueReceiptCount > 0)
        ]);

        this.PaymentMetrics = metrics;
    }

    private void SetDashboardError(string translationKey, string details)
    {
        this._errorMessageTranslationKey = translationKey;
        this._errorMessageDetails = details;
        this.ErrorMessage = DashboardViewModel.FormatLocalizedError(translationKey, details);
    }

    private void SetStatisticsError(string translationKey, string details)
    {
        this._statisticsErrorTranslationKey = translationKey;
        this._statisticsErrorDetails = details;
        this.StatisticsErrorMessage = DashboardViewModel.FormatLocalizedError(translationKey, details);
    }

    private void SetBillingError(string translationKey, string? details = null)
    {
        this._billingErrorTranslationKey = translationKey;
        this._billingErrorDetails = details;
        this.BillingErrorMessage = DashboardViewModel.FormatLocalizedError(translationKey, details);
    }

    private void ClearDashboardError()
    {
        this._errorMessageTranslationKey = null;
        this._errorMessageDetails = null;
        this.ErrorMessage = string.Empty;
    }

    private void ClearStatisticsError()
    {
        this._statisticsErrorTranslationKey = null;
        this._statisticsErrorDetails = null;
        this.StatisticsErrorMessage = string.Empty;
    }

    private void ClearBillingError()
    {
        this._billingErrorTranslationKey = null;
        this._billingErrorDetails = null;
        this.BillingErrorMessage = string.Empty;
    }

    private static void RefreshLocalizedError(string? translationKey, string? details, Action<string> apply)
    {
        if (translationKey == null)
        {
            return;
        }

        apply(DashboardViewModel.FormatLocalizedError(translationKey, details));
    }

    private static string FormatLocalizedError(string translationKey, string? details)
    {
        var message = AdminLocalization.Instance[translationKey];
        return string.IsNullOrEmpty(details) ? message : $"{message} {details}";
    }

    private static string FormatTimestamp(string translationKey, DateTimeOffset timestamp) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture,
            AdminLocalization.Instance[translationKey], timestamp.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture));

    /// <summary> Создаёт серию графика количества. </summary>
    private static DashboardChartSeries CountSeries(string title, string color, IReadOnlyList<AdminDashboardCountPoint> points) =>
        new(title, color, "count", points.Select(point => new DashboardChartPoint(point.Label, point.Value)).ToArray());
}
