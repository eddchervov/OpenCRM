using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class PurchaseTests
{
    [Fact]
    public async Task QueueFiltersSortsAndPagesWithoutTruncatingPurchases()
    {
        using var handler = new PurchaseHandler { Count = 76 };
        using var client = PurchaseTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new PurchasesViewModel(client);
        await model.ActivateAsync();
        Assert.Equal(76, model.TotalCount);
        Assert.Equal(50, model.Rows.Count);
        Assert.False(model.Rows[0].Purchase.NpdReceiptIssued);
        Assert.True(model.NextPageCommand.CanExecute(null));
        await model.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(26, model.Rows.Count);
        Assert.Equal(2, model.Page);
        model.Search = "7712345678";
        await model.ApplyFiltersCommand.ExecuteAsync(null);
        Assert.Equal(1, model.Page);
        Assert.Equal(38, model.TotalCount);
        Assert.All(model.Rows, row => Assert.Equal("business", row.Purchase.BuyerType));
        model.Search = string.Empty;
        model.SelectedReceipt = model.ReceiptOptions.Single(option => option.Code == "pending");
        await model.ApplyFiltersCommand.ExecutionTask!;
        Assert.Equal(39, model.TotalCount);
        Assert.All(model.Rows, row => Assert.False(row.Purchase.NpdReceiptIssued));
        model.SelectedBuyer = model.BuyerOptions.Single(option => option.Code == "unknown");
        await model.ApplyFiltersCommand.ExecutionTask!;
        Assert.Empty(model.Rows);
        Assert.True(model.IsEmpty);
        await model.ResetFiltersCommand.ExecuteAsync(null);
        model.SelectedQueue = model.QueueOptions.Single(option => option.Code == "failed");
        await model.ApplyFiltersCommand.ExecutionTask!;
        Assert.All(model.Rows, row => Assert.Equal("failed", row.Purchase.NpdReceiptDeliveryStatus));
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task ReceiptChangesRequireConfirmationAndExpectedStateAndKeepInputOnFailure()
    {
        using var handler = new PurchaseHandler { Count = 1 };
        using var client = PurchaseTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new PurchasesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRow = model.Rows[0];
        model.OpenDetailCommand.Execute(null);
        model.ChangeReason = "  Чек оформлен и передан.  ";
        model.RequestToggleIssuedCommand.Execute(null);
        Assert.Equal(0, handler.Mutations);
        Assert.True(model.IsConfirmationOpen);
        model.CancelConfirmationCommand.Execute(null);
        Assert.Equal(0, handler.Mutations);
        model.RequestToggleIssuedCommand.Execute(null);
        handler.FailMutation = true;
        Assert.Contains("Чек оформлен и передан.", model.ConfirmationMessage);
        model.ChangeReason = "Причина изменена после подготовки подтверждения.";
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.NotEmpty(model.ErrorMessage);
        Assert.Equal("Причина изменена после подготовки подтверждения.", model.ChangeReason);
        Assert.False(model.Detail!.Purchase.NpdReceiptIssued);
        using (var document = JsonDocument.Parse(handler.LastBody))
        {
            Assert.False(document.RootElement.GetProperty("expectedIssued").GetBoolean());
            Assert.True(document.RootElement.GetProperty("issued").GetBoolean());
            Assert.Equal("Чек оформлен и передан.", document.RootElement.GetProperty("reason").GetString());
        }

        handler.FailMutation = false;
        model.RequestToggleIssuedCommand.Execute(null);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.Detail!.Purchase.NpdReceiptIssued);
        Assert.Empty(model.ChangeReason);
        model.ChangeReason = "Исправление ошибочной отметки.";
        model.RequestToggleIssuedCommand.Execute(null);
        await model.ConfirmCommand.ExecuteAsync(null);
        using (var document = JsonDocument.Parse(handler.LastBody))
        {
            Assert.True(document.RootElement.GetProperty("expectedIssued").GetBoolean());
            Assert.False(document.RootElement.GetProperty("issued").GetBoolean());
        }

        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task ReadOnlyAndRefreshedPermissionsBlockMutationsAndKeepDownloads()
    {
        using var handler = new PurchaseHandler { CanManage = false };
        using var client = PurchaseTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new PurchasesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRow = model.Rows[0];
        model.OpenDetailCommand.Execute(null);
        model.ChangeReason = "Причина.";
        Assert.False(model.CanManage);
        Assert.False(model.CanUpload);
        Assert.True(model.CanDownload);
        Assert.False(model.RequestToggleIssuedCommand.CanExecute(null));
        Assert.False(model.SaveDetailsCommand.CanExecute(null));
        var file = await model.DownloadFileAsync();
        Assert.Equal("receipt.pdf", file!.FileName);
        Assert.NotEmpty(file.Content);
        Assert.Equal(0, handler.Mutations);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task ValidatesReceiptUrlCommentFilesAndConfirmsDeleteAndSend()
    {
        using var handler = new PurchaseHandler { Count = 1 };
        using var client = PurchaseTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new PurchasesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRow = model.Rows[0];
        model.OpenDetailCommand.Execute(null);
        foreach (var invalid in new[] { "http://nalog.ru/receipt", "https://nalog.ru.attacker.test/", "https://attacker.test/", "https://contact@example.invalid/test" })
        {
            model.ReceiptUrl = invalid;
            await model.SaveDetailsCommand.ExecuteAsync(null);
            Assert.NotEmpty(model.ErrorMessage);
        }

        model.ReceiptUrl = "https://lknpd.nalog.ru/api/receipt";
        model.ReceiptComment = new string('я', 1001);
        await model.SaveDetailsCommand.ExecuteAsync(null);
        Assert.Equal(0, handler.Mutations);
        using var oversized = new MemoryStream(new byte[5_242_881]);
        await model.UploadFileAsync(oversized, "receipt.pdf", "application/pdf");
        Assert.Contains("5 МБ", model.ErrorMessage);
        using var invalidFile = new MemoryStream([1, 2, 3]);
        await model.UploadFileAsync(invalidFile, "receipt.exe", "application/pdf");
        Assert.Equal(0, handler.Mutations);
        model.ReceiptComment = "Проверено.";
        await model.SaveDetailsCommand.ExecuteAsync(null);
        Assert.Equal(1, handler.Mutations);
        model.RequestSendCommand.Execute(null);
        Assert.True(model.IsConfirmationOpen);
        Assert.Equal(1, handler.Mutations);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(2, handler.Mutations);
        Assert.Equal("queued", model.Detail!.Purchase.NpdReceiptDeliveryStatus);
        Assert.False(model.RequestSendCommand.CanExecute(null));
        model.RequestDeleteFileCommand.Execute(null);
        Assert.Equal(2, handler.Mutations);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.Equal(3, handler.Mutations);
        Assert.False(model.HasFile);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task StopDropsLateResponsesAndForbiddenRetainsReadOnlyDetail()
    {
        using var handler = new PurchaseHandler { DelayList = true };
        using var client = PurchaseTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new PurchasesViewModel(client);
        var activation = model.ActivateAsync();
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        model.Stop(true);
        handler.Released.SetResult();
        await activation;
        Assert.Empty(model.Rows);
        Assert.False(model.IsBusy);
        handler.DelayList = false;
        await model.ActivateAsync();
        model.SelectedRow = model.Rows[0];
        model.OpenDetailCommand.Execute(null);
        model.ReceiptComment = "Сохранить при ошибке.";
        handler.DenyMutation = true;
        handler.CanManage = false;
        var denied = false;
        model.AccessDenied += async () =>
        {
            denied = true;
            await client.GetProfileAsync(TestContext.Current.CancellationToken);
            model.RefreshAccess();
        };
        await model.SaveDetailsCommand.ExecuteAsync(null);
        Assert.True(denied);
        Assert.NotNull(model.Detail);
        Assert.Equal("Сохранить при ошибке.", model.ReceiptComment);
        Assert.False(model.CanManage);
        Assert.False(model.SaveDetailsCommand.CanExecute(null));
        await model.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task PurchasesListAndReceiptRenderAtNormalAndNarrowDesktopWidths()
    {
        using var handler = new PurchaseHandler();
        using var client = PurchaseTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new PurchasesViewModel(client);
        await model.ActivateAsync();
        var window = new Window { Width = 1160, Height = 840, Content = new PurchasesView { DataContext = model } };
        window.Show();
        PurchaseTests.SaveScreenshot(window, "purchases-list.png");
        var pagination = window.GetVisualDescendants().OfType<ListPagination>().Single(control => control.IsEffectivelyVisible);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "PurchasesList");
        Assert.True(pagination.TranslatePoint(new Point(0, pagination.Bounds.Height), window)!.Value.Y
            <= list.TranslatePoint(default, window)!.Value.Y);
        model.SelectedRow = model.Rows[0];
        var secondRow = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
        var openButton = secondRow.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Открыть");
        var openPoint = openButton.TranslatePoint(new Point(openButton.Bounds.Width / 2, openButton.Bounds.Height / 2), window)!.Value;
        window.MouseDown(openPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(openPoint, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(model.Rows[1].Id, model.Detail!.Id);
        model.CloseDetailCommand.Execute(null);
        window.Width = 600;
        PurchaseTests.SaveScreenshot(window, "purchases-narrow.png");
        Assert.True(pagination.TranslatePoint(new Point(0, pagination.Bounds.Height), window)!.Value.Y
            <= list.TranslatePoint(default, window)!.Value.Y);
        model.SelectedRow = model.Rows[0];
        model.OpenDetailCommand.Execute(null);
        PurchaseTests.SaveScreenshot(window, "purchases-detail-narrow.png");
        window.Width = 1160;
        PurchaseTests.SaveScreenshot(window, "purchases-detail.png");
        window.Close();
        await model.DeactivateAsync(true);
    }

    private static void SaveScreenshot(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS") is not { Length: > 0 } directory)
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, name));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private static AdminSessionClient CreateClient(PurchaseHandler handler) => new(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });

    private sealed class PurchaseHandler : HttpMessageHandler
    {
        public bool CanManage { get; set; } = true;
        public bool FailMutation { get; set; }
        public bool DenyMutation { get; set; }
        public bool DelayList { get; set; }
        public int Count { get; init; } = 4;
        public int Mutations { get; private set; }
        public string LastBody { get; private set; } = string.Empty;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _issued;
        private bool _hasFile = true;
        private string _delivery = "not_sent";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return PurchaseHandler.Json(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid", roles = Array.Empty<string>() }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return PurchaseHandler.Json(new AdminProfile { Id = "admin", Permissions = this.CanManage ? ["Admin.Access", "Admin.Billing.View", "Admin.Billing.Manage"] : ["Admin.Access", "Admin.Billing.View"] });
            }

            if (path == "/api/v1/admin/billing/purchases")
            {
                if (this.DelayList)
                {
                    this.Started.TrySetResult();
                    await this.Released.Task;
                }

                return PurchaseHandler.Json(Enumerable.Range(1, this.Count).Select(index => new AdminBillingPurchase
                {
                    PaymentId = index, User = "Покупатель с длинным именем", UserEmail = "contact@example.invalid", BuyerName = "Детейлинг студия",
                    BuyerType = index % 2 == 0 ? "business" : "individual", BuyerInn = index % 2 == 0 ? "7712345678" : null,
                    Amount = 3000, RefundedAmount = index == 4 ? 500 : 0, PlanName = "Рабочее пространство · Детейлинг",
                    PaymentStatus = "completed", PaidAt = DateTime.UtcNow.AddDays(-index),
                    NpdReceiptIssued = index == 1 ? this._issued : index % 2 != 0,
                    NpdReceiptUrl = "https://lknpd.nalog.ru/receipt", NpdReceiptFileName = this._hasFile ? "receipt.pdf" : null,
                    NpdReceiptFileUrl = this._hasFile ? "https://admin.test/file" : null,
                    NpdReceiptDeliveryStatus = index == 1 ? this._delivery : index == 2 ? "failed" : "not_sent"
                }).ToArray());
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/file", StringComparison.Ordinal))
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
                response.Content.Headers.ContentType = new("application/pdf");
                response.Content.Headers.ContentDisposition = new("attachment") { FileName = "receipt.pdf" };

                return response;
            }

            this.Mutations++;
            this.LastBody = request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            if (this.DenyMutation)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (this.FailMutation)
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "Состояние изменилось." }) };
            }

            if (path.EndsWith("/npd-receipt", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(this.LastBody);
                this._issued = document.RootElement.GetProperty("issued").GetBoolean();
            }

            if (path.EndsWith("/send", StringComparison.Ordinal))
            {
                this._delivery = "queued";
            }

            if (request.Method == HttpMethod.Delete)
            {
                this._hasFile = false;
            }

            return PurchaseHandler.Json(new { message = "Сохранено." });
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
