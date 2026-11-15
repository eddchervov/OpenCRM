using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

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
    public string UrgentPaymentMessage => $"Оплата получена, но подписка не активирована: {this.FailedActivationCount}. Проверьте обработку платежей.";

    /// <summary> Признак наличия ошибки биллинга. </summary>
    public bool HasBillingError => !string.IsNullOrWhiteSpace(this.BillingErrorMessage);

    /// <summary> Серии графика роста. </summary>
    public IReadOnlyList<DashboardChartSeries> GrowthSeries => this.ActivitySeries.Take(1).ToArray();

    /// <summary> Подпись выбранного периода. </summary>
    public string PeriodLabel => this.PeriodDays switch
    {
        1 => "Сегодня",
        7 => "Последние 7 дней",
        _ => "Последние 30 дней"
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
        1 => "Сегодня по часам",
        7 => "За 7 дней",
        _ => "За месяц"
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
            this.BillingErrorMessage = string.Empty;
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
            this.StatisticsUpdatedLabel = string.Empty;
            this.StatisticsErrorMessage = string.Empty;
            this._loadedPeriod = null;
            this.RegistrationMetrics = [];
            this.PaymentMetrics = [];
            this.FailedActivationCount = 0;
            this._missingReceiptCount = null;
            this._overdueReceiptCount = null;
            this.BillingErrorMessage = string.Empty;
            this.StatisticGroups = [];
            this.ActivitySeries = [];
            this.PaymentSeries = [];
            this.UpdatedLabel = string.Empty;
            this.ErrorMessage = string.Empty;
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
            this.StatisticsUpdatedLabel = "Загружено: " + dashboard.GeneratedAt.ToLocalTime().ToString("HH:mm:ss");
            this.StatisticsErrorMessage = string.Empty;
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
                this.StatisticsErrorMessage = this._statisticsDashboard == null
                    ? "Не удалось загрузить общую статистику. " + exception.Message
                    : "Не удалось обновить общую статистику. Показаны ранее полученные значения. " + exception.Message;
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
            this.UpdatedLabel = "Обновлено: " + dashboard.GeneratedAt.ToLocalTime().ToString("HH:mm:ss");
            this.ErrorMessage = string.Empty;
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

            this.ErrorMessage = this._loadedPeriod == period
                ? "Не удалось обновить данные. Показаны ранее полученные значения. " + exception.Message
                : "Не удалось загрузить дашборд. " + exception.Message;
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
                this.BillingErrorMessage = "Сервер не поддерживает статистику чеков за выбранный период. Требуется обновление сервера.";
                this.UpdatePaymentMetrics();

                return;
            }

            this._missingReceiptCount = attention.MissingReceiptCount;
            this._overdueReceiptCount = attention.OverdueReceiptCount;
            this.BillingErrorMessage = string.Empty;
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
                this.BillingErrorMessage = this._missingReceiptCount.HasValue
                    ? "Не удалось обновить платежи. Показаны ранее полученные значения. " + exception.Message
                    : "Не удалось загрузить состояние платежей. " + exception.Message;
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
        this.BillingErrorMessage = string.Empty;
        this.UpdatedLabel = string.Empty;
        this.ErrorMessage = string.Empty;
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
            new("Новые пользователи", DashboardFormatting.Value(period.NewUsers), "Регистрации за период"),
            new("Новые организации", DashboardFormatting.Value(period.NewOrganizations), "Созданы за период")
        ] : [];
        var charts = dashboard.Charts;
        this.ActivitySeries = hasCurrentPeriod ? [
            DashboardViewModel.CountSeries("Регистрации", "#3478F6", charts.Registrations),
            DashboardViewModel.CountSeries("Email", "#E67E35", charts.Emails),
            DashboardViewModel.CountSeries("Обслуживания", "#9974E5", charts.Maintenances)] : [];

        this.PaymentSeries = this.CanViewBilling && hasCurrentPeriod
            ? [new("Платежи", "#1B9368", "money", charts.Payments.Select(point =>
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
            new("Пользователи", [
                DashboardViewModel.Metric("Всего", kpis.UsersTotal),
                DashboardViewModel.Metric("Новых сегодня", kpis.NewUsersToday),
                DashboardViewModel.Metric("Новых за 7 дней", kpis.NewUsers7Days)]),
            new("Организации", [
                DashboardViewModel.Metric("Всего", kpis.OrganizationsTotal),
                DashboardViewModel.Metric("Активных", kpis.ActiveOrganizations),
                DashboardViewModel.Metric("Новых за 7 дней", kpis.NewOrganizations7Days),
                DashboardViewModel.Metric("Участников", kpis.OrganizationMembers),
                .. this.CanViewBilling ? new[] { DashboardViewModel.Metric("Активных подписок", kpis.ActiveOrganizationSubscriptions) } : []]),
            new("Техника и обслуживание", [
                DashboardViewModel.Metric("Техники", kpis.VehiclesTotal),
                DashboardViewModel.Metric("Обслуживаний", kpis.MaintenancesTotal),
                DashboardViewModel.Metric("Запланировано", kpis.PlannedMaintenances),
                DashboardViewModel.Metric("Документов", kpis.DocumentsTotal),
                DashboardViewModel.Metric("Открытых дефектов", kpis.OpenDefects)])
        };

        if (this.CanViewBilling)
        {
            groups.Add(new DashboardStatisticGroup("Финансы", [
                new("Сумма за 30 дней", DashboardFormatting.Value((double)kpis.Payments30DaysAmount, "money")),
                DashboardViewModel.Metric("Платежей за 30 дней", kpis.Payments30DaysCount),
                DashboardViewModel.Metric("Проблемных платежей", attention.FailedPayments)]));
        }

        groups.AddRange([
            new("Коммуникации", [
                DashboardViewModel.Metric("Email сегодня", kpis.EmailsToday),
                DashboardViewModel.Metric("Email за 7 дней", kpis.Emails7Days),
                DashboardViewModel.Metric("Отправлено за 7 дней", kpis.SentEmails7Days),
                DashboardViewModel.Metric("Ошибок за 7 дней", kpis.FailedEmails7Days),
                DashboardViewModel.Metric("Ожидают отправки", kpis.PendingEmails)]),
            new("Прочие показатели внимания", [
                DashboardViewModel.Metric("Предложения в каталог", attention.PendingCatalogSuggestions),
                DashboardViewModel.Metric("Ожидающие приглашения", attention.PendingInvitations),
                DashboardViewModel.Metric("Просроченные задачи", attention.OverdueTasks),
                DashboardViewModel.Metric("Низкие остатки", attention.LowStockItems)])
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
                new("Поступления", DashboardFormatting.Value((double)dashboard.Period.PaymentsAmount, "money"), "С учётом возвратов"),
                new("Платежей за период", DashboardFormatting.Value(dashboard.Period.PaymentsCount), this.PeriodLabel)
            ]);
        }

        metrics.AddRange([
            new("Оплачено без чека", DashboardFormatting.Value(this._missingReceiptCount),
                "Выдача чека не отмечена", this._missingReceiptCount > 0),
            new("Просроченные чеки", DashboardFormatting.Value(this._overdueReceiptCount),
                "Оплачены более 24 ч назад", this._overdueReceiptCount > 0)
        ]);

        this.PaymentMetrics = metrics;
    }

    /// <summary> Создаёт серию графика количества. </summary>
    private static DashboardChartSeries CountSeries(string title, string color, IReadOnlyList<AdminDashboardCountPoint> points) =>
        new(title, color, "count", points.Select(point => new DashboardChartPoint(point.Label, point.Value)).ToArray());
}
