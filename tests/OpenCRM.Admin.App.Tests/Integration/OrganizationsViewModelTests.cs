using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OpenCRM.Admin.Api;
using OpenCRM.Admin.App.ViewModels;
using OpenCRM.Admin.App.Views;

namespace OpenCRM.Admin.App.Tests.Integration;

public sealed class OrganizationsViewModelTests
{
    [Theory]
    [InlineData("detailing")]
    [InlineData("service-station")]
    public void OrganizationRowShowsFreeAsAnUnlimitedPlan(string scenarioCode)
    {
        var row = new AdminOrganizationRow(new OpenCRM.Admin.Api.Models.AdminOrganization
        {
            PrimaryScenarioCode = scenarioCode,
            IsActive = true
        }, true);

        Assert.Equal("Free", row.Plan);
        Assert.Equal("Бессрочно", row.PlanEnd);
    }

    [AvaloniaFact]
    public void LongOrganizationNamesDoNotShiftColumnsRelativeToHeadersOrOtherRows()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        var longName = new string('Ж', 500);
        var model = new OrganizationsViewModel(session)
        {
            Rows =
            [
                new AdminOrganizationRow(new OpenCRM.Admin.Api.Models.AdminOrganization
                {
                    Name = "Студия", Owner = "Владелец", ActiveSubscriptionName = "Базовый"
                }),
                new AdminOrganizationRow(new OpenCRM.Admin.Api.Models.AdminOrganization
                {
                    Name = longName, Owner = new string('В', 300), OwnerEmail = new string('a', 300) + "@example.test",
                    ActiveSubscriptionName = new string('Т', 300), ScenarioName = "Детейлинг", StatusName = "Активна"
                })
            ]
        };
        model.SetCommercialEnabled(true);

