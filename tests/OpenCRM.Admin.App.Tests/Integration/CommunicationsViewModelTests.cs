using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class CommunicationsViewModelTests
{
    [Fact]
    public async Task EmailUsesAppliedFiltersAndServerPaginationAndLoadsBodyAndEvents()
    {
        using var handler = new CommunicationsHandler();
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new EmailHistoryViewModel(client);
        await model.ActivateAsync();
        Assert.Contains("page=1&pageSize=50", handler.Requests[^1]);
        Assert.True(model.NextPageCommand.CanExecute(null));
        model.Search = " contact@example.invalid ";
        model.SelectedStatus = model.StatusOptions.Single(option => option.Code == "queued");
        await model.ApplyFiltersCommand.ExecuteAsync(null);
        Assert.Contains("search=contact%40example.invalid&status=queued&page=1", handler.Requests[^1]);
        model.Search = "Ещё не применённый запрос";
        await model.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Page);
        Assert.Contains("search=contact%40example.invalid&status=queued&page=2", handler.Requests[^1]);
        model.SelectedRow = model.Rows[0];
        await model.OpenDetailCommand.ExecuteAsync(null);
        Assert.True(model.IsDetailOpen);
        Assert.Equal("Здравствуйте & спасибо", model.BodyText);
        Assert.Single(model.Events);
        Assert.Contains("Доставлено", model.Events[0].Event);
        await model.DeactivateAsync(true);
        Assert.Empty(model.Rows);
        Assert.Null(model.Detail);
    }

    [Fact]
    public async Task SupportReadOnlyProfileCannotReplyOrSave()
    {
        using var handler = new CommunicationsHandler { CanManage = false };
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new SupportMessagesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRow = model.Rows[0];
        await model.OpenDetailCommand.ExecuteAsync(null);
        model.ReplyText = "Ответ";
        Assert.False(model.CanManage);
        Assert.False(model.CanReply);
        Assert.False(model.SendReplyCommand.CanExecute(null));
        Assert.False(model.SaveDetailsCommand.CanExecute(null));
        Assert.Single(model.Entries);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task SupportSavesStatusAndCommentAndRetainsReplyOnServerFailure()
    {
        using var handler = new CommunicationsHandler();
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new SupportMessagesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRow = model.Rows[0];
        await model.OpenDetailCommand.ExecuteAsync(null);
        model.NextStatus = model.EditStatusOptions.Single(option => option.Code == "closed");
        model.AdminComment = "Проверили подключение.";
        await model.SaveDetailsCommand.ExecuteAsync(null);
        Assert.Contains("\"statusCode\":\"closed\"", handler.LastBody);
        Assert.Equal("closed", model.Detail!.StatusCode);
        Assert.Equal("Проверили подключение.", model.AdminComment);
        model.ReplyText = "  Проблема устранена.  ";
        handler.FailReply = true;
        await model.SendReplyCommand.ExecuteAsync(null);
        Assert.Equal("  Проблема устранена.  ", model.ReplyText);
        Assert.NotEmpty(model.ErrorMessage);
        handler.FailReply = false;
        await model.SendReplyCommand.ExecuteAsync(null);
        Assert.Empty(model.ReplyText);
        Assert.Equal(2, model.Entries.Count);
        Assert.NotEmpty(model.SuccessMessage);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task StopDiscardsLateSuccessfulResponseEvenWhenTransportIgnoresCancellation()
    {
        using var handler = new CommunicationsHandler { DelayEmail = true };
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new EmailHistoryViewModel(client);
        var activation = model.ActivateAsync();
        await handler.EmailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        model.Stop(true);
        handler.EmailReleased.SetResult();
        await activation;
        Assert.Empty(model.Rows);
        Assert.Empty(model.ErrorMessage);
        Assert.False(model.IsBusy);
        Assert.False(model.RefreshCommand.CanExecute(null));
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task AccessDeniedRaisesEventAndLeavesNoEditableDetail()
    {
        using var handler = new CommunicationsHandler { DenyEmail = true };
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new EmailHistoryViewModel(client);
        var denied = false;
        model.AccessDenied += () =>
        {
            denied = true;
            model.Stop(true);

            return Task.CompletedTask;
        };
        await model.ActivateAsync();
        Assert.True(denied);
        Assert.Empty(model.Rows);
        Assert.Null(model.Detail);
        await model.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task CommunicationsViewsRenderListsAndDetailsAtDesktopWidths()
    {
        using var handler = new CommunicationsHandler();
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var email = new EmailHistoryViewModel(client);
        var support = new SupportMessagesViewModel(client);
        await email.ActivateAsync();
        await support.ActivateAsync();
        var window = new Window { Width = 1160, Height = 840, Content = new EmailHistoryView { DataContext = email } };
        window.Show();
        var pagination = CommunicationsViewModelTests.AssertTopPagination(window, "EmailHistoryList");
        var nextButton = pagination.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "→");
        var nextPoint = nextButton.TranslatePoint(new Point(nextButton.Bounds.Width / 2, nextButton.Bounds.Height / 2), window)!.Value;
        window.MouseDown(nextPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(nextPoint, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await email.NextPageCommand.ExecutionTask!;
        Assert.Equal(2, email.Page);
        Assert.Equal(email.PageLabel, pagination.PageLabel);
        CommunicationsViewModelTests.SaveScreenshot(window, "email-history-list.png");
        window.Width = 600;
        CommunicationsViewModelTests.SaveScreenshot(window, "email-history-narrow.png");
        CommunicationsViewModelTests.AssertTopPagination(window, "EmailHistoryList");
        email.SelectedRow = email.Rows[0];
        await email.OpenDetailCommand.ExecuteAsync(null);
        CommunicationsViewModelTests.SaveScreenshot(window, "email-history-detail.png");
        window.Content = new SupportMessagesView { DataContext = support };
        window.Width = 1160;
        CommunicationsViewModelTests.SaveScreenshot(window, "support-list.png");
        CommunicationsViewModelTests.AssertTopPagination(window, "SupportList");
        window.Width = 600;
        CommunicationsViewModelTests.SaveScreenshot(window, "support-narrow.png");
        CommunicationsViewModelTests.AssertTopPagination(window, "SupportList");
        support.SelectedRow = support.Rows[0];
        await support.OpenDetailCommand.ExecuteAsync(null);
        CommunicationsViewModelTests.SaveScreenshot(window, "support-detail.png");
        window.Close();
        await email.DeactivateAsync(true);
        await support.DeactivateAsync(true);
    }

    [Fact]
    public async Task SpamMustBeReclassifiedBeforeReplying()
    {
        using var handler = new CommunicationsHandler();
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new SupportMessagesViewModel(client);
        await model.ActivateAsync();
        model.SelectedRow = model.Rows[0];
        await model.OpenDetailCommand.ExecuteAsync(null);
        model.ReplyText = "Ответ";
        model.NextStatus = model.EditStatusOptions.Single(option => option.Code == "spam");
        await model.SaveDetailsCommand.ExecuteAsync(null);

        Assert.True(model.CanManage);
        Assert.False(model.CanReply);
        Assert.False(model.SendReplyCommand.CanExecute(null));
        Assert.DoesNotContain(handler.Requests, path => path.Contains("/replies", StringComparison.Ordinal));

        model.NextStatus = model.EditStatusOptions.Single(option => option.Code == "in_progress");
        await model.SaveDetailsCommand.ExecuteAsync(null);
        Assert.True(model.CanReply);
        Assert.True(model.SendReplyCommand.CanExecute(null));
        await model.DeactivateAsync(true);
    }
    [AvaloniaFact]
    public async Task SelectingNewStatusAppliesFiltersWithoutFindAndResetLoadsOnlyOnce()
    {
        using var handler = new CommunicationsHandler { InitialStatus = "closed" };
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new SupportMessagesViewModel(client);
        await model.ActivateAsync();
        var window = new Window { Width = 1160, Height = 840, Content = new SupportMessagesView { DataContext = model } };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();
            var statusSelect = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(control => ReferenceEquals(control.ItemsSource, model.StatusOptions));
            Assert.Single(model.Rows);
            statusSelect.Focus();
            statusSelect.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            CommunicationsViewModelTests.SaveScreenshot(window, "support-status-dropdown.png");
            window.KeyPress(Key.Down, RawInputModifiers.None, default, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, default, null);
            Dispatcher.UIThread.RunJobs();
            await model.ApplyFiltersCommand.ExecutionTask!;

            Assert.Equal("new", model.SelectedStatus.Code);
            Assert.Contains("status=new&", handler.Requests[^1]);
            Assert.Empty(model.Rows);
            Assert.Equal(0, model.TotalCount);
            Assert.True(model.IsEmpty);

            model.SelectedSource = model.SourceOptions.Single(option => option.Code == "users");
            await model.ApplyFiltersCommand.ExecutionTask!;
            Assert.Contains("source=users&", handler.Requests[^1]);
            var requestCount = handler.Requests.Count;
            await model.ResetFiltersCommand.ExecuteAsync(null);

            Assert.Equal(requestCount + 1, handler.Requests.Count);
            Assert.Equal("", model.SelectedStatus.Code);
            Assert.Equal("", model.SelectedSource.Code);
            Assert.Single(model.Rows);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }
    [AvaloniaFact]
    public async Task ListRowsKeepReadableSelectionAndOpenTheClickedEmail()
    {
        using var handler = new CommunicationsHandler { MultipleRows = true };
        using var client = CommunicationsViewModelTests.CreateClient(handler);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new EmailHistoryViewModel(client);
        await model.ActivateAsync();
        var view = new EmailHistoryView { DataContext = model };
        var window = new Window { Width = 1160, Height = 840, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var list = window.GetVisualDescendants().OfType<ListBox>().Single(control =>
            AutomationProperties.GetAutomationId(control) == "EmailHistoryList");
        var firstRow = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(0));
        var firstRowPoint = firstRow.TranslatePoint(new Point(20, 10), window)!.Value;
        window.MouseDown(firstRowPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(firstRowPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseMove(firstRowPoint);
        Dispatcher.UIThread.RunJobs();

        Assert.Same(model.Rows[0], model.SelectedRow);
        Assert.Equal(Color.Parse("#2B4358"), Assert.IsAssignableFrom<ISolidColorBrush>(firstRow.Background).Color);
        Assert.Equal(Color.Parse("#F1F1F1"), Assert.IsAssignableFrom<ISolidColorBrush>(firstRow.Foreground).Color);

        var secondRow = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
        var openButton = secondRow.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Открыть");
        var openPoint = openButton.TranslatePoint(new Point(openButton.Bounds.Width / 2, openButton.Bounds.Height / 2), window)!.Value;
        window.MouseDown(openPoint, MouseButton.Left, RawInputModifiers.None);
        window.MouseUp(openPoint, MouseButton.Left, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
        await model.OpenDetailCommand.ExecutionTask!;

        Assert.Equal(2, model.Detail!.Id);
        Assert.Contains(handler.Requests, path => path.EndsWith("/email-messages/2", StringComparison.Ordinal));
        window.Close();
        await model.DeactivateAsync(true);
    }

    private static ListPagination AssertTopPagination(Window window, string listId)
    {
        Dispatcher.UIThread.RunJobs();
        var pagination = window.GetVisualDescendants().OfType<ListPagination>().Single(control => control.IsEffectivelyVisible);
        var list = window.GetVisualDescendants().OfType<ListBox>().Single(control => AutomationProperties.GetAutomationId(control) == listId);
        var paginationBottom = pagination.TranslatePoint(new Point(0, pagination.Bounds.Height), window)!.Value.Y;
        Assert.True(paginationBottom <= list.TranslatePoint(default, window)!.Value.Y);
        return pagination;
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

    private static AdminSessionClient CreateClient(CommunicationsHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });

    private sealed class CommunicationsHandler : HttpMessageHandler
    {
        public bool CanManage { get; init; } = true;
        public bool MultipleRows { get; init; }
        public bool FailReply { get; set; }
        public bool DelayEmail { get; init; }
        public bool DenyEmail { get; init; }
        public string LastBody { get; private set; } = string.Empty;
        public List<string> Requests { get; } = [];
        public TaskCompletionSource EmailStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource EmailReleased { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private string _status = "new";
        private string _comment = string.Empty;
        private bool _hasReply;

        public string InitialStatus { init => this._status = value; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return CommunicationsHandler.Json(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid", roles = Array.Empty<string>() }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return CommunicationsHandler.Json(new AdminProfile
                {
                    Id = "admin", Permissions = this.CanManage
                        ? ["Admin.Access", "Admin.Communications.View", "Admin.Content.View", "Admin.Content.Manage"]
                        : ["Admin.Access", "Admin.Communications.View", "Admin.Content.View"]
                });
            }

            this.Requests.Add(request.RequestUri.PathAndQuery);
            if (path == "/api/v1/admin/email-messages")
            {
                if (this.DelayEmail)
                {
                    this.EmailStarted.TrySetResult();
                    await this.EmailReleased.Task;
                }

                if (this.DenyEmail)
                {
                    return new HttpResponseMessage(HttpStatusCode.Forbidden);
                }

                var emailRows = new List<EmailMessageListItem>
                {
                    new() { Id = 1, ToEmail = "contact@example.invalid", Subject = "Подтверждение записи", CreatedAt = DateTime.UtcNow, StatusCode = "delivered" }
                };
                if (this.MultipleRows)
                {
                    emailRows.Add(new EmailMessageListItem { Id = 2, ToEmail = "contact@example.invalid", Subject = "Вторая запись", CreatedAt = DateTime.UtcNow, StatusCode = "sent" });
                }

                return CommunicationsHandler.Json(new EmailMessagesPageResponse
                {
                    Items = emailRows,
                    TotalCount = 70, Page = request.RequestUri.Query.Contains("page=2") ? 2 : 1, PageSize = 50
                });
            }

            if (path is "/api/v1/admin/email-messages/1" or "/api/v1/admin/email-messages/2")
            {
                var messageId = path.EndsWith("/2", StringComparison.Ordinal) ? 2 : 1;
                return CommunicationsHandler.Json(new EmailMessageDetails
                {
                    Id = messageId, Subject = messageId == 1 ? "Подтверждение записи" : "Вторая запись",
                    ToEmail = messageId == 1 ? "contact@example.invalid" : "contact@example.invalid", StatusCode = "delivered", CreatedAt = DateTime.UtcNow,
                    HtmlBody = "<style>body{color:red}</style><p>Здравствуйте &amp; спасибо</p><script>alert('bad')</script>"
                });
            }

            if (path is "/api/v1/admin/email-messages/1/events" or "/api/v1/admin/email-messages/2/events")
            {
                return CommunicationsHandler.Json(new EmailMessageEventsResponse
                {
                    Items = [new EmailMessageEventItem { EventName = "delivered", Status = "delivered", Provider = "Email", EventAt = DateTime.UtcNow }]
                });
            }

            if (request.Method == HttpMethod.Put || request.Method == HttpMethod.Post)
            {
                this.LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
                if (request.Method == HttpMethod.Put)
                {
                    using var body = System.Text.Json.JsonDocument.Parse(this.LastBody);
                    this._status = body.RootElement.GetProperty("statusCode").GetString()!;
                    this._comment = body.RootElement.GetProperty("adminComment").GetString()!;
                }
                else if (this.FailReply)
                {
                    return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = JsonContent.Create(new { message = "Ошибка отправки ответа." }) };
                }
                else
                {
                    this._hasReply = true;
                }

                return CommunicationsHandler.Json(new CommunicationResult { Message = "Изменения сохранены." });
            }

            var message = new AdminSupportMessage
            {
                Id = 1, Topic = "Не открывается заказ", Email = "contact@example.invalid", Name = "Иван", OrganizationName = "Детейлинг",
                StatusCode = this._status, StatusName = this._status == "closed" ? "Закрыто" : "Новое", TypeName = "Вопрос", CreatedAt = DateTime.UtcNow,
                Text = "Помогите открыть заказ.", AdminComment = this._comment,
                Entries = this._hasReply
                    ? [new AdminSupportMessageEntry { Text = "Помогите открыть заказ.", CreatedAt = DateTime.UtcNow },
                        new AdminSupportMessageEntry { IsStaff = true, Kind = "guest_email_reply", Text = "Проблема устранена.", DeliveryStatus = "queued", CreatedAt = DateTime.UtcNow }]
                    : []
            };

            if (path == "/api/v1/admin/support-messages/1")
            {
                return CommunicationsHandler.Json(message);
            }

            var query = request.RequestUri.Query.TrimStart('?').Split('&');
            var requestedStatus = query.Single(value => value.StartsWith("status=", StringComparison.Ordinal))[7..];
            var requestedSource = query.Single(value => value.StartsWith("source=", StringComparison.Ordinal))[7..];
            var hasMatchingMessage = (requestedStatus.Length == 0 || requestedStatus == this._status) && requestedSource != "users";

            return CommunicationsHandler.Json(new AdminSupportMessagesPage
            {
                Items = hasMatchingMessage ? [message] : [],
                TotalCount = hasMatchingMessage ? 70 : 0,
                PageSize = 50,
                NewCount = this._status == "new" ? 1 : 0
            });
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
