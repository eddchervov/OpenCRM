using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

[Collection("Admin localization")]
public sealed class PaymentServiceActionsTests
{
    [Theory]
    [InlineData("check")]
    [InlineData("refund")]
    [InlineData("retry-access")]
    [InlineData("resolve-review")]
    public async Task ServiceCommandsRequireConfirmationAndNeverUseLegacyStatusMutation(string action)
    {
        using var handler = new ServiceHandler();
        using var client = await PaymentServiceActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        await model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
        model.Reason = "  Проверено по обращению  ";
        model.RefundAmount = 150.25m;
        try
        {
            Assert.False(model.RequestSyncCommand.CanExecute(null));
            Assert.False(model.RequestStatusChangeCommand.CanExecute(null));
            Assert.True(model.RequestServiceActionCommand.CanExecute(action));
            model.RequestServiceActionCommand.Execute(action);
            Assert.True(model.IsConfirmationOpen);
            Assert.Empty(handler.Bodies);
            await model.ConfirmCommand.ExecuteAsync(null);
            var sent = Assert.Single(handler.Bodies);
            using var json = JsonDocument.Parse(sent);
            Assert.Equal(action, json.RootElement.GetProperty("action").GetString());
            Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("operationId").GetGuid());
            Assert.Equal("Проверено по обращению", json.RootElement.GetProperty("reason").GetString());
            if (action == "refund")
            {
                Assert.Equal(150.25m, json.RootElement.GetProperty("amount").GetDecimal());
            }
            Assert.False(model.HasUnresolvedServiceRequest);
            Assert.Equal(0, handler.LegacyMutations);
        }
        finally
        {
            await model.DeactivateAsync(true);
            await new PendingPaymentActionStore().DeleteAsync(client.PendingActionScope);
        }
    }

    [Fact]
    public async Task TimeoutAndRestartReplayTheSameImmutableRequest()
    {
        using var handler = new ServiceHandler { LoseFirstResponse = true };
        using var client = await PaymentServiceActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        await model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
        var localization = AdminLocalization.Instance;
        var previousLanguage = localization.Language;
        var preferencePath = Path.Combine(Path.GetTempPath(), $"opencrm-retry-confirmation-{Guid.NewGuid():N}.txt");
        using var preferenceScope = localization.UsePreferencePath(preferencePath);
        PaymentActionsViewModel? restored = null;
        model.Reason = "Возврат с потерянным ответом";
        model.RefundAmount = 210m;
        try
        {
            localization.Language = "ru";
            model.RequestServiceActionCommand.Execute("refund");
            await model.ConfirmCommand.ExecuteAsync(null);
            Assert.True(model.HasUnresolvedServiceRequest);
            Assert.Single(handler.Bodies);
            await model.DeactivateAsync(true);
            restored = new PaymentActionsViewModel(client);
            restored.SetPayment(handler.Payment);
            await restored.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
            Assert.True(restored.CanRetryUnresolvedServiceRequest);
            Assert.False(restored.RequestServiceActionCommand.CanExecute("refund"));
            restored.RetryUnresolvedServiceActionCommand.Execute(null);
            using var requestJson = JsonDocument.Parse(handler.Bodies[0]);
            var operationId = requestJson.RootElement.GetProperty("operationId").GetGuid();
            Assert.Contains(operationId.ToString(), restored.ConfirmationText);
            localization.Language = "en";
            restored.RefreshLocalizedValues();
            Assert.Contains(operationId.ToString(), restored.ConfirmationText);
            Assert.Contains("Retry", restored.ConfirmationText);
            await restored.ConfirmCommand.ExecuteAsync(null);
            Assert.Equal(2, handler.Bodies.Count);
            Assert.Equal(handler.Bodies[0], handler.Bodies[1]);
            Assert.False(restored.HasUnresolvedServiceRequest);
            await restored.DeactivateAsync(true);
        }
        finally
        {
            await model.DeactivateAsync(true);
            if (restored != null)
            {
                await restored.DeactivateAsync(true);
            }

            await new PendingPaymentActionStore().DeleteAsync(client.PendingActionScope);
            localization.Language = previousLanguage;
            if (File.Exists(preferencePath))
            {
                File.Delete(preferencePath);
            }
        }
    }

    [Fact]
    public async Task ReceiptForAnotherOperationDoesNotDiscardUnresolvedRequest()
    {
        using var handler = new ServiceHandler { ReturnAnotherOperation = true };
        using var client = await PaymentServiceActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        await model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
        model.Reason = "Проверка идентификатора ответа";
        try
        {
            model.RequestServiceActionCommand.Execute("refund");
            await model.ConfirmCommand.ExecuteAsync(null);
            Assert.True(model.HasUnresolvedServiceRequest);
            Assert.False(model.RequestServiceActionCommand.CanExecute("refund"));
        }
        finally
        {
            await model.DeactivateAsync(true);
            await new PendingPaymentActionStore().DeleteAsync(client.PendingActionScope);
        }
    }

    [Fact]
    public async Task RefundRejectsInvalidPrecisionAndAmountBeforeSending()
    {
        using var handler = new ServiceHandler();
        using var client = await PaymentServiceActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        await model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
        model.Reason = "Проверка суммы";
        foreach (var amount in new[] { -1m, 0m, 420.01m, 1.001m })
        {
            model.RefundAmount = amount;
            Assert.False(model.RequestServiceActionCommand.CanExecute("refund"));
        }
        Assert.Empty(handler.Bodies);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task FailedLocalSaveDoesNotSendFinancialRequest()
    {
        using var handler = new ServiceHandler();
        using var client = await PaymentServiceActionsTests.CreateClientAsync(handler);
        var path = Path.GetTempFileName();
        var model = new PaymentActionsViewModel(client, new PendingPaymentActionStore(path));
        model.SetPayment(handler.Payment);
        await model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
        model.Reason = "Проверка локального сбоя";
        try
        {
            model.RequestServiceActionCommand.Execute("refund");
            await model.ConfirmCommand.ExecuteAsync(null);
            Assert.Empty(handler.Bodies);
            Assert.Contains("Запрос не отправлен", model.ErrorMessage);
        }
        finally
        {
            await model.DeactivateAsync(true);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OldAdministratorResponseCannotOverwriteCurrentAdministratorState()
    {
        using var handler = new ServiceHandler { IncludeActorInHint = true };
        using var client = new AdminSessionClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://payment-scope-" + Guid.NewGuid().ToString("N") + ".example.test/")
        });
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        await client.LoginAsync("contact@example.invalid", "local-password", TestContext.Current.CancellationToken);
        var firstScope = client.PendingActionScope;
        handler.DelayNextState = true;
        var previous = model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
        await handler.StateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        handler.ActorId = "admin-b";
        handler.DelayNextState = false;
        await client.LoginAsync("contact@example.invalid", "local-password", TestContext.Current.CancellationToken);
        try
        {
            await model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
            handler.ReleaseState.TrySetResult();
            await previous;
            Assert.EndsWith("admin-b", model.CheckHint);
            Assert.NotEqual(firstScope, client.PendingActionScope);
            Assert.False(model.HasUnresolvedServiceRequest);
        }
        finally
        {
            handler.ReleaseState.TrySetResult();
            await model.DeactivateAsync(true);
            var store = new PendingPaymentActionStore();
            await store.DeleteAsync(firstScope);
            await store.DeleteAsync(client.PendingActionScope);
        }
    }

    [Theory]
    [InlineData(false, 0, false, "Не выдан")]
    [InlineData(true, 420, false, "Не выдан: средства возвращены")]
    [InlineData(true, 150, false, "Выдача требует проверки")]
    [InlineData(true, 420, true, "Период по этой покупке выдан")]
    public async Task PaymentSummarySeparatesVerifiedMoneyAndHistoricalAccess(bool confirmed, decimal refunded, bool granted, string expectedAccess)
    {
        using var handler = new ServiceHandler();
        handler.Payment = new AdminBillingPayment
        {
            Id = 12, PaymentProviderCode = "payments-service", UserId = "buyer", User = "Тестовый клиент",
            Amount = 420, RefundedAmount = refunded, ProviderStatus = confirmed ? "Succeeded" : "Pending",
            PaidAt = confirmed ? DateTime.UtcNow : null, AppliedAt = granted ? DateTime.UtcNow : null,
            OrganizationName = "Тестовая студия", PlanName = "Тестовый PRO",
            ProviderEvents = [new() { Id = Guid.NewGuid(), Stage = "access_application", Code = "applied", CreatedAt = DateTime.UtcNow }]
        };
        using var client = await PaymentServiceActionsTests.CreateClientAsync(handler);
        var model = new BillingViewModel(client);
        try
        {
            await model.ActivateAsync();
            await model.OpenDetailCommand.ExecuteAsync(Assert.Single(model.Rows));
            var detail = Assert.IsType<BillingDetailViewModel>(model.Detail);
            Assert.Equal(expectedAccess, Assert.Single(detail.Fields, field => field.Title == "Доступ по этой покупке").Value);
            Assert.Equal("Последние технические события (до 20)", detail.AuditTitle);
            Assert.Contains(detail.Audits, item => item.Title == "Выдача доступа");
            Assert.DoesNotContain(detail.Fields, field => field.Title == "Последний код ошибки");
            if (!confirmed)
            {
                Assert.Equal("Пока нет", Assert.Single(detail.Fields, field => field.Title == "Оплата подтверждена").Value);
                Assert.Equal("—", Assert.Single(detail.Fields, field => field.Title == "Осталось после возвратов").Value);
            }
        }
        finally
        {
            await model.DeactivateAsync(true);
        }
    }

    [AvaloniaFact]
    public async Task ServiceActionsRenderHintsAndCollapsedHistory()
    {
        using var handler = new ServiceHandler();
        using var client = await PaymentServiceActionsTests.CreateClientAsync(handler);
        var model = new PaymentActionsViewModel(client);
        model.SetPayment(handler.Payment);
        await model.LoadServiceActionsAsync(TestContext.Current.CancellationToken);
        model.Reason = "Возврат части оплаты по обращению клиента";
        model.RefundAmount = 150.25m;
        var view = new PaymentActionsView { DataContext = model };
        var window = new Window { Width = 1040, Height = 820, Content = new ScrollViewer { Content = view } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var labels = new[] { "Проверить платёж", "Вернуть деньги", "Повторить выдачу доступа", "Завершить разбор" };
            foreach (var label in labels)
            {
                Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => Equals(button.Content, label));
            }
            Assert.False(Assert.Single(view.GetVisualDescendants().OfType<Expander>()).IsExpanded);
            var amount = Assert.Single(view.GetVisualDescendants().OfType<NumericUpDown>());
            Assert.Equal(420m, amount.Maximum);
            Assert.Equal(0.01m, amount.Increment);
            PaymentServiceActionsTests.Capture(window, "payment-service-actions-desktop.png");
            model.RequestServiceActionCommand.Execute("refund");
            Dispatcher.UIThread.RunJobs();
            PaymentServiceActionsTests.Capture(window, "payment-service-refund-confirmation-desktop.png");
            Assert.Empty(handler.Bodies);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        using var stream = File.Create(Path.Combine(directory, name));
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private static async Task<AdminSessionClient> CreateClientAsync(ServiceHandler handler)
    {
        var client = new AdminSessionClient(new HttpClient(handler)
        {
            BaseAddress = new Uri("https://payment-actions-" + Guid.NewGuid().ToString("N") + ".example.test/")
        });
        await client.LoginAsync("contact@example.invalid", "local-test-password", TestContext.Current.CancellationToken);

        return client;
    }

    private sealed class ServiceHandler : HttpMessageHandler
    {
        public bool IncludeActorInHint { get; init; }
        public string ActorId { get; set; } = "admin";
        public bool DelayNextState { get; set; }
        public TaskCompletionSource StateStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseState { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool LoseFirstResponse { get; init; }
        public bool ReturnAnotherOperation { get; init; }
        public List<string> Bodies { get; } = [];
        public int LegacyMutations { get; private set; }
        public AdminBillingPayment Payment { get; set; } = new()
        {
            Id = 12, PaymentProviderCode = "payments-service", UserId = "buyer", User = "Тестовый клиент",
            Amount = 420m, ProviderStatus = "Succeeded", PaidAt = DateTime.UtcNow,
            OrganizationName = "Тестовая студия", HasExternalId = true, ExternalPaymentId = "qa-provider-id"
        };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return ServiceHandler.Json(new { accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "local-refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1), user = new { id = this.ActorId } });
            }
            if (path == "/api/v1/admin/current-user")
            {
                return ServiceHandler.Json(new AdminProfile { Id = this.ActorId, IsSystem = true,
                    Permissions = ["Admin.Access", "Admin.Billing.View", "Admin.Billing.Manage"] });
            }
            if (path == "/api/v1/admin/billing/payments")
            {
                return ServiceHandler.Json(new AdminBillingPaymentsPage { Items = [this.Payment], TotalCount = 1, Page = 1, PageSize = 50 });
            }
            if (path == "/api/v1/admin/billing/payments/12")
            {
                return ServiceHandler.Json(this.Payment);
            }
            if (path.EndsWith("/status-audits", StringComparison.Ordinal))
            {
                return ServiceHandler.Json(Array.Empty<AdminPaymentStatusAudit>());
            }
            if (path.EndsWith("/sync-yookassa", StringComparison.Ordinal) || path.EndsWith("/mark-status", StringComparison.Ordinal))
            {
                this.LegacyMutations++;
                throw new InvalidOperationException("Для МС нельзя использовать старое изменение статуса.");
            }
            if (path.EndsWith("/service-actions", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var responseActor = this.ActorId;
                if (this.DelayNextState)
                {
                    this.StateStarted.TrySetResult();
                    await this.ReleaseState.Task;
                }

                return ServiceHandler.Json(new AdminBillingPaymentServiceActions
                {
                    StateToken = new string('A', 64), CanCheck = true, CanRefund = true, CanRetryAccess = true,
                    CanResolveReview = true, RefundableAmount = 420m,
                    CheckHint = "Запрашивает проверку в ЮKassa. Деньги не списывает." + (this.IncludeActorInHint ? " " + responseActor : string.Empty),
                    RefundHint = "Возвращает выбранную сумму. Выданный доступ сохраняется.",
                    RetryAccessHint = "Повторяет выдачу оплаченного доступа без второго продления.",
                    ResolveReviewHint = "Сохраняет решение и комментарий, не меняя деньги и доступ."
                });
            }
            if (path.EndsWith("/service-actions", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                this.Bodies.Add(body);
                if (this.LoseFirstResponse && this.Bodies.Count == 1)
                {
                    throw new HttpRequestException("Ответ потерян после приёма команды.");
                }
                var command = JsonSerializer.Deserialize<AdminBillingPaymentServiceActionRequest>(body,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                return ServiceHandler.Json(new AdminBillingPaymentServiceActionOperation
                {
                    Id = this.ReturnAnotherOperation ? Guid.NewGuid() : command.OperationId, Action = command.Action,
                    Status = "queued", Amount = command.Amount, Reason = command.Reason, Actor = "admin",
                    CreatedAt = DateTime.UtcNow, Message = "Операция принята в обработку."
                }, HttpStatusCode.Accepted);
            }

            throw new InvalidOperationException("Неожиданный запрос: " + path);
        }

        private static HttpResponseMessage Json<T>(T value, HttpStatusCode status = HttpStatusCode.OK) => new(status)
        {
            Content = JsonContent.Create(value)
        };
    }
}
