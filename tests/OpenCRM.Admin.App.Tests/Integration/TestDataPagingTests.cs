using System.Net;
using System.Net.Http.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class TestDataPagingTests
{
    [AvaloniaFact]
    public async Task OrganizationPagesPreserveSelectionAndSearchStartsAtFirstPage()
    {
        using var handler = new PagingHandler();
        using var client = await TestDataPagingTests.CreateClientAsync(handler);
        var model = new TestDataViewModel(client);
        var window = new Window { Width = 1100, Height = 800, Content = new TestDataView { DataContext = model } };
        window.Show();
        try
        {
            await model.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(20, model.Organizations.Count);
            Assert.Equal(45, model.TotalCount);
            Assert.False(model.PreviousPageCommand.CanExecute(null));
            var next = window.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Command, model.NextPageCommand));
            Assert.True(next.IsEffectivelyEnabled);
            model.Search = "needle";
            await ((IAsyncRelayCommand)next.Command!).ExecuteAsync(null);
            Assert.Equal(2, model.Page);
            Assert.Equal("?search=&page=2&pageSize=20", handler.PageRequests.Last());
            Assert.Equal(20, model.Organizations.Count);
            var organization = model.Organizations[0];
            await model.SelectOrganizationCommand.ExecuteAsync(organization);
            var pageRequestCount = handler.PageRequests.Count;
            model.RequestVehiclesCommand.Execute(null);
            await model.ConfirmCommand.ExecuteAsync(null);
            Assert.Empty(model.ErrorMessage);
            Assert.Equal(10, model.SelectedOrganization!.VehiclesCount);
            Assert.Equal(pageRequestCount, handler.PageRequests.Count);
            model.CloseOrganizationCommand.Execute(null);
            Assert.Equal(2, model.Page);
            Assert.Equal(organization.Id, model.Organizations[0].Id);
            Assert.Equal(10, model.Organizations[0].VehiclesCount);
            await model.ApplyFiltersCommand.ExecuteAsync(null);
            Assert.Equal(1, model.Page);
            Assert.Equal("?search=needle&page=1&pageSize=20", handler.PageRequests.Last());
            Assert.Equal(1, model.TotalCount);
            Assert.Single(model.Organizations);
            Assert.False(model.NextPageCommand.CanExecute(null));
            Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/v1/admin/test-data/organizations?", StringComparison.Ordinal));
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [Fact]
    public async Task LastOrganizationPageContainsOnlyRemainingItems()
    {
        using var handler = new PagingHandler();
        using var client = await TestDataPagingTests.CreateClientAsync(handler);
        var model = new TestDataViewModel(client);
        await model.ActivateAsync();
        await model.NextPageCommand.ExecuteAsync(null);
        await model.NextPageCommand.ExecuteAsync(null);
        Assert.Equal(3, model.Page);
        Assert.Equal(5, model.Organizations.Count);
        Assert.False(model.NextPageCommand.CanExecute(null));
        await model.PreviousPageCommand.ExecuteAsync(null);
        Assert.Equal(2, model.Page);
        Assert.Equal(20, model.Organizations.Count);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task LegacyServerMarksItsLimitedSearchResponseAndPagesAvailableItems()
    {
        using var handler = new PagingHandler { Legacy = true };
        using var client = await TestDataPagingTests.CreateClientAsync(handler);
        var page = await client.GetTestDataOrganizationsPageAsync("", 2, 20, TestContext.Current.CancellationToken);
        Assert.True(page.IsLegacyResponse);
        Assert.Equal(25, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(5, page.Items.Count);
        var organization = await client.GetTestDataOrganizationAsync(TestDataPagingTests.OrganizationId(31), TestContext.Current.CancellationToken);
        Assert.Equal(TestDataPagingTests.OrganizationId(31), organization!.Id);
    }

    private static Guid OrganizationId(int number) => Guid.Parse($"00000000-0000-0000-0000-{number:000000000000}");

    private static async Task<AdminSessionClient> CreateClientAsync(PagingHandler handler)
    {
        var client = new AdminSessionClient(new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") });
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);

        return client;
    }

    private sealed class PagingHandler : HttpMessageHandler
    {
        public bool Legacy { get; init; }
        public List<string> Requests { get; } = [];
        public List<string> PageRequests { get; } = [];
        private int _vehiclesCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            this.Requests.Add(request.RequestUri.PathAndQuery);
            if (path == "/api/v1/auth/login")
            {
                return PagingHandler.Json(new
                {
                    accessToken = "access-token", refreshToken = "refresh",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1), refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return PagingHandler.Json(new AdminProfile { Id = "admin", IsSystem = true });
            }

            if (path.EndsWith("/organizations/page", StringComparison.Ordinal))
            {
                if (this.Legacy)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
                }

                this.PageRequests.Add(request.RequestUri.Query);
                var query = request.RequestUri.Query.TrimStart('?').Split('&').Select(part => part.Split('=', 2))
                    .ToDictionary(part => part[0], part => Uri.UnescapeDataString(part[1]));
                var page = int.Parse(query["page"]);
                var size = int.Parse(query["pageSize"]);
                var count = query["search"] == "needle" ? 1 : 45;

                return PagingHandler.Json(new AdminTestDataOrganizationsPage
                {
                    Page = page, PageSize = size, TotalCount = count,
                    Items = Enumerable.Range(1, count).Skip((page - 1) * size).Take(size).Select(this.CreateOrganization).ToArray()
                });
            }

            if (path.EndsWith("/organizations", StringComparison.Ordinal))
            {
                Assert.True(this.Legacy);
                var search = Uri.UnescapeDataString(request.RequestUri.Query["?search=".Length..]);

                return PagingHandler.Json(Guid.TryParse(search, out var id)
                    ? new[] { new AdminTestDataOrganization { Id = id, Name = "Выбранная организация" } }
                    : Enumerable.Range(1, 25).Select(this.CreateOrganization).ToArray());
            }

            if (path.EndsWith("/vehicles/generate", StringComparison.Ordinal))
            {
                this._vehiclesCount = 10;

                return PagingHandler.Json(new AdminTestDataGenerationResult { CreatedCount = 10, Message = "Создано." });
            }

            if (path.EndsWith("/vehicles", StringComparison.Ordinal))
            {
                return PagingHandler.Json(Array.Empty<AdminTestDataVehicle>());
            }

            if (Guid.TryParse(path.Split('/').Last(), out var organizationId))
            {
                return this.Legacy
                    ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
                    : PagingHandler.Json(new AdminTestDataOrganization { Id = organizationId, Name = "Организация", VehiclesCount = this._vehiclesCount });
            }

            throw new InvalidOperationException($"Неожиданный запрос: {path}");
        }

        private AdminTestDataOrganization CreateOrganization(int number) => new()
        {
            Id = TestDataPagingTests.OrganizationId(number), Name = $"Организация {number}", VehiclesCount = this._vehiclesCount
        };

        private static Task<HttpResponseMessage> Json<T>(T value) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(value) });
    }
}