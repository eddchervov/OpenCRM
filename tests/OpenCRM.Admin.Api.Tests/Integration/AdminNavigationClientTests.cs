using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api.Tests.Integration;

public sealed class AdminNavigationClientTests
{
    [Fact]
    public async Task UnreadCountsAndViewedMarkerUseUtcRoundTripValues()
    {
        var handler = new NavigationClientHandler();
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(httpClient);
        await client.LoginAsync("contact@example.invalid", "password");
        var fromUtc = new DateTime(2026, 10, 14, 17, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddDays(1);
        var viewedThroughUtc = new DateTime(2026, 10, 15, 7, 30, 0, DateTimeKind.Utc).AddTicks(1234560);

        var counts = await client.GetUnreadNavigationCountsAsync(fromUtc, toUtc);
        var result = await client.MarkNavigationSectionViewedAsync("supportMessages", viewedThroughUtc);

        Assert.Equal(4, counts.SupportMessages);
        Assert.Equal(viewedThroughUtc.ToString("O", CultureInfo.InvariantCulture), counts.AsOfUtc?.ToString("O", CultureInfo.InvariantCulture));
        Assert.Equal("Просмотр раздела сохранён.", result.Message);
        Assert.Equal(
            $"?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O", CultureInfo.InvariantCulture))}"
                + $"&toUtc={Uri.EscapeDataString(toUtc.ToString("O", CultureInfo.InvariantCulture))}&unread=true",
            handler.CountsUri!.Query);
        Assert.Equal("/api/v1/admin/navigation/sections/supportMessages/viewed", handler.ViewedUri!.AbsolutePath);
        using var requestBody = JsonDocument.Parse(handler.ViewedRequestBody!);
        var sentTimestamp = requestBody.RootElement.GetProperty("viewedThroughUtc").GetString();
        Assert.Equal(viewedThroughUtc.ToString("O", CultureInfo.InvariantCulture), sentTimestamp);
    }

    private sealed class NavigationClientHandler : HttpMessageHandler
    {
        public Uri? CountsUri { get; private set; }

        public Uri? ViewedUri { get; private set; }

        public string? ViewedRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/api/v1/auth/login")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new
                    {
                        accessToken = "test-access-token",
                        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(15),
                        refreshToken = "test-refresh-token",
                        refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                        user = new { id = "test-admin", email = "contact@example.invalid", roles = new[] { "Admin" } }
                    })
                };
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/current-user")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AdminProfile
                    {
                        Id = "test-admin",
                        Email = "contact@example.invalid",
                        Roles = ["Admin"],
                        Permissions = ["Admin.Access"]
                    })
                };
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/navigation/counts")
            {
                this.CountsUri = request.RequestUri;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new AdminNavigationCounts
                    {
                        SupportMessages = 4,
                        AsOfUtc = new DateTime(2026, 10, 15, 7, 30, 0, DateTimeKind.Utc).AddTicks(1234560)
                    })
                };
            }

            if (request.RequestUri.AbsolutePath == "/api/v1/admin/navigation/sections/supportMessages/viewed")
            {
                this.ViewedUri = request.RequestUri;
                this.ViewedRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { message = "Просмотр раздела сохранён." })
                };
            }

            throw new InvalidOperationException($"Неожиданный запрос: {request.RequestUri}.");
        }
    }
}
