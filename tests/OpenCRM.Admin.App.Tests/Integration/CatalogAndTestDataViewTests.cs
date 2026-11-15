using System.Net;
using System.Net.Http.Json;
using Avalonia;
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

public sealed class CatalogAndTestDataViewTests
{
    [AvaloniaFact]
    public async Task TestDataSelectButtonsShowOrganizationAndVehicleActionsWithReturn()
    {
        using var http = new HttpClient(new FixtureHandler()) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new TestDataViewModel(client);
        var window = new Window { Width = 900, Height = 700, Content = new TestDataView { DataContext = model } };
        window.Show();
        try
        {
            await model.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            var organizationButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "Выбрать" && button.IsEffectivelyVisible);
            Assert.True(organizationButton.IsEffectivelyEnabled);
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)organizationButton.Command!)
                .ExecuteAsync(organizationButton.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(model.SelectedOrganization);
            Assert.False(organizationButton.IsEffectivelyVisible);
            var vehicleButton = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "Выбрать" && button.IsEffectivelyVisible);
            Assert.True(vehicleButton.IsEffectivelyEnabled);
            vehicleButton.Command!.Execute(vehicleButton.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(model.SelectedVehicle);
            Assert.False(vehicleButton.IsEffectivelyVisible);
            var actions = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "Создать обслуживания");
            Assert.True(actions.IsEffectivelyVisible);
            Assert.True(actions.IsEffectivelyEnabled);
            Assert.False(model.IsConfirmationOpen);
            CatalogAndTestDataViewTests.SaveScreenshot(window, "test-data-selected-vehicle.png");
            var backToVehicles = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "← К технике организации");
            Assert.True(backToVehicles.IsEffectivelyEnabled);
            backToVehicles.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(model.SelectedVehicle);
            Assert.True(vehicleButton.IsEffectivelyVisible);
            var backToOrganizations = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "← К организациям");
            Assert.True(backToOrganizations.IsEffectivelyEnabled);
            backToOrganizations.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(model.SelectedOrganization);
            Assert.True(organizationButton.IsEffectivelyVisible);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task ModelsAndSuggestionButtonsReplaceListsWithDetailsAndCanReturn()
    {
        using var http = new HttpClient(new FixtureHandler()) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var catalog = new VehicleCatalogViewModel(client);
        var suggestions = new CatalogSuggestionsViewModel(client);
        var catalogView = new VehicleCatalogView { DataContext = catalog };
        var window = new Window { Width = 900, Height = 700, Content = catalogView };
        window.Show();
        try
        {
            await catalog.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            var models = window.GetVisualDescendants().OfType<Button>()
                .First(button => button.Content as string == "Модели");
            Assert.NotNull(models.Command);
            await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)models.Command).ExecuteAsync(models.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(catalog.SelectedBrand);
            Assert.NotEmpty(catalog.Models);
            CatalogAndTestDataViewTests.SaveScreenshot(window, "catalog-models-detail.png");
            Assert.DoesNotContain(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Марки" && text.IsEffectivelyVisible);
            var back = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "← К маркам");
            Assert.True(back.IsEffectivelyVisible);
            Assert.True(back.IsEffectivelyEnabled);
            back.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(catalog.SelectedBrand);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Марки" && text.IsEffectivelyVisible);

            window.Content = new CatalogSuggestionsView { DataContext = suggestions };
            await suggestions.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            var open = window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == "Открыть");
            Assert.NotNull(open.Command);
            open.Command.Execute(open.CommandParameter);
            Dispatcher.UIThread.RunJobs();
            Assert.NotNull(suggestions.SelectedSuggestion);
            CatalogAndTestDataViewTests.SaveScreenshot(window, "catalog-suggestion-detail.png");
            Assert.False(open.IsEffectivelyVisible);
            var returnToList = window.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "← К предложениям");
            Assert.True(returnToList.IsEffectivelyVisible);
            returnToList.Command!.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Null(suggestions.SelectedSuggestion);
            Assert.True(open.IsEffectivelyVisible);
        }
        finally
        {
            await catalog.DeactivateAsync(true);
            await suggestions.DeactivateAsync(true);
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task BrandEditorOpensAsAnOverlayOverTheCatalogList()
    {
        using var http = new HttpClient(new FixtureHandler()) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new VehicleCatalogViewModel(client);
        var view = new VehicleCatalogView { DataContext = model };
        var window = new Window { Width = 1240, Height = 940, Content = view };
        window.Show();
        try
        {
            await model.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == "Марки" && text.IsEffectivelyVisible);

            model.CreateBrandCommand.Execute(null);
            model.Name = "Новая марка";
            Dispatcher.UIThread.RunJobs();

            Assert.True(model.IsEditorOpen);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBox>(), input => input.IsEffectivelyVisible);
            var save = view.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Content as string == "Сохранить");
            Assert.True(save.IsEffectivelyVisible);
            Assert.True(save.IsEffectivelyEnabled);
            CatalogAndTestDataViewTests.SaveScreenshot(window, "catalog-editor-overlay-wide.png");
            window.Width = 592;
            Dispatcher.UIThread.RunJobs();
            CatalogAndTestDataViewTests.SaveScreenshot(window, "catalog-editor-overlay-592.png");

            model.CancelEditCommand.Execute(null);
            model.EditBrandCommand.Execute(model.Brands.Single(brand => brand.Id == 12));
            model.Name = "Toyota обновлена";
            Dispatcher.UIThread.RunJobs();
            Assert.True(model.IsEditorOpen);
            Assert.Contains(view.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Сохранить"
                && button.IsEffectivelyVisible && button.IsEffectivelyEnabled);
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [Fact]
    public async Task TestDataGenerationRequiresSelectionConfirmationAndRespectsLimits()
    {
        using var http = new HttpClient(new FixtureHandler()) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new TestDataViewModel(client);
        await model.ActivateAsync();
        await model.SelectOrganizationCommand.ExecuteAsync(model.Organizations.Single());
        model.SelectVehicleCommand.Execute(model.Vehicles.Single());

        model.RequestVehiclesCommand.Execute(null);
        Assert.True(model.IsConfirmationOpen);
        model.Count = 101;
        Assert.False(model.ConfirmCommand.CanExecute(null));
        model.Count = 100;
        Assert.True(model.ConfirmCommand.CanExecute(null));
        model.CancelConfirmationCommand.Execute(null);

        model.RequestMaintenancesCommand.Execute(null);
        model.Count = 301;
        Assert.False(model.ConfirmCommand.CanExecute(null));
        model.Count = 300;
        Assert.True(model.ConfirmCommand.CanExecute(null));
        model.CancelConfirmationCommand.Execute(null);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task LostGenerationResponseBlocksRetryUntilExplicitRefresh()
    {
        var handler = new FixtureHandler { LoseNextGenerationResponse = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new TestDataViewModel(client);
        await model.ActivateAsync();
        await model.SelectOrganizationCommand.ExecuteAsync(model.Organizations.Single());
        model.RequestVehiclesCommand.Execute(null);
        await model.ConfirmCommand.ExecuteAsync(null);

        Assert.True(model.IsGenerationOutcomeUnknown);
        Assert.True(model.HasError);
        Assert.False(model.RequestVehiclesCommand.CanExecute(null));
        Assert.False(model.ConfirmCommand.CanExecute(null));
        Assert.True(model.RefreshUnknownOutcomeCommand.CanExecute(null));
        Assert.Equal(1, handler.GenerationAttempts);

        await model.RefreshUnknownOutcomeCommand.ExecuteAsync(null);

        Assert.False(model.IsGenerationOutcomeUnknown);
        Assert.True(model.HasMessage);
        Assert.Equal(11, model.SelectedOrganization!.VehiclesCount);
        Assert.True(model.RequestVehiclesCommand.CanExecute(null));
        Assert.Equal(1, handler.GenerationAttempts);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task UnknownOutcomeRefreshUsesTheOrganizationAndSearchFromTheAttempt()
    {
        var handler = new FixtureHandler { LoseNextGenerationResponse = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new TestDataViewModel(client) { Search = "org-a" };
        await model.ActivateAsync();
        await model.SelectOrganizationCommand.ExecuteAsync(model.Organizations.Single());
        model.RequestVehiclesCommand.Execute(null);
        await model.ConfirmCommand.ExecuteAsync(null);

        model.Search = "org-b";
        await model.ApplyFiltersCommand.ExecuteAsync(null);
        var organizationB = model.Organizations.Single();
        await model.SelectOrganizationCommand.ExecuteAsync(organizationB);

        await model.RefreshUnknownOutcomeCommand.ExecuteAsync(null);

        Assert.False(model.IsGenerationOutcomeUnknown);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), model.SelectedOrganization!.Id);
        Assert.Contains("/api/v1/admin/test-data/organizations/page?search=org-a&page=1&pageSize=20", handler.RequestedPaths);
        Assert.Contains("/api/v1/admin/test-data/organizations/11111111-1111-1111-1111-111111111111", handler.RequestedPaths);
        Assert.Contains("/api/v1/admin/test-data/organizations/11111111-1111-1111-1111-111111111111/vehicles", handler.RequestedPaths);
        Assert.Equal(1, handler.GenerationAttempts);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task UnknownOutcomeRemainsBlockedWhenAttemptOrganizationIsNotFound()
    {
        var handler = new FixtureHandler { LoseNextGenerationResponse = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new TestDataViewModel(client) { Search = "org-a" };
        await model.ActivateAsync();
        await model.SelectOrganizationCommand.ExecuteAsync(model.Organizations.Single());
        model.RequestVehiclesCommand.Execute(null);
        await model.ConfirmCommand.ExecuteAsync(null);

        model.Search = "org-b";
        await model.ApplyFiltersCommand.ExecuteAsync(null);
        var organizationB = model.Organizations.Single();
        await model.SelectOrganizationCommand.ExecuteAsync(organizationB);
        handler.HideAttemptedOrganization = true;

        await model.RefreshUnknownOutcomeCommand.ExecuteAsync(null);

        Assert.True(model.IsGenerationOutcomeUnknown);
        Assert.False(model.RequestVehiclesCommand.CanExecute(null));
        Assert.Equal(organizationB.Id, model.SelectedOrganization!.Id);
        Assert.Contains("не удалось найти", model.ErrorMessage);
        Assert.Equal(1, handler.GenerationAttempts);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task NewModelKeepsBrandSelectedWhenEditorOpened()
    {
        var handler = new FixtureHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new VehicleCatalogViewModel(client);
        await model.ActivateAsync();
        var brandA = model.Brands.Single(brand => brand.Id == 12);
        var brandB = model.Brands.Single(brand => brand.Id == 13);
        await model.SelectBrandCommand.ExecuteAsync(brandA);
        model.CreateModelCommand.Execute(null);
        model.Name = "RAV4";
        await model.SelectBrandCommand.ExecuteAsync(brandB);

        await model.SaveCommand.ExecuteAsync(null);

        Assert.Contains(handler.Mutations, request => request.Method == "POST"
            && request.Path == "/api/v1/admin/catalog/brands/12/models");
        await model.DeactivateAsync(true);
    }

    [AvaloniaFact]
    public async Task CatalogSuggestionAndTestDataScreensRenderAtMinimumAndDesktopWidths()
    {
        using var http = new HttpClient(new FixtureHandler()) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password");
        var catalogModel = new VehicleCatalogViewModel(client);
        var suggestionsModel = new CatalogSuggestionsViewModel(client);
        var testDataModel = new TestDataViewModel(client);
        var views = new (string Name, UserControl View)[]
        {
            ("catalog", new VehicleCatalogView { DataContext = catalogModel }),
            ("catalog-suggestions", new CatalogSuggestionsView { DataContext = suggestionsModel }),
            ("test-data", new TestDataView { DataContext = testDataModel })
        };

        foreach (var (name, view) in views)
        {
            var window = new Window { Width = 1240, Height = 940, Content = view };
            try
            {
                window.Show();
                var model = (CommunicationsViewModel)view.DataContext!;
                await model.ActivateAsync();
                Dispatcher.UIThread.RunJobs();
                CatalogAndTestDataViewTests.SaveScreenshot(window, $"{name}-list-wide.png");
                window.Width = 592;
                Dispatcher.UIThread.RunJobs();
                CatalogAndTestDataViewTests.SaveScreenshot(window, $"{name}-list-592.png");
                window.Width = 1240;
                if (ReferenceEquals(model, catalogModel))
                {
                    await catalogModel.SelectBrandCommand.ExecuteAsync(new OpenCRM.Admin.Api.Models.AdminCatalogBrand { Id = 12, Name = "Toyota", Kind = 1, KindText = "Легковой автомобиль", ModelsCount = 2 });
                }
                else if (ReferenceEquals(model, testDataModel))
                {
                    await testDataModel.SelectOrganizationCommand.ExecuteAsync(new OpenCRM.Admin.Api.Models.AdminTestDataOrganization { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Name = "СТО Автопарк", OwnerEmail = "contact@example.invalid", ScenarioCode = "service-station", VehiclesCount = 1 });
                    testDataModel.SelectVehicleCommand.Execute(new OpenCRM.Admin.Api.Models.AdminTestDataVehicle { Id = Guid.Parse("22222222-2222-2222-2222-222222222222"), DisplayName = "Toyota Camry · 2022", MaintenanceCount = 3, DefectCount = 1 });
                }
                Dispatcher.UIThread.RunJobs();
                CatalogAndTestDataViewTests.SaveScreenshot(window, $"{name}-wide.png");
                Assert.True(view.Bounds.Width > 0);

                window.Width = 592;
                Dispatcher.UIThread.RunJobs();
                CatalogAndTestDataViewTests.SaveScreenshot(window, $"{name}-592.png");
                Assert.True(view.Bounds.Width >= 590);
                Assert.Contains(view.GetVisualDescendants().OfType<Button>(), button => button.Bounds.Width > 0);
            }
            finally
            {
                window.Close();
                await ((CommunicationsViewModel)view.DataContext!).DeactivateAsync(true);
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

    private sealed class FixtureHandler : HttpMessageHandler
    {
        public List<(string Method, string Path)> Mutations { get; } = [];
        public List<string> RequestedPaths { get; } = [];
        public bool LoseNextGenerationResponse { get; set; }
        public bool HideAttemptedOrganization { get; set; }
        public int GenerationAttempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.PathAndQuery;
            this.RequestedPaths.Add(path);
            if (request.RequestUri.AbsolutePath == "/api/v1/admin/test-data/organizations/page")
            {
                var isOrganizationB = request.RequestUri.Query.Contains("search=org-b", StringComparison.Ordinal);
                var items = isOrganizationB
                    ? new[] { new { id = Guid.Parse("33333333-3333-3333-3333-333333333333"), name = "Организация B", ownerUserId = "owner-b", ownerEmail = "contact@example.invalid", scenarioCode = "detailing", isActive = true, vehiclesCount = 1, maintenanceCount = 0 } }
                    : FixtureHandler.CreateOrganizations(this.GenerationAttempts == 0 ? 1 : 11);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { items, totalCount = items.Length, page = 1, pageSize = 20 })
                });
            }

            if (path == "/api/v1/admin/test-data/organizations/11111111-1111-1111-1111-111111111111")
            {
                return Task.FromResult(this.HideAttemptedOrganization
                    ? new HttpResponseMessage(HttpStatusCode.NotFound)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = JsonContent.Create(FixtureHandler.CreateOrganizations(this.GenerationAttempts == 0 ? 1 : 11).Single())
                    });
            }
            if (path.EndsWith("/vehicles/generate", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                this.GenerationAttempts++;
                if (this.LoseNextGenerationResponse)
                {
                    this.LoseNextGenerationResponse = false;
                    throw new HttpRequestException("Ответ потерян после выполнения запроса.");
                }
            }

            if (request.Method != HttpMethod.Get && path.StartsWith("/api/v1/admin/catalog/", StringComparison.Ordinal))
            {
                this.Mutations.Add((request.Method.Method, request.RequestUri.AbsolutePath));
            }

            object body = path switch
            {
                "/api/v1/auth/login" => FixtureHandler.CreateTokens(),
                "/api/v1/admin/current-user" => new { id = "admin-1", email = "contact@example.invalid", roles = new[] { "System" }, permissions = Array.Empty<string>(), isSystem = true },
                "/api/v1/admin/catalog/brands?kind=&search=" => new[]
                {
                    new { id = 12, name = "Toyota", kind = 1, kindText = "Легковой автомобиль", modelsCount = 2 },
                    new { id = 13, name = "Honda", kind = 1, kindText = "Легковой автомобиль", modelsCount = 1 }
                },
                "/api/v1/admin/catalog/brands/12/models?search=" => new[] { new { id = 121, brandId = 12, name = "Camry", vehiclesCount = 25 } },
                "/api/v1/admin/catalog/brands/13/models?search=" => new[] { new { id = 131, brandId = 13, name = "CR-V", vehiclesCount = 11 } },
                "/api/v1/admin/catalog-suggestions?status=1" => new[] { new { id = 3, kind = 1, kindText = "Легковой автомобиль", brandName = "Toyota", modelName = "Land Cruiser 300", status = 1, statusText = "На модерации", suggestedByEmail = "contact@example.invalid", createdAt = DateTime.UtcNow } },
                "/api/v1/admin/test-data/organizations?search=" => FixtureHandler.CreateOrganizations(this.GenerationAttempts == 0 ? 1 : 11),
                "/api/v1/admin/test-data/organizations?search=org-a" => this.HideAttemptedOrganization
                    ? Array.Empty<object>()
                    : FixtureHandler.CreateOrganizations(this.GenerationAttempts == 0 ? 1 : 11),
                "/api/v1/admin/test-data/organizations?search=11111111-1111-1111-1111-111111111111" => this.HideAttemptedOrganization
                    ? Array.Empty<object>()
                    : FixtureHandler.CreateOrganizations(this.GenerationAttempts == 0 ? 1 : 11),
                "/api/v1/admin/test-data/organizations?search=org-b" => new[] { new { id = Guid.Parse("33333333-3333-3333-3333-333333333333"), name = "Организация B", ownerUserId = "owner-b", ownerEmail = "contact@example.invalid", scenarioCode = "detailing", isActive = true, vehiclesCount = 1, maintenanceCount = 0 } },
                "/api/v1/admin/test-data/organizations/11111111-1111-1111-1111-111111111111/vehicles" => new[] { new { id = Guid.Parse("22222222-2222-2222-2222-222222222222"), kind = 1, displayName = "Toyota Camry · 2022", brandName = "Toyota", modelName = "Camry", yearIssue = (int?)2022, maintenanceCount = 3, defectCount = 1 } },
                _ => new { message = "OK" }
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(body) });
        }

        private static object CreateTokens()
        {
            var now = DateTimeOffset.UtcNow;
            return new
            {
                accessToken = "access-token", accessTokenExpiresAt = now.AddHours(1),
                refreshToken = "refresh-token", refreshTokenExpiresAt = now.AddDays(10), tokenType = "Bearer",
                user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = new[] { "System" } }
            };
        }

        private static object[] CreateOrganizations(int vehiclesCount) =>
        [new { id = Guid.Parse("11111111-1111-1111-1111-111111111111"), name = "СТО Автопарк", ownerUserId = "owner", ownerEmail = "contact@example.invalid", scenarioCode = "service-station", isActive = true, vehiclesCount, maintenanceCount = 3 }];
    }
}
