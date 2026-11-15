using System.Net;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class DashboardTests
{
    [AvaloniaFact]
    public async Task LoginOpensDashboardAndRendersMetricsAndCharts()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsDashboardOpen);
            Assert.Equal(["Новые пользователи", "Новые организации"], model.Dashboard.RegistrationMetrics.Select(metric => metric.Title));
            Assert.Equal(1, model.Dashboard.GrowthSeries.Count);
            Assert.Equal(["Регистрации", "Email", "Обслуживания"], model.Dashboard.ActivitySeries.Select(series => series.Title));
            Assert.Single(model.Dashboard.PaymentSeries);
            var heading = window.GetVisualDescendants().OfType<TextBlock>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardHeading");

            Assert.True(heading.IsEffectivelyVisible);
            var payments = window.GetVisualDescendants().OfType<DashboardChart>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardPaymentsChart");

            var periodSwitcher = window.GetVisualDescendants().OfType<WrapPanel>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardPeriodSwitcher");
            Assert.True(periodSwitcher.TranslatePoint(default, window)!.Value.Y < payments.TranslatePoint(default, window)!.Value.Y);
            var sectionIds = new[] { "DashboardPaymentsSection", "DashboardRegistrationsSection", "DashboardStatisticsSection" };
            var sectionPositions = sectionIds.Select(id => window.GetVisualDescendants().OfType<Border>().Single(control =>
                AutomationProperties.GetAutomationId(control) == id).TranslatePoint(default, window)!.Value.Y).ToArray();

            Assert.Equal(sectionPositions.Order(), sectionPositions);
            DashboardTests.SaveScreenshot(window, "dashboard.png");
            window.Width = 1800;
            window.Height = 1100;
            Dispatcher.UIThread.RunJobs();
            var paymentSection = window.GetVisualDescendants().OfType<Border>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardPaymentsSection");
            var registrationSection = window.GetVisualDescendants().OfType<Border>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardRegistrationsSection");

            Assert.Equal(paymentSection.TranslatePoint(default, window)!.Value.Y, registrationSection.TranslatePoint(default, window)!.Value.Y, 1);
            Assert.True(registrationSection.TranslatePoint(default, window)!.Value.X > paymentSection.TranslatePoint(default, window)!.Value.X);
            DashboardTests.SaveScreenshot(window, "dashboard-wide.png");
            Dispatcher.UIThread.RunJobs();
            window.Width = 900;
            window.Height = 800;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(paymentSection.TranslatePoint(default, window)!.Value.X, registrationSection.TranslatePoint(default, window)!.Value.X, 1);
            Assert.True(registrationSection.TranslatePoint(default, window)!.Value.Y > paymentSection.TranslatePoint(default, window)!.Value.Y);
            heading.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            DashboardTests.SaveScreenshot(window, "dashboard-compact.png");
            Dispatcher.UIThread.RunJobs();
            DashboardTests.SaveScreenshot(window, "dashboard-details.png");
            Dispatcher.UIThread.RunJobs();
            DashboardTests.SaveScreenshot(window, "dashboard-analytics.png");
            await model.Dashboard.LoadStatisticsCommand.ExecuteAsync(null);
            var usersStatistics = model.Dashboard.StatisticGroups.Single(group => group.Title == "Пользователи");
            Assert.Contains(usersStatistics.Items, metric => metric.Title == "Новых за 7 дней");
            Assert.DoesNotContain(usersStatistics.Items, metric => metric.Title is "Входов сегодня" or "Активны за 7 дней" or "Ни разу не входили");
            var statistics = window.GetVisualDescendants().OfType<Border>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardStatisticsSection");

            statistics.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            DashboardTests.SaveScreenshot(window, "dashboard-statistics.png");
            handler.CanViewBilling = false;
            await model.RefreshProfileCommand.ExecuteAsync(null);
            heading.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            Assert.False(payments.IsEffectivelyVisible);
            var growth = window.GetVisualDescendants().OfType<DashboardChart>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardGrowthChart");

            Assert.True(growth.Bounds.Width > 400);
            DashboardTests.SaveScreenshot(window, "dashboard-without-billing.png");
            window.Width = 1800;
            Dispatcher.UIThread.RunJobs();
            Assert.True(registrationSection.Bounds.Width > 1400);
            var statisticsSection = window.GetVisualDescendants().OfType<Border>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardStatisticsSection");

            Assert.Equal(statisticsSection.TranslatePoint(default, window)!.Value.X, registrationSection.TranslatePoint(default, window)!.Value.X, 1);
            await model.ShowProfileCommand.ExecuteAsync(null);
            Assert.False(heading.IsEffectivelyVisible);
            Assert.False(model.Dashboard.RefreshCommand.CanExecute(null));
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    [Fact]
    public async Task GeneralStatisticsLoadsOnlyOnRequestAndKeepsItsOwnSnapshot()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.Empty(model.Dashboard.StatisticGroups);
        Assert.Empty(model.Dashboard.StatisticsUpdatedLabel);
        var requests = handler.Requests.Count(path => path == "/api/v1/admin/dashboard?periodDays=1");
        await model.Dashboard.LoadStatisticsCommand.ExecuteAsync(null);
        Assert.Equal(requests + 1, handler.Requests.Count(path => path == "/api/v1/admin/dashboard?periodDays=1"));
        Assert.Equal(6, model.Dashboard.StatisticGroups.Count);
        var snapshot = model.Dashboard.StatisticGroups;
        var updated = model.Dashboard.StatisticsUpdatedLabel;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        await model.Dashboard.SetPeriodCommand.ExecuteAsync(7);
        Assert.Same(snapshot, model.Dashboard.StatisticGroups);
        Assert.Equal(updated, model.Dashboard.StatisticsUpdatedLabel);
        handler.DashboardStatus = HttpStatusCode.ServiceUnavailable;
        await model.Dashboard.LoadStatisticsCommand.ExecuteAsync(null);
        Assert.Same(snapshot, model.Dashboard.StatisticGroups);
        Assert.True(model.Dashboard.HasStatisticsError);
        Assert.False(model.Dashboard.HasError);
        await model.LogoutCommand.ExecuteAsync(null);
        Assert.Empty(model.Dashboard.StatisticGroups);
        Assert.Empty(model.Dashboard.StatisticsUpdatedLabel);
        Assert.False(model.Dashboard.LoadStatisticsCommand.CanExecute(null));
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task LeavingDashboardCancelsManualStatisticsRequest()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        handler.DelayDashboard = true;
        var operation = model.Dashboard.LoadStatisticsCommand.ExecuteAsync(null);
        await handler.DashboardStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.True(model.Dashboard.IsLoadingStatistics);
        Assert.False(model.Dashboard.LoadStatisticsCommand.CanExecute(null));
        await model.ShowProfileCommand.ExecuteAsync(null);
        await operation;
        Assert.True(handler.DashboardCancelled);
        Assert.Empty(model.Dashboard.StatisticGroups);
        Assert.False(model.Dashboard.IsLoadingStatistics);
        Assert.False(model.Dashboard.HasStatisticsError);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task SiteCountersFollowTheSharedPeriod()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.Dashboard.SetPeriodCommand.ExecuteAsync(7);
        await model.Dashboard.SetPeriodCommand.ExecuteAsync(30);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task OldServerDoesNotSubstituteFixedSevenDayEmailCountsForSelectedPeriod()
    {
        var handler = new DashboardApiHandler { LegacySitePeriod = true };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task NeutralModeHidesFinancialMetricsEvenWhenBillingPermissionIsPresent()
    {
        var handler = new DashboardApiHandler { CommercialEnabled = false };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);

        Assert.False(model.Dashboard.CanViewBilling);
        Assert.Empty(model.Dashboard.PaymentSeries);
        Assert.Empty(model.Dashboard.PaymentMetrics);
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/billing/"));
        await model.Dashboard.LoadStatisticsCommand.ExecuteAsync(null);
        Assert.DoesNotContain(model.Dashboard.StatisticGroups, group => group.Title == "Финансы");
        Assert.DoesNotContain(model.Dashboard.StatisticGroups.SelectMany(group => group.Items), metric => metric.Title == "Активных подписок");

        await model.ShutdownAsync();
    }

    [Fact]
    public async Task MissingBillingPermissionHidesAllFinancialMetricsAndRightsRefreshRemovesPage()
    {
        var handler = new DashboardApiHandler { CanViewBilling = false };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.False(model.Dashboard.CanViewBilling);
        Assert.Empty(model.Dashboard.PaymentSeries);
        Assert.Empty(model.Dashboard.PaymentMetrics);
        Assert.False(model.Dashboard.HasUrgentPayments);
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/billing/"));
        await model.Dashboard.LoadStatisticsCommand.ExecuteAsync(null);
        Assert.NotEmpty(model.Dashboard.StatisticGroups);
        Assert.DoesNotContain(model.Dashboard.StatisticGroups, group => group.Title == "Финансы");
        Assert.DoesNotContain(model.Dashboard.StatisticGroups.SelectMany(group => group.Items), metric => metric.Title == "Активных подписок");
        handler.CanViewDashboard = false;
        await model.RefreshProfileCommand.ExecuteAsync(null);
        Assert.True(model.IsAuthenticated);
        Assert.False(model.CanViewDashboard);
        Assert.False(model.IsDashboardOpen);
        Assert.Empty(model.Dashboard.RegistrationMetrics);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task RefreshFailureKeepsPreviousDataButPeriodChangeDoesNotMislabelOldValues()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        var previous = model.Dashboard.RegistrationMetrics;
        handler.DashboardStatus = HttpStatusCode.ServiceUnavailable;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(previous, model.Dashboard.RegistrationMetrics);
        Assert.Contains("ранее полученные", model.Dashboard.ErrorMessage);
        await model.Dashboard.SetPeriodCommand.ExecuteAsync(30);
        await model.RefreshProfileCommand.ExecuteAsync(null);
        Assert.Empty(model.Dashboard.RegistrationMetrics);
        Assert.Empty(model.Dashboard.ActivitySeries);
        Assert.DoesNotContain("ранее полученные", model.Dashboard.ErrorMessage);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task BillingRevocationFiltersCachedAttentionAndStatisticsAfterPeriodFailure()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.Dashboard.LoadStatisticsCommand.ExecuteAsync(null);
        Assert.Contains(model.Dashboard.StatisticGroups, group => group.Title == "Финансы");
        handler.DashboardStatus = HttpStatusCode.ServiceUnavailable;
        await model.Dashboard.SetPeriodCommand.ExecuteAsync(30);
        handler.CanViewBilling = false;
        await model.RefreshProfileCommand.ExecuteAsync(null);
        Assert.Empty(model.Dashboard.RegistrationMetrics);
        Assert.Empty(model.Dashboard.PaymentMetrics);
        Assert.False(model.Dashboard.HasUrgentPayments);
        Assert.DoesNotContain(model.Dashboard.StatisticGroups, group => group.Title == "Финансы");
        Assert.DoesNotContain(model.Dashboard.StatisticGroups.SelectMany(group => group.Items), metric => metric.Title == "Активных подписок");
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task ReceiptCountersFollowSelectedPeriodWithoutLoadingPurchaseHistory()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.Equal("3", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Оплачено без чека").Value);
        Assert.Equal("0", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Просроченные чеки").Value);
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        await model.Dashboard.SetPeriodCommand.ExecuteAsync(7);
        Assert.Equal("7", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Оплачено без чека").Value);
        await model.ShowProfileCommand.ExecuteAsync(null);
        await model.ShowDashboardCommand.ExecuteAsync(null);
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        Assert.Contains("/api/v1/admin/billing/attention?periodDays=1", handler.Requests);
        Assert.Contains("/api/v1/admin/billing/attention?periodDays=7", handler.Requests);
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/billing/purchases"));
        await model.ShutdownAsync();
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 7)]
    public async Task MissingOrWrongBillingPeriodDoesNotDisplayAllTimeCountsAsSelectedPeriod(bool legacyContract, int? returnedPeriod)
    {
        var handler = new DashboardApiHandler { LegacyBillingContract = legacyContract, ReturnedBillingPeriod = returnedPeriod, FailedActivationCount = 2 };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.Equal("—", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Оплачено без чека").Value);
        Assert.Equal("—", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Просроченные чеки").Value);
        Assert.True(model.Dashboard.HasBillingError);
        Assert.True(model.Dashboard.HasUrgentPayments);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task BillingCountsRemainAvailableWhenDashboardEndpointFails()
    {
        var handler = new DashboardApiHandler { DashboardStatus = HttpStatusCode.ServiceUnavailable };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.Empty(model.Dashboard.RegistrationMetrics);
        Assert.Equal("3", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Оплачено без чека").Value);
        Assert.True(model.Dashboard.HasError);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task BillingFailureShowsUnknownCountsOrPreviousValuesInsteadOfZero()
    {
        var handler = new DashboardApiHandler { BillingStatus = HttpStatusCode.ServiceUnavailable };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.Equal("—", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Оплачено без чека").Value);
        Assert.True(model.Dashboard.HasBillingError);
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        handler.BillingStatus = HttpStatusCode.OK;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        handler.BillingStatus = HttpStatusCode.ServiceUnavailable;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        Assert.Equal("3", model.Dashboard.PaymentMetrics.Single(metric => metric.Title == "Оплачено без чека").Value);
        Assert.Contains("ранее полученные", model.Dashboard.BillingErrorMessage);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task BillingForbiddenRefreshesPermissionsWithoutClosingDashboard()
    {
        var handler = new DashboardApiHandler { FailedActivationCount = 2 };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.True(model.Dashboard.HasUrgentPayments);
        handler.BillingStatus = HttpStatusCode.Forbidden;
        handler.RevokeBillingOnRequest = true;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.IsAuthenticated);
        Assert.True(model.IsDashboardOpen);
        Assert.False(model.Dashboard.CanViewBilling);
        Assert.False(model.Dashboard.HasUrgentPayments);
        Assert.Empty(model.Dashboard.PaymentMetrics);
        await model.ShutdownAsync();
    }

    [AvaloniaFact]
    public async Task FailedActivationBannerAppearsOnlyForActualPaidActivationFailures()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        var window = new MainWindow { DataContext = model };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            var urgent = window.GetVisualDescendants().OfType<Border>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "DashboardUrgentPayments");

            Assert.False(urgent.IsEffectivelyVisible);
            handler.FailedActivationCount = 2;
            await model.Dashboard.RefreshCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(urgent.IsEffectivelyVisible);
            Assert.Contains("2", model.Dashboard.UrgentPaymentMessage);
            DashboardTests.SaveScreenshot(window, "dashboard-urgent.png");
            handler.FailedActivationCount = 0;
            await model.Dashboard.RefreshCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.False(urgent.IsEffectivelyVisible);
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    [Fact]
    public async Task BillingForbiddenClearsFinancialDataEvenWhenProfileRefreshFails()
    {
        var handler = new DashboardApiHandler { FailedActivationCount = 2 };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        handler.BillingStatus = HttpStatusCode.Forbidden;
        handler.ProfileStatus = HttpStatusCode.ServiceUnavailable;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.IsAuthenticated);
        Assert.True(model.IsDashboardOpen);
        Assert.False(model.Dashboard.CanViewBilling);
        Assert.Empty(model.Dashboard.PaymentMetrics);
        Assert.Empty(model.Dashboard.PaymentSeries);
        Assert.False(model.Dashboard.HasUrgentPayments);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task ClosingDuringBillingLoadCancelsRequestAndKeepsSession()
    {
        var handler = new DashboardApiHandler { DelayBilling = true };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        var login = model.LoginCommand.ExecuteAsync(null);
        await handler.BillingStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.ShutdownAsync();
        await login;
        Assert.Equal(1, handler.CancelledBillingRequests);
        Assert.True(session.HasSession);
        Assert.Empty(model.Dashboard.PaymentMetrics);
        Assert.False(model.Dashboard.HasUrgentPayments);
    }

    [Fact]
    public async Task LeavingPageCancelsRequestAndLogoutClearsDashboard()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        handler.DelayDashboard = true;
        var loading = model.Dashboard.RefreshCommand.ExecuteAsync(null);
        await handler.DashboardStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.ShowProfileCommand.ExecuteAsync(null);
        await loading;
        Assert.True(handler.DashboardCancelled);
        Assert.Empty(model.Dashboard.ErrorMessage);
        await model.LogoutCommand.ExecuteAsync(null);
        Assert.Empty(model.Dashboard.RegistrationMetrics);
        Assert.False(model.IsAuthenticated);
    }

    [Fact]
    public async Task ForbiddenDuringInitialLoginRefreshesRightsBeforeLoginCompletes()
    {
        var handler = new DashboardApiHandler { DashboardStatus = HttpStatusCode.Forbidden, RevokeDashboardOnRequest = true };
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        Assert.True(model.IsAuthenticated);
        Assert.False(model.IsDashboardOpen);
        Assert.False(model.CanViewDashboard);
        Assert.Equal(2, handler.Requests.Count(path => path == "/api/v1/admin/current-user"));
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task ForbiddenDashboardClosesPageRefreshesRightsAndKeepsAdminSession()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        handler.CanViewDashboard = false;
        handler.DashboardStatus = HttpStatusCode.Forbidden;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.IsAuthenticated);
        Assert.False(model.IsDashboardOpen);
        Assert.False(model.CanViewDashboard);
        Assert.Empty(model.Dashboard.RegistrationMetrics);
        Assert.False(model.Dashboard.RefreshCommand.CanExecute(null));
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task ExpiredSessionReturnsToLoginAndClearsDashboard()
    {
        var handler = new DashboardApiHandler();
        using var http = DashboardTests.CreateHttpClient(handler);
        using var session = new AdminSessionClient(http);
        var model = DashboardTests.CreateViewModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        handler.DashboardStatus = HttpStatusCode.Unauthorized;
        await model.Dashboard.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.IsAuthenticated);
        Assert.Empty(model.Dashboard.RegistrationMetrics);
        Assert.True(model.HasError);
    }

    private static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler) { BaseAddress = new Uri("https://test.local/") };

    private static MainWindowViewModel CreateViewModel(AdminSessionClient session, HttpClient http) => new(session, http.BaseAddress!)
    {
        Email = "contact@example.invalid",
        Password ="test-password"
    };

    private static void SaveScreenshot(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(Path.Combine(directory, fileName));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }
}
