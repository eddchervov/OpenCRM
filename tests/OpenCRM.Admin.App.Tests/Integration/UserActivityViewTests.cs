using System.Net;
using System.Net.Http.Json;
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

public sealed class UserActivityViewTests
{
    [AvaloniaFact]
    public async Task UserPickerSearchesSelectsAndClearsUserThroughBindings()
    {
        var activityQueries = new List<string>();
        using var http = new HttpClient(new ActivityApiHandler(activityQueries)) { BaseAddress = new Uri("https://opencrm.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(client);
        await model.ActivateAsync();
        var view = new UserActivityView { DataContext = model };
        var window = new Window { Width = 1160, Height = 800, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var picker = view.FindControl<AutoCompleteBox>("ActivityUserPicker")!;
            picker.MinimumPopulateDelay = TimeSpan.Zero;
            picker.Text = "client@";
            for (var attempt = 0; attempt < 100 && model.Users.Count == 0; attempt++)
            {
                await Task.Delay(10, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }

            var user = Assert.Single(model.Users);
            Assert.Equal("contact@example.invalid", user.Label);
            picker.SelectedItem = user;
            Dispatcher.UIThread.RunJobs();
            await model.ApplyFiltersCommand.ExecuteAsync(null);
            Assert.Contains("userId=user-1", activityQueries[^1]);

            picker.Text = "another@";
            Dispatcher.UIThread.RunJobs();
            Assert.Null(model.SelectedUser);
            var count = activityQueries.Count;
            await model.ApplyFiltersCommand.ExecuteAsync(null);
            Assert.Equal(count, activityQueries.Count);
            Assert.Contains("Выберите пользователя", model.ErrorMessage);

            model.ClearUserCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            await model.ApplyFiltersCommand.ExecuteAsync(null);
            Assert.DoesNotContain("userId=", activityQueries[^1]);
            Assert.True(string.IsNullOrEmpty(picker.Text));
        }
        finally
        {
            window.Close();
            await model.DeactivateAsync(true);
        }
    }

    [AvaloniaFact]
    public async Task ActivityPageShowsFourReadableColumnsWithoutTechnicalDetails()
    {
        using var http = new HttpClient(new ActivityApiHandler([])) { BaseAddress = new Uri("https://opencrm.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new UserActivityViewModel(client)
        {
            RetentionDays = 90,
            Enabled = true
        };
        await model.ActivateAsync();
        model.Items = Enumerable.Range(1, 4)
            .Select(index => new UserActivityRow(new AdminUserActivityItem(
                index,
                index < 3 ? "user-activity-1" : "user-activity-2",
                index < 3 ? "contact@example.invalid" : "contact@example.invalid",
                Guid.Parse("c69ecf33-b6e3-4dde-a57a-99e967a7c118"),
                index < 3 ? "Студия «Блеск»" : "Автосервис «Мастер»",
                Guid.Parse("c75ecf33-b6e3-4dde-a57a-99e967a7c118"),
                DateTime.UtcNow.AddMinutes(-index),
                DateTime.UtcNow.AddMinutes(-index),
                "click",
                index % 2 == 1 ? "create_order" : "open_clients",
                $"/clients/{(index < 3 ? "detailing" : "autoservice")}/{(index % 2 == 1 ? "orders/{id}" : "clients")}",
                index < 3 ? "detailing" : "autoservice",
                index % 2 == 1 ? "orders" : "clients",
                $"order-{index}")))
            .ToArray();
        model.Users = [new ActivityUserOption("user-activity-1", "contact@example.invalid")];
        var window = new Window { Width = 1160, Height = 800, Content = new UserActivityView { DataContext = model } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            var renderedText = window.GetVisualDescendants().OfType<TextBlock>().Select(textBlock => textBlock.Text).ToArray();
            Assert.Contains("Создание заказа", renderedText);
            Assert.Contains("Детейлинг · Заказы", renderedText);
            Assert.Contains("contact@example.invalid", renderedText);
            Assert.Contains("Студия «Блеск»", renderedText);
            Assert.Contains("Когда", renderedText);
            Assert.Contains("Организация", renderedText);
            Assert.DoesNotContain(renderedText, text => text != null &&
                (text.Contains("user-activity-1") || text.Contains("c75ecf33") || text.Contains("target:") ||
                 text.Contains("Получено:") || text.Contains("/clients/") || text.Contains("Код:")));
            var picker = window.GetVisualDescendants().OfType<AutoCompleteBox>().Single();
            Assert.True(picker.Bounds.Width > 220);
            picker.SelectedItem = model.Users[0];
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("user-activity-1", model.SelectedUser?.Id);
            model.ClearUserCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(picker.SelectedItem);
            Assert.True(string.IsNullOrEmpty(picker.Text));

            var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            Directory.CreateDirectory(directory);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            using var output = File.Create(Path.Combine(directory, "user-activity-1160.png"));
            frame.Save(output, PngBitmapEncoderOptions.Default);
        }
        finally
        {
            window.Close();
            await model.DeactivateAsync(true);
        }
    }

    private sealed class ActivityApiHandler(List<string> activityQueries) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            object result = request.RequestUri!.AbsolutePath switch
            {
                "/api/v1/auth/login" => new
                {
                    accessToken = "access-token", accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
                    refreshToken = "refresh", refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    tokenType = "Bearer", user = new { id = "admin", roles = new[] { "Admin" } }
                },
                "/api/v1/admin/current-user" => new { id = "admin", roles = new[] { "Admin" }, permissions = new[] { "Admin.Access", "Admin.Users.View" } },
                "/api/v1/admin/users" => new AdminUsersResponse
                {
                    Users = [new AdminUserListItem { Id = "user-1", Email = "contact@example.invalid" }]
                },
                _ => new AdminUserActivityPage(true, 90, [], null)
            };
            if (request.RequestUri.AbsolutePath == "/api/v1/admin/user-activity")
            {
                activityQueries.Add(request.RequestUri.Query);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(result) });
        }
    }
}
