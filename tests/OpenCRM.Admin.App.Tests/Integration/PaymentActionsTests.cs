using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class PaymentActionsTests
{
    [Fact]
    public async Task ReadOnlyAndBillingManagerHaveDifferentMutationPermissions()
    {
        using var handler = new PaymentHandler { IsSystem = false, CanManage = false };
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        model.Reason = "Исправление";
        Assert.False(model.IsVisible);
        Assert.False(model.RequestSyncCommand.CanExecute(null));
        Assert.False(model.RequestStatusChangeCommand.CanExecute(null));

        handler.CanManage = true;
        await client.GetProfileAsync(TestContext.Current.CancellationToken);
        model.RefreshAccess();
        Assert.True(model.RequestSyncCommand.CanExecute(null));
        Assert.False(model.RequestStatusChangeCommand.CanExecute(null));
        model.Stop();
        Assert.False(model.RequestSyncCommand.CanExecute(null));
        Assert.Equal(0, handler.Mutations);
    }

    [Fact]
    public async Task ManualStatusRequiresConfirmationAndSendsExpectedStatusAndReason()
    {
        using var handler = new PaymentHandler();
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        Assert.False(model.RequestStatusChangeCommand.CanExecute(null));
        model.SelectedStatus = model.StatusOptions.Single(option => option.Code == "completed");
        model.Reason = "  Проверено по платёжному документу  ";
        model.RequestStatusChangeCommand.Execute(null);
        Assert.True(model.IsConfirmationOpen);
        Assert.Equal(0, handler.Mutations);

        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(1, handler.Mutations);
        using var body = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("pending", body.RootElement.GetProperty("expectedCurrentStatus").GetString());
        Assert.Equal("completed", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("Проверено по платёжному документу", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Состояние обновлено.", model.SuccessMessage);
        Assert.False(model.IsConfirmationOpen);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task StaleStatusFailurePreservesInputAndRefreshesPayment()
    {
        using var handler = new PaymentHandler { MutationStatus = HttpStatusCode.BadRequest };
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        var refreshCount = 0;
        model.PaymentChanged += _ => { refreshCount++; return Task.CompletedTask; };
        model.Reason = "Проверка статуса";
        model.RequestStatusChangeCommand.Execute(null);

        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal("Статус уже изменился.", model.ErrorMessage);
        Assert.Equal("Проверка статуса", model.Reason);
        Assert.Equal(1, refreshCount);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task DeactivationCancelsWriteAndDiscardsLateSuccess()
    {
        using var handler = new PaymentHandler { DelayMutation = true };
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        var refreshed = false;
        model.PaymentChanged += _ => { refreshed = true; return Task.CompletedTask; };
        model.RequestSyncCommand.Execute(null);
        var operation = model.ConfirmCommand.ExecuteAsync(null);
        await handler.MutationStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var deactivation = model.DeactivateAsync(true);
        handler.ReleaseMutation.TrySetResult();
        await Task.WhenAll(operation, deactivation);

        Assert.False(refreshed);
        Assert.Empty(model.SuccessMessage);
        Assert.False(model.IsVisible);
        Assert.False(model.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task SyncRefreshesListDetailAndAuditWithoutChangingSubscriptions()
    {
        using var handler = new PaymentHandler();
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new BillingViewModel(client);
        await model.ActivateAsync();
        await model.OpenDetailCommand.ExecuteAsync(Assert.Single(model.Rows));
        model.PaymentActions.RequestSyncCommand.Execute(null);

        await model.PaymentActions.ConfirmCommand.ExecuteAsync(null);

        Assert.True(model.IsDetailOpen);
        Assert.Equal("Оплачено", Assert.Single(model.Rows).Status);
        Assert.True(model.Detail!.HasAudits);
        Assert.Equal("Состояние обновлено.", model.OperationMessage);
        Assert.Equal(2, handler.PageRequests);
        Assert.Equal(2, handler.AuditRequests);
        await model.ShowPurchasesCommand.ExecuteAsync(null);
        Assert.True(model.IsPurchasesTab);
        Assert.False(model.IsLedgerListVisible);
        await model.SetTabCommand.ExecuteAsync(true);
        Assert.False(model.IsPurchasesTab);
        Assert.True(model.IsPaymentTabSelected);
        await model.DeactivateAsync(true);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task DeniedMutationUsesPermissionRefreshOrSessionExpiration(HttpStatusCode status)
    {
        using var handler = new PaymentHandler { MutationStatus = status };
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        var denied = false;
        var expired = false;
        model.AccessDenied += () => { denied = true; return Task.CompletedTask; };
        model.SessionExpired += () => expired = true;
        model.RequestSyncCommand.Execute(null);

        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(status == HttpStatusCode.Forbidden, denied);
        Assert.Equal(status == HttpStatusCode.Unauthorized, expired);
        Assert.Empty(model.SuccessMessage);
    }

    [Fact]
    public async Task SuccessRemainsVisibleWhenUpdatedPaymentLeavesFilteredPage()
    {
        using var handler = new PaymentHandler { OnlyPending = true };
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new BillingViewModel(client);
        await model.ActivateAsync();
        await model.OpenDetailCommand.ExecuteAsync(Assert.Single(model.Rows));
        model.PaymentActions.RequestSyncCommand.Execute(null);

        await model.PaymentActions.ConfirmCommand.ExecuteAsync(null);

        Assert.False(model.IsDetailOpen);
        Assert.Empty(model.Rows);
        Assert.Equal("Состояние обновлено.", model.OperationMessage);
        await model.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task PaymentActionsRenderConfirmationAtNarrowDesktopWidth()
    {
        using var handler = new PaymentHandler();
        using var client = await PaymentActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        model.Reason = "Уточнение статуса после проверки оплаты";
        model.RequestStatusChangeCommand.Execute(null);
        var window = new Window { Width = 600, Height = 800, Content = new PaymentActionsView { DataContext = model } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                using var stream = File.Create(Path.Combine(directory, "payment-confirmation.png"));
                frame.Save(stream, PngBitmapEncoderOptions.Default);
            }

            Assert.Equal(0, handler.Mutations);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    private static async Task<AdminSessionClient> CreateClientAsync(PaymentHandler handler)
    {
        var client = new AdminSessionClient(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);

        return client;
    }

    private sealed class PaymentHandler : HttpMessageHandler
    {
        private string _status = "pending";
        public bool IsSystem { get; init; } = true;
        public bool CanManage { get; set; } = true;
        public bool DelayMutation { get; init; }
        public bool OnlyPending { get; init; }
        public HttpStatusCode MutationStatus { get; init; } = HttpStatusCode.OK;
        public int Mutations { get; private set; }
        public int PageRequests { get; private set; }
        public int AuditRequests { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public TaskCompletionSource MutationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseMutation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AdminBillingPayment Payment => new()
        {
            Id = 1, UserId = "client", User = "Клиент", UserEmail = "contact@example.invalid", PlanName = "Детейлинг",
            Amount = 1000, CreatedAt = DateTime.UtcNow, PaymentStatus = this._status, HasExternalId = true,
            ExternalPaymentId = "external-payment", StatusName = this._status == "completed" ? "Оплачено" : "Ожидание"
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return PaymentHandler.Json(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1), user = new { id = "admin" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return PaymentHandler.Json(new AdminProfile
                {
                    Id = "admin", IsSystem = this.IsSystem,
                    Permissions = this.CanManage ? ["Admin.Access", "Admin.Billing.View", "Admin.Billing.Manage"] : ["Admin.Access", "Admin.Billing.View"]
                });
            }

            if (path.EndsWith("/sync-yookassa", StringComparison.Ordinal) || path.EndsWith("/mark-status", StringComparison.Ordinal))
            {
                this.Mutations++;
                this.LastBody = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
                this.MutationStarted.TrySetResult();
                if (this.DelayMutation)
                {
                    await this.ReleaseMutation.Task;
                }

                if (this.MutationStatus != HttpStatusCode.OK)
                {
                    return new HttpResponseMessage(this.MutationStatus) { Content = JsonContent.Create(new { message = "Статус уже изменился." }) };
                }

                this._status = "completed";

                return PaymentHandler.Json(new AdminBillingOperationResponse { Message = "Состояние обновлено.", Status = this._status });
            }

            if (path == "/api/v1/admin/billing/payments")
            {
                this.PageRequests++;

                var items = this.OnlyPending && this._status != "pending" ? Array.Empty<AdminBillingPayment>() : [this.Payment];

                return PaymentHandler.Json(new AdminBillingPaymentsPage { Items = items, TotalCount = items.Length, Page = 1, PageSize = 50 });
            }

            if (path == "/api/v1/admin/billing/payments/1")
            {
                return PaymentHandler.Json(this.Payment);
            }

            if (path.EndsWith("/status-audits", StringComparison.Ordinal))
            {
                this.AuditRequests++;

                return PaymentHandler.Json(this.Mutations == 0 ? Array.Empty<AdminPaymentStatusAudit>()
                    : [new AdminPaymentStatusAudit { Id = 1, Source = "manual", Reason = "Проверено" }]);
            }

            if (path == "/api/v1/admin/billing/purchases")
            {
                return PaymentHandler.Json(Array.Empty<AdminBillingPurchase>());
            }

            throw new InvalidOperationException("Неожиданный запрос: " + path);
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
