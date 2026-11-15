using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class OutboundCommunicationsTests
{
    [Fact]
    public async Task SelectingSpecificAudienceReplacesAllUsersAndCanBeReset()
    {
        using var handler = new OutboundCommunicationsHandler();
        using var client = await OutboundCommunicationsTests.CreateClientAsync(handler);
        var model = new SiteNotificationsViewModel(client);
        var all = model.AudienceOptions.Single(option => option.Code == "all");
        var administrators = model.AudienceOptions.Single(option => option.Code == "admins");

        Assert.True(all.IsSelected);
        administrators.IsSelected = true;
        Assert.False(all.IsSelected);
        Assert.True(administrators.IsSelected);
        all.IsSelected = true;
        Assert.False(administrators.IsSelected);
        Assert.True(all.IsSelected);
    }

    [Fact]
    public async Task EmailComposerPreviewsRecipientsAndSchedulesOnlyAfterPreview()
    {
        using var handler = new OutboundCommunicationsHandler();
        using var client = await OutboundCommunicationsTests.CreateClientAsync(handler);
        var model = new EmailComposeViewModel(client);
        await model.ActivateAsync();
        model.TargetMode = model.TargetModeOptions.Single(option => option.Code == "email");
        model.Email = "contact@example.invalid";
        model.Subject = "Тема";
        model.Body = "Строка <script>alert(1)</script>";

        Assert.False(model.ScheduleCommand.CanExecute(null));
        await model.PreviewRecipientsCommand.ExecuteAsync(null);
        Assert.Equal(1, model.Recipients!.TotalCount);
        Assert.Contains("&lt;script&gt;", model.PreviewHtml);
        Assert.True(model.ScheduleCommand.CanExecute(null));

        model.StartDelayMinutes = 12;
        Assert.Equal("Строка <script>alert(1)</script>", model.Body);
        await model.ScheduleCommand.ExecuteAsync(null);

        Assert.True(model.IsScheduled);
        Assert.Equal(1, model.ScheduledCount);
        Assert.Contains(handler.Requests, request => request.EndsWith("/schedule", StringComparison.Ordinal));
        using var payload = JsonDocument.Parse(handler.LastBody);
        var htmlBody = payload.RootElement.GetProperty("htmlBody").GetString();
        Assert.Contains("&lt;script&gt;", htmlBody);
        Assert.Equal(12, payload.RootElement.GetProperty("startDelayMinutes").GetInt32());
        Assert.All(handler.AuthorizationSchemes, scheme => Assert.Equal("Bearer", scheme));
        Assert.False(model.ScheduleCommand.CanExecute(null));
        await model.DeactivateAsync(true);
        Assert.Empty(model.Body);
        Assert.Empty(model.Subject);
    }

    [Fact]
    public async Task CampaignActionsUsePauseResumeAndCancelEndpoints()
    {
        using var handler = new OutboundCommunicationsHandler();
        using var client = await OutboundCommunicationsTests.CreateClientAsync(handler);
        var model = new EmailCampaignsViewModel(client);
        await model.ActivateAsync();

        Assert.Single(model.Campaigns);
        await model.PauseCommand.ExecuteAsync(model.Campaigns[0]);
        await model.ResumeCommand.ExecuteAsync(model.Campaigns[0]);
        await model.CancelCommand.ExecuteAsync(model.Campaigns[0]);

        Assert.Contains("/api/v1/admin/email-messages/campaigns/00000000-0000-0000-0000-000000000001/pause", handler.Requests);
        Assert.Contains("/api/v1/admin/email-messages/campaigns/00000000-0000-0000-0000-000000000001/resume", handler.Requests);
        Assert.Contains("/api/v1/admin/email-messages/campaigns/00000000-0000-0000-0000-000000000001/cancel", handler.Requests);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task SmsHistoryLoadsRowsAndFiltersLocally()
    {
        using var handler = new OutboundCommunicationsHandler();
        using var client = await OutboundCommunicationsTests.CreateClientAsync(handler);
        var model = new SmsHistoryViewModel(client);
        await model.ActivateAsync();

        Assert.Single(model.Rows);
        Assert.Equal("Подтверждение телефона", model.Rows[0].Type);
        model.Search = "000";
        Assert.Single(model.FilteredRows);
        model.Search = "не найдено";
        Assert.Empty(model.FilteredRows);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task SiteNotificationRequiresConfirmationAndSendsChosenUserWithAudience()
    {
        using var handler = new OutboundCommunicationsHandler();
        using var client = await OutboundCommunicationsTests.CreateClientAsync(handler);
        var model = new SiteNotificationsViewModel(client);
        await model.ActivateAsync();
        model.RecipientScope = model.RecipientScopes.Single(option => option.Code == "selected");
        model.UserSearch = "contact@example.invalid";
        await model.SearchUsersCommand.ExecuteAsync(null);
        model.FoundUsers[0].IsSelected = true;
        model.SendTitle = "Технические работы";
        model.SendBody = "Сайт будет обновлён.";

        Assert.True(model.RequestSendCommand.CanExecute(null));
        Assert.False(model.ConfirmSendCommand.CanExecute(null));
        await model.ConfirmSendCommand.ExecuteAsync(null);
        Assert.DoesNotContain(handler.Requests, request => request.EndsWith("/send", StringComparison.Ordinal));
        model.RequestSendCommand.Execute(null);
        Assert.DoesNotContain(handler.Requests, request => request.EndsWith("/send", StringComparison.Ordinal));

        await model.ConfirmSendCommand.ExecuteAsync(null);

        Assert.Contains(handler.Requests, request => request.EndsWith("/send", StringComparison.Ordinal));
        Assert.Contains("\"recipientScope\":\"selected\"", handler.LastBody);
        Assert.Contains("\"audiences\":[\"all\"]", handler.LastBody);
        Assert.Contains("\"userIds\":[\"user-1\"]", handler.LastBody);
        await model.DeactivateAsync(true);
        Assert.Empty(model.SendBody);
    }

    [Fact]
    public async Task SiteNotificationPreservesSelectionsAndBlocksRetryAfterUncertainSend()
    {
        using var handler = new OutboundCommunicationsHandler();
        using var client = await OutboundCommunicationsTests.CreateClientAsync(handler);
        var model = new SiteNotificationsViewModel(client);
        await model.ActivateAsync();
        model.RecipientScope = model.RecipientScopes.Single(option => option.Code == "selected");
        model.UserSearch = "client";
        await model.SearchUsersCommand.ExecuteAsync(null);
        model.FoundUsers[0].IsSelected = true;
        model.UserSearch = "second";
        await model.SearchUsersCommand.ExecuteAsync(null);
        model.FoundUsers[0].IsSelected = true;

        Assert.Equal(new[] { "user-1", "user-2" }, model.SelectedUsers.Select(user => user.Id).Order().ToArray());
        model.RemoveSelectedUserCommand.Execute(model.SelectedUsers.Single(user => user.Id == "user-1"));
        Assert.Equal("user-2", Assert.Single(model.SelectedUsers).Id);

        model.SendTitle = "Проверка";
        model.SendBody = "Текст";
        handler.FailNextSiteNotificationSend = true;
        model.RequestSendCommand.Execute(null);
        await model.ConfirmSendCommand.ExecuteAsync(null);

        Assert.True(model.IsSendAttempted);
        Assert.Contains("Проверьте историю", model.SendWarning);
        Assert.False(model.ConfirmSendCommand.CanExecute(null));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.RequestSendCommand.CanExecute(null));
        Assert.Single(handler.Requests, request => request.EndsWith("/send", StringComparison.Ordinal));

        await model.DeactivateAsync(true);
        await model.ActivateAsync();
        Assert.True(model.IsSendAttempted);
        Assert.Contains("Проверьте историю", model.SendWarning);
        Assert.False(model.RequestSendCommand.CanExecute(null));

        await model.CheckHistoryAndStartNewCommand.ExecuteAsync(null);

        Assert.False(model.IsSendAttempted);
        Assert.Empty(model.SendTitle);
        Assert.Empty(model.SendBody);
        Assert.Contains("История уведомлений обновлена", model.Notice);
        Assert.Single(handler.Requests, request => request.EndsWith("/send", StringComparison.Ordinal));
        using var requestPayload = JsonDocument.Parse(handler.LastBody);
        Assert.Equal("user-2", Assert.Single(requestPayload.RootElement.GetProperty("userIds").EnumerateArray()).GetString());
        await model.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task CommunicationScreensRenderAtDesktopAndNarrowWidths()
    {
        using var handler = new OutboundCommunicationsHandler();
        using var client = await OutboundCommunicationsTests.CreateClientAsync(handler);
        var compose = new EmailComposeViewModel(client);
        var campaigns = new EmailCampaignsViewModel(client);
        var sms = new SmsHistoryViewModel(client);
        var notifications = new SiteNotificationsViewModel(client);
        var screens = new (string Name, UserControl View, CommunicationsViewModel Model)[]
        {
            ("email-compose", new EmailComposeView { DataContext = compose }, compose),
            ("email-campaigns", new EmailCampaignsView { DataContext = campaigns }, campaigns),
            ("sms-history", new SmsHistoryView { DataContext = sms }, sms),
            ("site-notifications", new SiteNotificationsView { DataContext = notifications }, notifications)
        };

        foreach (var (name, view, model) in screens)
        {
            var window = new Window { Width = 1160, Height = 920, Content = view };
            try
            {
                window.Show();
                await model.ActivateAsync();
                if (ReferenceEquals(model, compose))
                {
                    compose.TargetMode = compose.TargetModeOptions.Single(option => option.Code == "email");
                    compose.Email = "contact@example.invalid";
                    compose.Subject = "Плановое обслуживание";
                    compose.Body = "Здравствуйте!\n\nВажная информация для вас.";
                    await compose.PreviewRecipientsCommand.ExecuteAsync(null);
                }

                Dispatcher.UIThread.RunJobs();
                OutboundCommunicationsTests.SaveScreenshot(window, $"{name}-1160.png");
                Assert.True(view.Bounds.Width > 0);

                window.Width = 592;
                Dispatcher.UIThread.RunJobs();
                OutboundCommunicationsTests.SaveScreenshot(window, $"{name}-592.png");
                Assert.True(view.Bounds.Width >= 590);
                Assert.Contains(view.GetVisualDescendants().OfType<Button>(), button => button.Bounds.Width > 0);
            }
            finally
            {
                window.Close();
                await model.DeactivateAsync(true);
            }
        }
    }

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
        using var bitmap = window.CaptureRenderedFrame();
        Assert.NotNull(bitmap);
        using var stream = File.Create(Path.Combine(directory, fileName));
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private static async Task<AdminSessionClient> CreateClientAsync(OutboundCommunicationsHandler handler)
    {
        var client = new AdminSessionClient(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);

        return client;
    }

    private sealed class OutboundCommunicationsHandler : HttpMessageHandler
    {
        private static readonly DateTime s_now = new(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc);

        public List<string> Requests { get; } = [];
        public List<string?> AuthorizationSchemes { get; } = [];
        public string LastBody { get; private set; } = string.Empty;
        public bool FailNextSiteNotificationSend { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return OutboundCommunicationsHandler.Json(new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid", roles = Array.Empty<string>() }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return OutboundCommunicationsHandler.Json(new AdminProfile
                {
                    Id = "admin",
                    IsSystem = true,
                    Permissions = ["Admin.Access", "Admin.Communications.View", "Admin.Communications.Manage"]
                });
            }

            this.Requests.Add(request.RequestUri.PathAndQuery);
            this.AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
            if (request.Content != null)
            {
                this.LastBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (path.EndsWith("/compose-data", StringComparison.Ordinal))
            {
                return OutboundCommunicationsHandler.Json(new AdminEmailComposeData
                {
                    OrganizationTypes = [new AdminEmailOrganizationType { Code = "detailing", Name = "Детейлинг" }],
                    Users = [new AdminEmailUser { Id = "user-1", Email = "contact@example.invalid", DisplayName = "Клиент" }]
                });
            }

            if (path.EndsWith("/preview-recipients", StringComparison.Ordinal))
            {
                return OutboundCommunicationsHandler.Json(new AdminEmailRecipientsPreview
                {
                    TotalCount = 1,
                    Items = [new AdminEmailRecipientPreviewItem { UserId = "user-1", Email = "contact@example.invalid", DisplayName = "Клиент" }]
                });
            }

            if (path.EndsWith("/schedule", StringComparison.Ordinal))
            {
                return OutboundCommunicationsHandler.Json(new AdminEmailCampaignScheduleResponse { ScheduledCount = 1 });
            }

            if (path.EndsWith("/campaigns", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                return OutboundCommunicationsHandler.Json(new AdminEmailCampaignsPage
                {
                    Page = 1,
                    PageSize = 20,
                    TotalCount = 1,
                    Items = [new AdminEmailCampaign
                    {
                        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                        Subject = "Тестовая кампания",
                        StatusCode = "running",
                        TargetMode = "all",
                        StartAt = OutboundCommunicationsHandler.s_now,
                        TotalCount = 1
                    }]
                });
            }

            if (path == "/api/v1/admin/sms-sending-histories")
            {
                return OutboundCommunicationsHandler.Json(new AdminSmsHistoryPage
                {
                    TotalCount = 1,
                    Histories = [new AdminSmsHistoryItem
                    {
                        Id = 1, Phone = "+7 000 000-00-01", Text = "Код: 1234", SmsCode = "1234",
                        DateTime = OutboundCommunicationsHandler.s_now, Type = 10
                    }]
                });
            }

            if (path == "/api/v1/admin/site-notifications/users")
            {
                var user = request.RequestUri.Query.Contains("second", StringComparison.Ordinal)
                    ? new AdminSiteNotificationUser { Id = "user-2", Email = "contact@example.invalid" }
                    : new AdminSiteNotificationUser { Id = "user-1", Email = "contact@example.invalid" };
                return OutboundCommunicationsHandler.Json(new[] { user });
            }

            if (path is "/api/v1/admin/site-notifications" or "/api/v1/admin/site-notifications/templates" or "/api/v1/admin/site-notifications/history" && request.Method == HttpMethod.Get)
            {
                var items = new[]
                {
                    new AdminSiteNotification { Id = Guid.Parse("00000000-0000-0000-0000-000000000011"), Title = "Демо-период завершён", Body = "Проверьте тариф организации.", TypeCode = "subscription-ended", IsTemplate = true },
                    new AdminSiteNotification { Id = Guid.Parse("00000000-0000-0000-0000-000000000012"), Title = "Плановое обслуживание", Body = "Сайт будет обновлён сегодня в 22:00.", TypeCode = "maintenance", CreatedAt = OutboundCommunicationsHandler.s_now, RecipientEmail = "contact@example.invalid" }
                };
                return path.EndsWith("/history", StringComparison.Ordinal)
                    ? OutboundCommunicationsHandler.Json(new AdminSiteNotificationHistoryPage { Items = items.Where(item => !item.IsTemplate).ToArray(), TotalCount = 1, Page = 1, PageSize = 20 })
                    : OutboundCommunicationsHandler.Json(path.EndsWith("/templates", StringComparison.Ordinal) ? items.Where(item => item.IsTemplate).ToArray() : items);
            }

            if (path == "/api/v1/admin/site-notifications/send" && this.FailNextSiteNotificationSend)
            {
                this.FailNextSiteNotificationSend = false;
                throw new HttpRequestException("Ответ потерян после отправки.");
            }

            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json<T>(T value) =>
            new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