        var view = new OrganizationsView { DataContext = model };
        var window = new Window { Width = 1100, Height = 800, Content = view };
        window.Show();
        try
        {
            foreach (var width in new[] { 1100, 1800, 2400, 1100 })
            {
                window.Width = width;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var header = view.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "OrganizationsSortName")
                    .GetVisualAncestors().OfType<Grid>().First();

                Assert.Equal(header.Bounds.Width, header.ColumnDefinitions.Sum(column => column.ActualWidth), 1);
                Assert.True(header.Bounds.Width >= Math.Max(1264, width) - 40);

                foreach (var name in new[] { "Студия", longName })
                {
                    var nameText = view.GetVisualDescendants().OfType<TextBlock>()
                        .Single(text => text.Text == name && text.IsEffectivelyVisible);

                    var row = nameText.GetVisualAncestors().OfType<Grid>().First();
                    Assert.Equal(10, row.ColumnDefinitions.Count);
                    Assert.Equal(header.Bounds.Width, row.Bounds.Width, 1);
                    Assert.Equal(header.TranslatePoint(default, view)!.Value.X, row.TranslatePoint(default, view)!.Value.X, 1);
                    for (var index = 0; index < row.ColumnDefinitions.Count; index++)
                    {
                        Assert.Equal(header.ColumnDefinitions[index].ActualWidth, row.ColumnDefinitions[index].ActualWidth, 1);
                    }

                    Assert.True(nameText.Bounds.Width <= row.ColumnDefinitions[0].ActualWidth);
                    Assert.Equal(Avalonia.Media.TextTrimming.CharacterEllipsis, nameText.TextTrimming);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NeutralModeHidesCommercialColumnAndKeepsBusinessFinanceVisible()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        var listModel = new OrganizationsViewModel(session)
        {
            Rows = [new AdminOrganizationRow(new OpenCRM.Admin.Api.Models.AdminOrganization
            {
                Name = "Студия",
                ActiveSubscriptionName = "Бизнес"
            })]
        };
        listModel.SetCommercialEnabled(false);
        var listView = new OrganizationsView { DataContext = listModel };
        var listWindow = new Window { Width = 1200, Height = 800, Content = listView };
        listWindow.Show();
        try
        {
            listWindow.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var header = listView.GetVisualDescendants().OfType<Button>()
                .Single(button => AutomationProperties.GetAutomationId(button) == "OrganizationsSortName")
                .GetVisualAncestors().OfType<Grid>().First();
            var commercialHeader = header.GetVisualDescendants().OfType<Button>()
                .Single(button => Grid.GetColumn(button) == 4);

            Assert.Equal(0, header.ColumnDefinitions[4].ActualWidth);
            Assert.False(commercialHeader.IsEffectivelyVisible);
            Assert.Equal(string.Empty, listModel.SubscriptionHeader);
        }
        finally
        {
            listWindow.Close();
        }

        var cardModel = new OrganizationsViewModel(session)
        {
            IsCardOpen = true,
            Organization = new OpenCRM.Admin.Api.Models.AdminOrganization
            {
                Name = "Студия",
                ActiveSubscriptionName = "Бизнес",
                WorkOrdersTotal = 25000,
                PaymentsTotal = 18000
            }
        };
        cardModel.SetCommercialEnabled(false);
        var cardView = new OrganizationsView { DataContext = cardModel };
        var cardWindow = new Window { Width = 1200, Height = 900, Content = cardView };
        cardWindow.Show();
        try
        {
            cardWindow.UpdateLayout();
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(cardView.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == cardModel.WorkOrdersTotalLabel && text.IsEffectivelyVisible);
            Assert.Contains(cardView.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == cardModel.PaymentsTotalLabel && text.IsEffectivelyVisible);
            Assert.DoesNotContain(cardView.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Подписка" && text.IsEffectivelyVisible);
        }
        finally
        {
            cardWindow.Close();
        }
    }

    [AvaloniaFact]
    public void OrganizationCardUsesCompactSummaryAndReadableMetricsAtDifferentWidths()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        var model = new OrganizationsViewModel(session)
        {
            IsCardOpen = true,
            Organization = new OpenCRM.Admin.Api.Models.AdminOrganization
            {
                Name = "Студия детейлинга",
                Owner = "Александр",
                OwnerEmail = "contact@example.invalid",
                ScenarioName = "Детейлинг",
                StatusName = "Активна",
                ActiveSubscriptionName = "Бизнес",
                MembersCount = 4,
                AdminsCount = 1,
                WorkOrdersCount = 36,
                CompletedWorkOrdersCount = 28,
                WorkOrdersTotal = 250000,
                PaymentsTotal = 180000,
                LastAuditTitle = "Заказ завершён",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };
        model.SetCommercialEnabled(true);
        var view = new OrganizationsView { DataContext = model };
        var window = new Window { Width = 1800, Height = 1100, Content = view };
        window.Show();
        try
        {
            foreach (var width in new[] { 1100, 1800, 2400 })
            {
                window.Width = width;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var summary = view.GetVisualDescendants().OfType<Grid>()
                    .Single(grid => AutomationProperties.GetAutomationId(grid) == "OrganizationSummary");
                Assert.Equal(3, summary.Children.Count);
                Assert.InRange(summary.Bounds.Height, 150, 320);
                var metrics = view.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.UniformGrid>()
                    .Single(grid => AutomationProperties.GetAutomationId(grid) == "OrganizationMetrics");
                Assert.Equal(12, metrics.Children.Count);
                Assert.All(metrics.GetVisualDescendants().OfType<TextBlock>().Where(text => text.Classes.Contains("metric-value")),
                    text => Assert.Equal(26, text.FontSize));
                Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(),
                    text => text.Text == "Ожидающие приглашения" && text.IsEffectivelyVisible);
                Assert.False(Assert.Single(view.GetVisualDescendants().OfType<Expander>()).IsExpanded);
                var directory = Environment.GetEnvironmentVariable("OPENCRM_ADMIN_SCREENSHOTS");
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    using var stream = File.Create(Path.Combine(directory, $"organization-card-{width}.png"));
                    frame.Save(stream, PngBitmapEncoderOptions.Default);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }
    [AvaloniaFact]
    public async Task FiltersLoadOrganizationsAndCardShowsStatisticsAndMembers()
    {
        var requests = new List<Uri>();
        var organizationId = Guid.Parse("c69ecf33-b6e3-4dde-a57a-99e967a7c118");
        using var http = new HttpClient(new FakeHttpMessageHandler(async (request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login") return Json(HttpStatusCode.OK, Tokens());
            if (path == "/api/v1/admin/current-user") return Json(HttpStatusCode.OK, Profile());
            requests.Add(request.RequestUri);
            return path == "/api/v1/admin/organizations"
                ? Json(HttpStatusCode.OK, new
                {
                    totalCount = 1,
                    page = 1,
                    pageSize = 50,
                    organizations = new[]
                    {
                        new { id = organizationId, name = "Студия", owner = "Владелец", scenarioName = "Детейлинг", statusName = "Активна" }
                    }
                })
                : Json(HttpStatusCode.OK, new
                {
                    id = organizationId,
                    name = "Студия",
                    workOrdersCount = 6,
                    completedWorkOrdersCount = 4,
                    workOrdersTotal = 25000m,
                    paymentsTotal = 18000m,
                    members = new[] { new { userId = "member", user = "Участник", roleCode = "admin" } }
                });
        })) { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        await session.LoginAsync("contact@example.invalid", "password");
        var viewModel = new OrganizationsViewModel(session);
        viewModel.SetCommercialEnabled(true);
        var window = new Window { Width = 1200, Height = 850, Content = new OrganizationsView { DataContext = viewModel } };
        window.Show();

        try
        {
            await viewModel.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            viewModel.Search = "Студия";
            viewModel.User = "contact@example.invalid";
            await viewModel.ApplyFiltersCommand.ExecuteAsync(null);
            await viewModel.OpenCardCommand.ExecuteAsync(viewModel.Rows[0]);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("Студия", Assert.Single(viewModel.Rows).Name);
            Assert.Contains("search=%D0%A1%D1%82%D1%83%D0%B4%D0%B8%D1%8F", requests[1].Query);
            Assert.Contains("user=contact%40example.invalid", requests[1].Query);
            Assert.True(viewModel.IsCardOpen);
            Assert.Equal(6, viewModel.Organization!.WorkOrdersCount);
            Assert.Equal(25000m, viewModel.Organization.WorkOrdersTotal);
            Assert.Equal("Участник", Assert.Single(viewModel.Organization.Members).User);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "6" && text.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Нет активного тарифа" && text.IsEffectivelyVisible);
        }
        finally
        {
            await viewModel.DeactivateAsync(true);
            window.Close();
        }

    }

    [AvaloniaFact]
    public void OrganizationsViewLoadsInHeadlessWindow()
    {
        using var http = new HttpClient { BaseAddress = new Uri("https://opencrm.test/") };
        using var session = new AdminSessionClient(http);
        var view = new OrganizationsView { DataContext = new OrganizationsViewModel(session) };
        var window = new Window { Width = 1100, Height = 800, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();

            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Организации" && text.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), box => box.PlaceholderText == "Поиск по организации" && box.IsEffectivelyVisible);
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button => button.Content?.ToString()?.StartsWith("Создана", StringComparison.Ordinal) == true);
        }
        finally
        {
            window.Close();
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status) { Content = JsonContent.Create(value) };

    private static object Tokens() => new
    {
        accessToken = "access-token",
        accessTokenExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        refreshToken = "refresh-token",
        refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
        tokenType = "Bearer",
        user = new { id = "admin", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
    };

    private static object Profile() => new
    {
        id = "admin",
        email = "contact@example.invalid",
        roles = new[] { "Admin" },
        permissions = new[] { "Admin.Access", "Admin.Users.View" }
    };

    private sealed class FakeHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
