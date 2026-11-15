using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class EmailComposeUserSearchTests
{
    [AvaloniaFact]
    public async Task UserPickerSelectsUserAndTextPreviewKeepsFullWidth()
    {
        using var http = CreateClient((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/v1/auth/login" => JsonResponse(HttpStatusCode.OK, CreateTokens()),
            "/api/v1/admin/current-user" => JsonResponse(HttpStatusCode.OK, CreateProfile()),
            _ => JsonResponse(HttpStatusCode.OK, new { organizationTypes = Array.Empty<object>(),
                users = new[] { request.RequestUri.Query.Contains("second", StringComparison.Ordinal)
                    ? CreateUser("second", "contact@example.invalid", "+7 000 000-00-01")
                    : CreateUser("first", "contact@example.invalid", "+7 000 000-00-01") } })
        });
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new EmailComposeViewModel(client);
        var view = new EmailComposeView { DataContext = model };
        var window = new Window { Width = 1160, Height = 1000, Content = view };
        window.Show();
        try
        {
            await model.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            var picker = view.FindControl<AutoCompleteBox>("ComposeUserPicker")!;
            Assert.True(picker.IsEffectivelyVisible);
            Assert.NotNull(picker.AsyncPopulator);
            picker.SelectedItem = model.Users[0];
            Assert.Same(model.Users[0], model.SelectedUser);
            var input = picker.GetVisualDescendants().OfType<TextBox>().Single();
            input.Focus();
            input.SelectAll();
            window.KeyTextInput("second");
            for (var attempt = 0; attempt < 40 && !model.Users.Any(user => user.Id == "second"); attempt++)
            {
                await Task.Delay(25, TestContext.Current.CancellationToken);
                Dispatcher.UIThread.RunJobs();
            }

            Assert.Contains(model.Users, user => user.Id == "second");
            Assert.True(picker.IsDropDownOpen);
            window.KeyPress(Key.Down, RawInputModifiers.None, default, null);
            window.KeyPress(Key.Enter, RawInputModifiers.None, default, null);
            Assert.Equal("second", model.SelectedUser?.Id);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Найти");
            model.Subject = "Проверка письма";
            model.Body = "Первая строка\nВторая строка с текстом письма";
            foreach (var width in new[] { 1160, 592 })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var editor = view.GetVisualDescendants().OfType<TextBox>()
                    .Single(control => Avalonia.Automation.AutomationProperties.GetAutomationId(control) == "ComposeBody");
                var preview = view.GetVisualDescendants().OfType<SelectableTextBlock>()
                    .Single(control => Avalonia.Automation.AutomationProperties.GetAutomationId(control) == "ComposeTextPreview");
                Assert.True(editor.AcceptsReturn);
                Assert.True(editor.Padding.Top >= 10);
                Assert.True(preview.Bounds.Width >= editor.Bounds.Width - 60);
                Assert.True(preview.IsEffectivelyVisible);
                var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    using var output = File.Create(Path.Combine(directory, $"compose-search-preview-{width}.png"));
                    frame.Save(output, PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally
        {
            window.Close();
            await model.DeactivateAsync(true);
        }
    }

    [Fact]
    public async Task PopulateUsersSearchesServerAndKeepsSelectedUserInOptions()
    {
        var searches = new List<string>();
        using var httpClient = CreateClient((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/email-messages/compose-data")
            {
                var search = GetQueryValue(request.RequestUri.Query, "userSearch");
                searches.Add(search);
                return JsonResponse(HttpStatusCode.OK, new
                {
                    organizationTypes = Array.Empty<object>(),
                    users = search.Length == 0
                        ? new[] { CreateUser("first", "contact@example.invalid", "+7 000 000-00-01") }
                        : new[] { CreateUser("second", "contact@example.invalid", "+7 000 000-00-01") }
                });
            }

            throw new InvalidOperationException("Неожиданный запрос.");
        });
        using var sessionClient = new AdminSessionClient(httpClient);
        await sessionClient.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var viewModel = new EmailComposeViewModel(sessionClient);
        await viewModel.ActivateAsync();
        viewModel.SelectedUser = viewModel.Users.Single();

        var results = await viewModel.PopulateUsers("+7 000 000-00-01", CancellationToken.None);

        Assert.Equal([string.Empty, "+7 000 000-00-01"], searches);
        Assert.Equal("second", Assert.IsType<OpenCRM.Admin.Api.Models.AdminEmailUser>(Assert.Single(results)).Id);
        Assert.Equal("first", viewModel.SelectedUser?.Id);
        Assert.Contains(viewModel.Users, user => user.Id == viewModel.SelectedUser?.Id);
        Assert.Equal("+7 000 000-00-01", viewModel.Users[0].Phone);

        await viewModel.DeactivateAsync();
    }

    [Fact]
    public async Task PopulateUsersStopsWhenViewModelIsStopped()
    {
        var requestStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var httpClient = CreateClient(async (request, cancellationToken) =>
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, CreateProfile());
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/email-messages/compose-data"
                && GetQueryValue(request.RequestUri.Query, "userSearch").Length == 0)
            {
                return JsonResponse(HttpStatusCode.OK, new { organizationTypes = Array.Empty<object>(), users = Array.Empty<object>() });
            }

            requestStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, new { organizationTypes = Array.Empty<object>(), users = Array.Empty<object>() });
        });
        using var sessionClient = new AdminSessionClient(httpClient);
        await sessionClient.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var viewModel = new EmailComposeViewModel(sessionClient);
        await viewModel.ActivateAsync();

        var populateTask = viewModel.PopulateUsers("person", CancellationToken.None);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        viewModel.Stop();
        var results = await populateTask;

        Assert.Empty(results);
        Assert.False(viewModel.IsSearching);

        await viewModel.DeactivateAsync();
    }

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) =>
        new(new FakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };

    private static HttpClient CreateClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) =>
        new(new AsyncFakeHttpMessageHandler(handler)) { BaseAddress = new Uri("https://opencrm.test/") };

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) =>
        new(statusCode) { Content = JsonContent.Create(value) };

    private static object CreateTokens() => new
    {
        accessToken = "access-token",
        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        refreshToken = "refresh-token",
        refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(10),
        tokenType = "Bearer",
        user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
    };

    private static object CreateProfile() => new
    {
        id = "admin-1",
        email = "contact@example.invalid",
        roles = new[] { "Admin" },
        permissions = new[] { "Admin.Access" },
        isSystem = true
    };

    private static object CreateUser(string id, string email, string phone) => new
    {
        id,
        email,
        phone,
        displayName = email,
        emailConfirmed = true,
        advertisingConsentAccepted = false
    };

    private static string GetQueryValue(string query, string name)
    {
        var pair = query.TrimStart('?').Split('&').FirstOrDefault(value => value.StartsWith(name + "=", StringComparison.Ordinal));
        return pair == null ? string.Empty : Uri.UnescapeDataString(pair[(name.Length + 1)..]);
    }

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request, cancellationToken));
    }

    private sealed class AsyncFakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
