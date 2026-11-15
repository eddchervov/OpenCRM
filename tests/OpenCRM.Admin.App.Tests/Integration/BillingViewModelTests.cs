using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.App.Views;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class BillingViewModelTests
{
    [Fact]
    public async Task PaymentsUseServerPaginationAndRequestOnlyCurrentPage()
    {
        using var handler = new BillingHandler();
        using var client = BillingViewModelTests.CreateClient(handler);
        await BillingViewModelTests.LoginAsync(client);
        var model = new BillingViewModel(client);

        await model.ActivateAsync();

        Assert.Equal(50, model.Rows.Count);
        Assert.Equal(102, model.TotalCount);
        Assert.Single(handler.Requests);
        Assert.Contains("/api/v1/admin/billing/payments?", handler.Requests[0]);
        Assert.Contains("pageSize=50", handler.Requests[0]);
        Assert.Contains("status=all", handler.Requests[0]);
        Assert.Contains("periodDays=30", handler.Requests[0]);

        await model.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(2, model.Page);
        Assert.Equal(50, model.Rows.Count);
        Assert.Equal(49, model.Rows[0].Id);
        Assert.Equal(2, handler.Requests.Count);

        model.SelectedStatus = "Ошибка активации";
        await BillingViewModelTests.WaitForLoadAsync(model, handler, 3);

        Assert.Equal(1, model.TotalCount);
        Assert.Equal(101, Assert.Single(model.Rows).Id);
        Assert.Contains("status=activationfailed", handler.Requests[2]);
    }

    [Fact]
    public async Task SubscriptionsLoadOrganizationsOnlyAndDefaultToActive()
    {
        using var handler = new BillingHandler();
        using var client = BillingViewModelTests.CreateClient(handler);
        await BillingViewModelTests.LoginAsync(client);
        var model = new BillingViewModel(client);
        await model.ActivateAsync();

        await model.SetTabCommand.ExecuteAsync(false);

        Assert.Equal("Активные", model.SelectedStatus);
        Assert.All(model.Rows, row => Assert.False(row.IsPayment));
        Assert.Equal(1, model.TotalCount);
        Assert.Equal("Детейлинг студия", Assert.Single(model.Rows).Customer);
        Assert.Contains("/api/v1/admin/billing/subscriptions/organizations?", handler.Requests[1]);
        Assert.Contains("status=active", handler.Requests[1]);
        Assert.DoesNotContain(handler.Requests, request => request.Contains("/billing?", StringComparison.Ordinal));

        model.SelectedStatus = "Все";
        await BillingViewModelTests.WaitForLoadAsync(model, handler, 3);

        Assert.Equal(2, model.TotalCount);
        Assert.Contains("status=all", handler.Requests[2]);
    }

    [Fact]
    public async Task RefreshPreservesTabAndFiltersAndSendsSearchToServer()
    {
        using var handler = new BillingHandler();
        using var client = BillingViewModelTests.CreateClient(handler);
        await BillingViewModelTests.LoginAsync(client);
        var model = new BillingViewModel(client);
        await model.ActivateAsync();
        await model.SetTabCommand.ExecuteAsync(false);
        model.SelectedStatus = "Все";
        await BillingViewModelTests.WaitForLoadAsync(model, handler, 3);
        model.Search = "Организация 2";
        await model.ApplyFiltersCommand.ExecuteAsync(null);

        await model.RefreshCommand.ExecuteAsync(null);

        Assert.False(model.IsPaymentsTab);
        Assert.Equal("Все", model.SelectedStatus);
        Assert.Equal("Организация 2", Assert.Single(model.Rows).Customer);
        Assert.Contains("search=" + Uri.EscapeDataString("Организация 2"), handler.Requests[^1]);
        Assert.DoesNotContain(handler.Requests, request => request.Contains("/billing?", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PaymentDetailRemainsVisibleWhenAuditRequestFails()
    {
        using var handler = new BillingHandler { PaymentAuditStatus = HttpStatusCode.InternalServerError };
        using var client = BillingViewModelTests.CreateClient(handler);
        await BillingViewModelTests.LoginAsync(client);
        var model = new BillingViewModel(client);
        await model.ActivateAsync();
        var row = model.Rows.First(item => item.Id == 1);

        await model.OpenDetailCommand.ExecuteAsync(row);

        Assert.True(model.IsDetailOpen);
        Assert.Equal("План 1", model.Detail?.Title);
        Assert.True(model.Detail?.HasAuditError);
        Assert.True(model.HasDetailError);
        Assert.Empty(model.Detail!.Audits);
    }

    [AvaloniaFact]
    public async Task NavigationRendersBothTabsAndDetailAndClearsDataOnLogout()
    {
        using var handler = new BillingHandler();
        using var client = BillingViewModelTests.CreateClient(handler);
        var model = new MainWindowViewModel(client, new Uri("https://admin.test/"))
        {
            Email = "contact@example.invalid",
            Password =""
        };
        var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            await model.ShowBillingCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsBillingOpen);
            Assert.False(model.IsProfileOpen);
            Assert.False(model.IsScrollablePageOpen);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "BillingList");

            Assert.True(list.IsEffectivelyVisible);
            Assert.True(list.Bounds.Height > 500);
            var pagination = window.GetVisualDescendants().OfType<ListPagination>().Single(control => control.IsEffectivelyVisible);
            Assert.True(pagination.TranslatePoint(new Point(0, pagination.Bounds.Height), window)!.Value.Y
                <= list.TranslatePoint(default, window)!.Value.Y);
            Assert.Equal(50, list.ItemCount);
            var firstRow = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
            var hoverPoint = firstRow.TranslatePoint(new Point(24, firstRow.Bounds.Height / 2), window)!.Value;
            window.MouseMove(hoverPoint, RawInputModifiers.None);
            window.MouseDown(hoverPoint, MouseButton.Left, RawInputModifiers.None);
            window.MouseUp(hoverPoint, MouseButton.Left, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(firstRow.IsSelected);
            Assert.True(firstRow.IsPointerOver);
            Assert.Equal(Color.Parse("#2B4358"), Assert.IsAssignableFrom<ISolidColorBrush>(firstRow.Background).Color);
            Assert.Equal(Color.Parse("#F1F1F1"), Assert.IsAssignableFrom<ISolidColorBrush>(firstRow.Foreground).Color);
            var search = window.GetVisualDescendants().OfType<TextBox>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "BillingSearch");
            var status = window.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "BillingStatus");

            Assert.Equal(search.Bounds.Height, status.Bounds.Height);
            Assert.Equal(search.TranslatePoint(default, window)!.Value.Y, status.TranslatePoint(default, window)!.Value.Y);
            BillingViewModelTests.SaveScreenshot(window, "billing-payments.png");
            await model.Billing.NextPageCommand.ExecuteAsync(null);
            var row = model.Billing.Rows[0];
            await model.Billing.OpenDetailCommand.ExecuteAsync(row);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.Billing.IsDetailOpen);
            Assert.Equal(row.Plan, model.Billing.Detail!.Title);
            BillingViewModelTests.SaveScreenshot(window, "billing-detail.png");
            await model.Billing.BackToListCommand.ExecuteAsync(null);
            Assert.Equal(2, model.Billing.Page);
            await model.Billing.SetTabCommand.ExecuteAsync(false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Активные", model.Billing.SelectedStatus);
            Assert.Equal("Детейлинг студия", Assert.Single(model.Billing.Rows).Customer);
            BillingViewModelTests.SaveScreenshot(window, "billing-subscriptions.png");
            await model.ShowProfileCommand.ExecuteAsync(null);
            Assert.False(model.IsBillingOpen);
            await model.ShowBillingCommand.ExecuteAsync(null);
            Assert.False(model.Billing.IsPaymentsTab);
            await model.LogoutCommand.ExecuteAsync(null);
            Assert.Empty(model.Billing.Rows);
            Assert.Null(model.Billing.Detail);
            Assert.False(model.IsBillingOpen);
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    [Fact]
    public async Task MissingPermissionPreventsBillingRequests()
    {
        using var handler = new BillingHandler { CanViewBilling = false };
        using var client = BillingViewModelTests.CreateClient(handler);
        var model = new MainWindowViewModel(client, new Uri("https://admin.test/"))
        {
            Email = "contact@example.invalid",
            Password =""
        };
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowBillingCommand.ExecuteAsync(null);
        Assert.False(model.IsBillingOpen);
        Assert.Empty(handler.Requests);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task ForbiddenResponseClosesBillingAndClearsLoadedData()
    {
        using var handler = new BillingHandler();
        using var client = BillingViewModelTests.CreateClient(handler);
        var model = new MainWindowViewModel(client, new Uri("https://admin.test/"))
        {
            Email = "contact@example.invalid",
            Password =""
        };
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowBillingCommand.ExecuteAsync(null);
        Assert.NotEmpty(model.Billing.Rows);
        handler.CanViewBilling = false;
        await model.Billing.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.IsBillingOpen);
        Assert.False(model.CanViewBilling);
        Assert.Empty(model.Billing.Rows);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task FailedNextPageKeepsTheLoadedPageAndCanRetry()
    {
        using var handler = new BillingHandler();
        using var client = BillingViewModelTests.CreateClient(handler);
        await BillingViewModelTests.LoginAsync(client);
        var model = new BillingViewModel(client);
        await model.ActivateAsync();
        var firstId = model.Rows[0].Id;
        handler.PageStatus = HttpStatusCode.InternalServerError;

        await model.NextPageCommand.ExecuteAsync(null);

        Assert.Equal(1, model.Page);
        Assert.Equal(firstId, model.Rows[0].Id);
        Assert.True(model.HasError);
        handler.PageStatus = HttpStatusCode.OK;
        await model.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Page);
        Assert.Contains("page=2", handler.Requests[^1]);
        await model.DeactivateAsync();
    }

    [Fact]
    public async Task FailedTabLoadDoesNotShowRowsFromTheOtherTab()
    {
        using var handler = new BillingHandler();
        using var client = BillingViewModelTests.CreateClient(handler);
        await BillingViewModelTests.LoginAsync(client);
        var model = new BillingViewModel(client);
        await model.ActivateAsync();
        Assert.NotEmpty(model.Rows);
        handler.PageStatus = HttpStatusCode.InternalServerError;

        await model.SetTabCommand.ExecuteAsync(false);

        Assert.False(model.IsPaymentsTab);
        Assert.Empty(model.Rows);
        Assert.True(model.HasError);
        handler.PageStatus = HttpStatusCode.OK;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.All(model.Rows, row => Assert.False(row.IsPayment));
        await model.DeactivateAsync();
    }

    private static void SaveScreenshot(Window window, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
        if (string.IsNullOrEmpty(directory))
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

    private static AdminSessionClient CreateClient(BillingHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        return new AdminSessionClient(httpClient);
    }

    private static async Task LoginAsync(AdminSessionClient client)
    {
        await client.LoginAsync("contact@example.invalid", "password");
    }

    private static async Task WaitForLoadAsync(BillingViewModel model, BillingHandler handler, int requestCount)
    {
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout)
        {
            if (handler.Requests.Count >= requestCount && !model.IsLoading)
            {
                return;
            }

            await Task.Delay(10);
        }

        Assert.Fail("Не дождались завершения загрузки списка.");
    }

    private sealed class BillingHandler : HttpMessageHandler
    {
        private readonly IReadOnlyList<AdminBillingPayment> _payments = Enumerable.Range(1, 100)
            .Select(index => new AdminBillingPayment
            {
                Id = index,
                User = $"Пользователь {index}",
                UserEmail = $"user{index}@test.ru",
                PlanName = $"План {index}",
                Amount = index * 100,
                CreatedAt = DateTime.UtcNow.AddMinutes(-index),
                PaymentStatus = "completed",
                StatusGroup = "success",
                StatusName = "Оплачено"
            })
            .Append(new AdminBillingPayment
            {
                Id = 101,
                User = "Пользователь ошибка",
                PlanName = "Ошибка активации",
                Amount = 100,
                CreatedAt = DateTime.UtcNow,
                PaymentStatus = "failed",
                StatusGroup = "problem",
                StatusName = "Ошибка",
                IsProblem = true
            })
            .Append(new AdminBillingPayment
            {
                Id = 102,
                User = "Пользователь провайдер",
                PlanName = "Ошибка провайдера",
                Amount = 100,
                CreatedAt = DateTime.UtcNow,
                PaymentStatus = "provider_error",
                StatusGroup = "problem",
                StatusName = "Ошибка провайдера",
                IsProblem = true
            })
            .ToArray();

        private readonly IReadOnlyList<AdminBillingOrganizationSubscription> _subscriptions =
        [
            new() { Id = 1, OrganizationName = "Детейлинг студия", Owner = "Владелец", PlanName = "Бизнес", Source = "Оплата", IsActive = true, StartDate = DateTime.UtcNow, EndDate = DateTime.UtcNow.AddDays(30), DaysLeft = 30 },
            new() { Id = 2, OrganizationName = "Организация 2", Owner = "Владелец", PlanName = "Бизнес", Source = "Оплата", IsActive = false, StartDate = DateTime.UtcNow.AddDays(-60), EndDate = DateTime.UtcNow.AddDays(-30), DaysLeft = -30 }
        ];

        public HttpStatusCode PageStatus { get; set; } = HttpStatusCode.OK;

        public bool CanViewBilling { get; set; } = true;

        public List<string> Requests { get; } = [];

        public HttpStatusCode PaymentAuditStatus { get; init; } = HttpStatusCode.OK;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var pathAndQuery = request.RequestUri!.PathAndQuery;
            var path = request.RequestUri.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return Task.FromResult(BillingHandler.Json(new
                {
                    accessToken = "access-token",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh",
                    refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid", roles = Array.Empty<string>() }
                }));
            }

            if (path == "/api/v1/auth/revoke")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }

            if (path == "/api/v1/admin/current-user")
            {
                return Task.FromResult(BillingHandler.Json(new AdminProfile
                {
                    Id = "admin",
                    Email = "contact@example.invalid",
                    CommercialEnabled = true,
                    Permissions = this.CanViewBilling ? ["Admin.Access", "Admin.Billing.View"] : ["Admin.Access"]
                }));
            }

            if (!this.CanViewBilling && path.StartsWith("/api/v1/admin/billing", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            if (path == "/api/v1/admin/billing/payments")
            {
                this.Requests.Add(pathAndQuery);
                return Task.FromResult(this.PageStatus == HttpStatusCode.OK
                    ? this.CreatePaymentPage(request.RequestUri.Query)
                    : new HttpResponseMessage(this.PageStatus));
            }

            if (path == "/api/v1/admin/billing/subscriptions/organizations")
            {
                this.Requests.Add(pathAndQuery);
                return Task.FromResult(this.PageStatus == HttpStatusCode.OK
                    ? this.CreateSubscriptionPage(request.RequestUri.Query)
                    : new HttpResponseMessage(this.PageStatus));
            }

            if (path.StartsWith("/api/v1/admin/billing/payments/", StringComparison.Ordinal)
                && int.TryParse(path.Split('/')[^1], out var paymentId))
            {
                return Task.FromResult(BillingHandler.Json(this._payments.Single(payment => payment.Id == paymentId)));
            }

            if (path.EndsWith("/status-audits", StringComparison.Ordinal))
            {
                return Task.FromResult(this.PaymentAuditStatus == HttpStatusCode.OK
                    ? BillingHandler.Json(Array.Empty<AdminPaymentStatusAudit>())
                    : new HttpResponseMessage(this.PaymentAuditStatus)
                    {
                        Content = JsonContent.Create(new { message = "История недоступна." })
                    });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private HttpResponseMessage CreatePaymentPage(string query)
        {
            var parameters = BillingHandler.ParseQuery(query);
            IEnumerable<AdminBillingPayment> payments = this._payments;
            var search = parameters.GetValueOrDefault("search");
            if (!string.IsNullOrWhiteSpace(search))
            {
                payments = payments.Where(payment => payment.User.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var status = parameters.GetValueOrDefault("status", "all");
            payments = status switch
            {
                "success" => payments.Where(payment => payment.StatusGroup == "success"),
                "pending" => payments.Where(payment => payment.StatusGroup == "pending"),
                "canceled" => payments.Where(payment => payment.StatusGroup == "canceled"),
                "refunded" => payments.Where(payment => payment.StatusGroup == "refunded"),
                "activationfailed" => payments.Where(payment => payment.PaymentStatus == "failed"),
                _ => payments
            };
            var allRows = payments.OrderByDescending(payment => payment.CreatedAt).ToArray();
            var page = int.Parse(parameters["page"], CultureInfo.InvariantCulture);
            var pageSize = int.Parse(parameters["pageSize"], CultureInfo.InvariantCulture);
            return BillingHandler.Json(new AdminBillingPaymentsPage
            {
                TotalCount = allRows.Length,
                Page = page,
                PageSize = pageSize,
                Items = allRows.Skip((page - 1) * pageSize).Take(pageSize).ToArray()
            });
        }

        private HttpResponseMessage CreateSubscriptionPage(string query)
        {
            var parameters = BillingHandler.ParseQuery(query);
            IEnumerable<AdminBillingOrganizationSubscription> subscriptions = this._subscriptions;
            var search = parameters.GetValueOrDefault("search");
            if (!string.IsNullOrWhiteSpace(search))
            {
                subscriptions = subscriptions.Where(subscription =>
                    subscription.OrganizationName.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            var status = parameters.GetValueOrDefault("status", "active");
            subscriptions = status switch
            {
                "active" => subscriptions.Where(subscription => subscription.IsActive),
                "expired" => subscriptions.Where(subscription => subscription.EndDate < DateTime.UtcNow),
                "scheduled" => subscriptions.Where(subscription => subscription.StartDate > DateTime.UtcNow),
                _ => subscriptions
            };
            var allRows = subscriptions.OrderByDescending(subscription => subscription.StartDate).ToArray();
            var page = int.Parse(parameters["page"], CultureInfo.InvariantCulture);
            var pageSize = int.Parse(parameters["pageSize"], CultureInfo.InvariantCulture);
            return BillingHandler.Json(new AdminBillingSubscriptionsPage
            {
                TotalCount = allRows.Length,
                Page = page,
                PageSize = pageSize,
                Items = allRows.Skip((page - 1) * pageSize).Take(pageSize).ToArray()
            });
        }

        private static Dictionary<string, string> ParseQuery(string query) => query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
