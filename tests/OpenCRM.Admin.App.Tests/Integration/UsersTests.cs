using System.Net;
using Avalonia;
using Avalonia.Media;
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

public sealed class UsersTests
{
    [AvaloniaFact]
    public async Task UsersNavigationRendersListAndCardWithTabsAndReturnsToSamePage()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        var window = new MainWindow { DataContext = model, Width = 1600, Height = 1000 };
        window.Show();
        try
        {
            await model.LoginCommand.ExecuteAsync(null);
            await model.ShowUsersCommand.ExecuteAsync(null);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsUsersOpen);
            Assert.False(model.IsProfileOpen);
            Assert.Equal(50, model.Users.Rows.Count);
            Assert.Equal("contact@example.invalid", model.Users.Rows[0].Email);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == "Пользователи" && block.IsEffectivelyVisible);
            var list = window.GetVisualDescendants().OfType<ListBox>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "UsersList");
            var table = window.GetVisualDescendants().OfType<Border>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "UsersTable");

            Assert.True(table.Bounds.Height > 650);
            var pagination = window.GetVisualDescendants().OfType<ListPagination>().Single(control => control.IsEffectivelyVisible);
            Assert.True(pagination.TranslatePoint(new Point(0, pagination.Bounds.Height), window)!.Value.Y
                <= table.TranslatePoint(default, window)!.Value.Y);
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<ComboBox>(), combo =>
                AutomationProperties.GetAutomationId(combo) == "UsersSort");
            list.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            var selected = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(1));
            Assert.Equal(Color.Parse("#23384A"), Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background).Color);
            window.MouseMove(selected.TranslatePoint(new Point(20, 10), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Color.Parse("#2B4358"), Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background).Color);
            UsersTests.SaveScreenshot(window, "users-selection-hover.png");
            window.MouseMove(new Point(30, 30));
            list.SelectedIndex = -1;
            Dispatcher.UIThread.RunJobs();
            var headers = window.GetVisualDescendants().OfType<Button>().Where(button =>
                AutomationProperties.GetAutomationId(button)?.StartsWith("UsersSort", StringComparison.Ordinal) == true)
                .Select(button => button.Content?.ToString()).ToArray();

            Assert.Equal(new[] { model.Users.EmailHeader, model.Users.VehiclesHeader, model.Users.MaintenanceHeader,
                model.Users.OrganizationsHeader, model.Users.RegistrationHeader }, headers);
            UsersTests.SaveScreenshot(window, "users-list.png");
            await model.Users.NextPageCommand.ExecuteAsync(null);
            Assert.Equal(2, model.Users.Page);
            var user = model.Users.Rows[0];
            await model.Users.OpenCardCommand.ExecuteAsync(user);
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.Users.IsCardOpen);
            Assert.Equal(7, model.Users.Card!.Tabs.Count);
            Assert.Equal("Активен", model.Users.Card.StatusLabel);
            Assert.Equal("Карточка", window.GetVisualDescendants().OfType<TextBlock>().Single(block =>
                AutomationProperties.GetAutomationId(block) == "UserCardHeading").Text);
            UsersTests.SaveScreenshot(window, "user-card.png");
            model.Users.Card.SelectedTab = model.Users.Card.Tabs.Single(tab => tab.IsOrganizations);
            Dispatcher.UIThread.RunJobs();
            var organizationStatus = window.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "UserOrganizationsStatus");

            Assert.True(organizationStatus.IsEffectivelyVisible);
            Assert.Equal("Активные", organizationStatus.SelectedItem);
            organizationStatus.SelectedItem = "Архивированные";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Архивированные", model.Users.Card.OrganizationStatus);
            Assert.Empty(model.Users.Card.SelectedTab.Items);
            organizationStatus.SelectedItem = "Активные";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, model.Users.Card.SelectedTab.Items.Count);
            var organizationList = window.GetVisualDescendants().OfType<ItemsControl>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "UserCardList");
            var firstOrganization = organizationList.ContainerFromIndex(0)!;
            var secondOrganization = organizationList.ContainerFromIndex(1)!;

            Assert.Equal(firstOrganization.TranslatePoint(default, window)!.Value.X, secondOrganization.TranslatePoint(default, window)!.Value.X, 1);
            Assert.True(secondOrganization.TranslatePoint(default, window)!.Value.Y > firstOrganization.TranslatePoint(default, window)!.Value.Y);
            UsersTests.SaveScreenshot(window, "user-card-organizations.png");
            model.Users.Card.SelectedTab = model.Users.Card.Tabs.Single(tab => tab.IsSubscriptions);
            Dispatcher.UIThread.RunJobs();
            var subscriptionStatus = window.GetVisualDescendants().OfType<ComboBox>().Single(control =>
                AutomationProperties.GetAutomationId(control) == "UserSubscriptionsStatus");

            Assert.True(subscriptionStatus.IsEffectivelyVisible);
            Assert.Equal("Активные", subscriptionStatus.SelectedItem);
            subscriptionStatus.SelectedItem = "Завершённые";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("Завершена", Assert.Single(model.Users.Card.SelectedTab.Items).Subtitle);
            var requestsBeforeStatusChange = handler.Requests.Count;
            subscriptionStatus.SelectedItem = "Все";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, model.Users.Card.SelectedTab.Items.Count);
            Assert.Equal(requestsBeforeStatusChange, handler.Requests.Count);
            await model.Users.RefreshCardCommand.ExecuteAsync(null);
            Assert.Equal("Все", model.Users.Card.SubscriptionStatus);
            Assert.True(model.Users.Card.SelectedTab.IsSubscriptions);
            Dispatcher.UIThread.RunJobs();
            UsersTests.SaveScreenshot(window, "user-card-subscriptions.png");
            model.Users.Card.SelectedTab = model.Users.Card.Tabs.Single(tab => tab.IsSupport);
            Dispatcher.UIThread.RunJobs();
            var supportText = window.GetVisualDescendants().OfType<SelectableTextBlock>().Single(control =>
                control.IsEffectivelyVisible && control.Text?.StartsWith("Как добавить сотрудника в студию?", StringComparison.Ordinal) == true);

            Assert.True(supportText.Bounds.Width > 900);
            Assert.True(supportText.Bounds.Height < 400);
            UsersTests.SaveScreenshot(window, "user-card-support.png");
            model.Users.Card.SelectedTab = model.Users.Card.Tabs.Single(tab => tab.Title.StartsWith("Платежи"));
            await model.Users.RefreshCardCommand.ExecuteAsync(null);
            Assert.StartsWith("Платежи", model.Users.Card.SelectedTab.Title);
            Dispatcher.UIThread.RunJobs();
            UsersTests.SaveScreenshot(window, "user-card-payments.png");
            window.Width = 900;
            Dispatcher.UIThread.RunJobs();
            UsersTests.SaveScreenshot(window, "user-card-compact.png");
            await model.Users.BackToListCommand.ExecuteAsync(null);
            Assert.False(model.Users.IsCardOpen);
            Assert.Equal(2, model.Users.Page);
            Assert.Equal(user.Id, model.Users.SelectedUser!.Id);
            await model.ShowProfileCommand.ExecuteAsync(null);
            Assert.False(model.Users.RefreshCommand.CanExecute(null));
        }
        finally
        {
            await model.ShutdownAsync();
            window.Close();
        }
    }

    [Fact]
    public async Task SearchFiltersAreAppliedOnServerAndResetThePage()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        await model.Users.NextPageCommand.ExecuteAsync(null);
        model.Users.Search = " studio ";
        model.Users.IncludeDeleted = true;
        model.Users.PageSize = 50;
        await model.Users.ApplyFiltersCommand.ExecuteAsync(null);
        Assert.Equal(1, model.Users.Page);
        Assert.Single(model.Users.Rows);
        var request = handler.Requests.Last();
        Assert.Contains("search=studio", request);
        Assert.DoesNotContain("utmSource", request);
        Assert.Contains("includeDeleted=true", request);
        Assert.Contains("sort=1", request);
        Assert.Contains("pageSize=50", request);
        Assert.False(model.Users.NextPageCommand.CanExecute(null));
        await model.ShutdownAsync();
    }

    [Theory]
    [InlineData(3, 3, 4)]
    [InlineData(2, 2, 1)]
    [InlineData(8, 7, 8)]
    [InlineData(10, 9, 10)]
    [InlineData(12, 11, 12)]
    public async Task EveryVisibleSortableColumnSortsOnServerInBothDirectionsAndResetsThePage(int column, int first, int second)
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        await model.Users.NextPageCommand.ExecuteAsync(null);
        await model.Users.SortCommand.ExecuteAsync(column);
        Assert.Equal(1, model.Users.Page);
        Assert.Contains($"sort={first}&", handler.Requests.Last());
        await model.Users.SortCommand.ExecuteAsync(column);
        Assert.Contains($"sort={second}&", handler.Requests.Last());
        Assert.Contains("pageSize=50", handler.Requests.Last());
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task OrganizationCountOpensAllOrganizationsWithTheirOwnPlansOnDemand()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        Assert.DoesNotContain(handler.Requests, path => path.EndsWith("/overview"));
        await model.Users.OpenOrganizationsCommand.ExecuteAsync(model.Users.Rows[0]);
        var tab = model.Users.Card!.SelectedTab;
        Assert.StartsWith("Организации", tab.Title);
        Assert.Equal(2, tab.Items.Count);
        Assert.Equal(new[] { "Бизнес", "СТО бизнес" }, tab.Items.Select(item => item.Fields.Single(field => field.Title == "Тариф").Value));
        Assert.Equal(1, handler.Requests.Count(path => path.EndsWith("/overview")));
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task OrganizationStatusFilterIsLocalAndSurvivesCardRefresh()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        await model.Users.OpenOrganizationsCommand.ExecuteAsync(model.Users.Rows[0]);
        var requestCount = handler.Requests.Count;
        model.Users.Card!.OrganizationStatus = "Архивированные";
        Assert.Empty(model.Users.Card.SelectedTab.Items);
        Assert.False(model.Users.Card.SelectedTab.HasItems);
        Assert.Equal(requestCount, handler.Requests.Count);
        await model.Users.RefreshCardCommand.ExecuteAsync(null);
        Assert.Equal("Архивированные", model.Users.Card.OrganizationStatus);
        Assert.True(model.Users.Card.SelectedTab.IsOrganizations);
        Assert.Empty(model.Users.Card.SelectedTab.Items);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task ChangingPageSizeAndDeletedFilterAppliesImmediatelyAndResetClearsSearch()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        model.Users.PageSize = 25;
        await model.Users.ApplyFiltersCommand.ExecutionTask!;
        Assert.Equal(25, model.Users.Rows.Count);
        Assert.Contains("pageSize=25", handler.Requests.Last());
        model.Users.IncludeDeleted = true;
        await model.Users.ApplyFiltersCommand.ExecutionTask!;
        Assert.Contains("includeDeleted=true", handler.Requests.Last());
        model.Users.Search = " studio ";
        await model.Users.ApplyFiltersCommand.ExecuteAsync(null);
        Assert.Single(model.Users.Rows);
        await model.Users.ResetFiltersCommand.ExecuteAsync(null);
        Assert.Empty(model.Users.Search);
        Assert.False(model.Users.IncludeDeleted);
        Assert.Equal(25, model.Users.Rows.Count);
        Assert.Contains("sort=1&", handler.Requests.Last());
        await model.ShutdownAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LeavingUsersCancelsPendingRequestsAndLogoutClearsPersonalData(bool isCard)
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        handler.DelayCard = isCard;
        handler.DelayList = !isCard;
        var operation = isCard ? model.Users.OpenCardCommand.ExecuteAsync(model.Users.Rows[0]) : model.Users.RefreshCommand.ExecuteAsync(null);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await model.LogoutCommand.ExecuteAsync(null);
        await operation;
        Assert.True(handler.RequestCancelled);
        Assert.Empty(model.Users.Rows);
        Assert.Null(model.Users.Card);
        Assert.False(model.IsUsersOpen);
        Assert.False(model.IsAuthenticated);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task BillingRightsRevocationFiltersOpenCardAndListWithoutReloadingSensitiveValues()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        await model.Users.OpenCardCommand.ExecuteAsync(model.Users.Rows[0]);
        handler.CanViewBilling = false;
        await model.RefreshProfileCommand.ExecuteAsync(null);
        Assert.Equal(
            new[] { "Обзор", "Организации", "Личная техника и ТО", "Обращения" },
            model.Users.Card!.Tabs.Select(tab => tab.DisplayTitle));
        Assert.DoesNotContain(model.Users.Card.Tabs, tab => tab.Title.StartsWith("Платежи") || tab.Title.StartsWith("Подписки"));
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task ForbiddenUsersClosesSectionAndRefreshesRightsWithoutSigningOut()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        handler.UsersStatus = HttpStatusCode.Forbidden;
        handler.CanViewUsers = false;
        await model.Users.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.IsAuthenticated);
        Assert.False(model.IsUsersOpen);
        Assert.False(model.CanViewUsers);
        Assert.Empty(model.Users.Rows);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task MissingUserDoesNotReusePreviousCard()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        await model.Users.OpenCardCommand.ExecuteAsync(model.Users.Rows[0]);
        await model.Users.BackToListCommand.ExecuteAsync(null);
        handler.CardStatus = HttpStatusCode.NotFound;
        await model.Users.OpenCardCommand.ExecuteAsync(model.Users.Rows[1]);
        Assert.Null(model.Users.Card);
        Assert.True(model.Users.HasCardError);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task RapidNavigationDoesNotReactivateSectionAfterItsCancellationFinishes()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        handler.CanViewDashboard = true;
        await model.RefreshProfileCommand.ExecuteAsync(null);
        handler.DelayCard = true;
        handler.HoldCancellation = true;
        var card = model.Users.OpenCardCommand.ExecuteAsync(model.Users.Rows[0]);
        await handler.RequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var dashboard = model.ShowDashboardCommand.ExecuteAsync(null);
        var profile = model.ShowProfileCommand.ExecuteAsync(null);
        handler.ReleaseCancellation.TrySetResult();
        await Task.WhenAll(card, dashboard, profile);
        Assert.True(model.IsProfileOpen);
        Assert.False(model.IsDashboardOpen);
        Assert.False(model.Dashboard.RefreshCommand.CanExecute(null));
        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/v1/admin/dashboard"));
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task SessionExpirationFromCardClearsAllUserDataAndReturnsToLogin()
    {
        var handler = new UsersApiHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        handler.CardStatus = HttpStatusCode.Unauthorized;
        await model.Users.OpenCardCommand.ExecuteAsync(model.Users.Rows[0]);
        Assert.False(model.IsAuthenticated);
        Assert.Empty(model.Users.Rows);
        Assert.Null(model.Users.Card);
        Assert.False(model.Users.IsCardOpen);
        await model.ShutdownAsync();
    }

    [Fact]
    public async Task MissingUsersPermissionDoesNotStartAnyUserRequests()
    {
        var handler = new UsersApiHandler { CanViewUsers = false };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://test.local/") };
        using var session = new AdminSessionClient(http);
        var model = UsersTests.CreateModel(session, http);
        await model.LoginCommand.ExecuteAsync(null);
        await model.ShowUsersCommand.ExecuteAsync(null);
        Assert.False(model.IsUsersOpen);
        Assert.DoesNotContain(handler.Requests, path => path.StartsWith("/api/v1/admin/users"));
        await model.ShutdownAsync();
    }

    private static MainWindowViewModel CreateModel(AdminSessionClient session, HttpClient http) => new(session, http.BaseAddress!)
    {
        Email = "contact@example.invalid", Password ="test-password"
    };

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
}
