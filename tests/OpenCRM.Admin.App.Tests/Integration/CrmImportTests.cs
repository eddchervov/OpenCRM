using System.Net;
using System.Net.Http.Json;
using System.Text;
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

public sealed class CrmImportTests
{
    [AvaloniaFact]
    public async Task PreviewRequiresExplicitConfirmationAndDownloadsBothReports()
    {
        using var handler = new ImportHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new CrmImportViewModel(client);
        var window = new Window { Width = 1100, Height = 950, Content = new CrmImportView { DataContext = model } };
        window.Show();
        try
        {
            await model.ActivateAsync();
            model.SelectedUser = Assert.Single(model.Users);
            await model.LoadOrganizationsCommand.ExecuteAsync(null);
            Assert.Equal(ImportHandler.OrganizationId, model.SelectedOrganization!.Id);
            model.SetFile("clients.csv", Encoding.UTF8.GetBytes("name,phone\nКлиент,123"));
            await model.PreviewFileCommand.ExecuteAsync(null);

            Assert.Equal(0, handler.ConfirmCount);
            Assert.NotNull(model.Preview);
            Assert.False(model.ConfirmCommand.CanExecute(null));
            Assert.Contains("userId", handler.UploadBody);
            Assert.Contains("target-user", handler.UploadBody);
            Assert.Contains(ImportHandler.OrganizationId.ToString(), handler.UploadBody);
            Assert.Contains("Клиент", handler.UploadBody);
            var previewReport = await model.DownloadReportAsync(false);
            Assert.Equal("preview csv", Encoding.UTF8.GetString(previewReport!));

            model.RequestConfirmationCommand.Execute(null);
            Assert.True(model.IsConfirmationOpen);
            await model.ConfirmCommand.ExecuteAsync(null);
            Assert.Equal(1, handler.ConfirmCount);
            Assert.True(model.HasResultReport);
            Assert.False(model.RequestConfirmationCommand.CanExecute(null));
            Assert.Equal("result csv", Encoding.UTF8.GetString((await model.DownloadReportAsync(true))!));
            Dispatcher.UIThread.RunJobs();
            var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                foreach (var width in new[] { 1100, 592 })
                {
                    window.Width = width;
                    Dispatcher.UIThread.RunJobs();
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    using var output = File.Create(Path.Combine(directory, $"crm-import-{width}.png"));
                    frame.Save(output, PngBitmapEncoderOptions.Default);
                }
            }

            model.SelectedOrganization = null;
            Assert.Null(model.Preview);
            Assert.Null(model.Result);
            Assert.Empty(model.FileName);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [Fact]
    public async Task MultipartUploadIsRecreatedAfterAccessTokenRefresh()
    {
        using var handler = new ImportHandler { RejectFirstUpload = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var preview = await client.CreateCrmImportPreviewAsync("target-user", ImportHandler.OrganizationId,
            "данные.csv", Encoding.UTF8.GetBytes("Клиент"), TestContext.Current.CancellationToken);

        Assert.NotEqual(Guid.Empty, preview.Id);
        Assert.Equal(2, handler.UploadCount);
        Assert.Equal(1, handler.RefreshCount);
        Assert.Contains("Клиент", handler.UploadBody);
    }

    [Fact]
    public async Task FailedConfirmationBlocksRetryUntilServerStateIsRefreshed()
    {
        using var handler = new ImportHandler { FailConfirmation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new CrmImportViewModel(client);
        await model.ActivateAsync();
        model.SelectedUser = model.Users[0];
        await model.LoadOrganizationsCommand.ExecuteAsync(null);
        model.SetFile("clients.csv", [1]);
        await model.PreviewFileCommand.ExecuteAsync(null);
        model.RequestConfirmationCommand.Execute(null);
        await model.ConfirmCommand.ExecuteAsync(null);
        Assert.True(model.IsOutcomeUnknown);
        Assert.False(model.RequestConfirmationCommand.CanExecute(null));
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.IsOutcomeUnknown);
        Assert.True(model.HasResultReport);
        Assert.False(model.RequestConfirmationCommand.CanExecute(null));
        Assert.Equal(1, handler.ConfirmCount);
        await model.DeactivateAsync(true);
    }

    private sealed class ImportHandler : HttpMessageHandler
    {
        public static Guid OrganizationId { get; } = Guid.Parse("524d7b07-edfd-4d3a-88fc-e00c7995407f");
        private static readonly Guid s_previewId = Guid.Parse("b4be284e-4f7e-4c58-b20d-e329c77d157f");
        public bool FailConfirmation { get; init; }
        public bool RejectFirstUpload { get; init; }
        public int UploadCount { get; private set; }
        public int RefreshCount { get; private set; }
        public int ConfirmCount { get; private set; }
        public string UploadBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                if (path == "/api/v1/auth/refresh")
                {
                    this.RefreshCount++;
                }

                return Json(new { accessToken = "access-token", refreshToken = "refresh", tokenType = "Bearer",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1), refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", roles = Array.Empty<string>() } });
            }

            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            if (path == "/api/v1/admin/current-user")
            {
                return Json(new AdminProfile { Id = "admin", IsSystem = true });
            }

            if (path.EndsWith("/users", StringComparison.Ordinal))
            {
                return Json(new[] { new AdminCrmImportUser("target-user", "contact@example.invalid", null) });
            }

            if (path.EndsWith("/organizations", StringComparison.Ordinal))
            {
                return Json(new[] { new AdminCrmImportOrganization(OrganizationId, "Студия", "contact@example.invalid") });
            }

            if (path.EndsWith("/confirm", StringComparison.Ordinal))
            {
                this.ConfirmCount++;
                return this.FailConfirmation ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    : Json(new AdminCrmImportResult(1, 0, 0, 0, s_previewId));
            }

            if (path.EndsWith("/report", StringComparison.Ordinal) || path.EndsWith("/result-report", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(path.EndsWith("/result-report", StringComparison.Ordinal) ? "result csv" : "preview csv")
                };
            }

            if (path.EndsWith("/previews", StringComparison.Ordinal))
            {
                Assert.IsType<MultipartFormDataContent>(request.Content);
                this.UploadBody = await request.Content.ReadAsStringAsync(cancellationToken);
                this.UploadCount++;
                if (this.RejectFirstUpload && this.UploadCount == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
            }

            return Json(new AdminCrmImportPreview(s_previewId, "clients.csv", this.ConfirmCount > 0,
                [new(1, "client", "Клиент", "123", null, null, null, null, "create", null)], new(1, 0, 1, 0, 0)));
        }

        private static HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };
    }
}
