using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
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

public sealed class SubscriptionPlansViewTests
{
    [Fact]
    public async Task PlanEditorSavesBusinessFieldsAndKeepsPersonalPlanReadOnly()
    {
        using var handler = new SubscriptionPlansHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();

        Assert.Equal(1, Assert.Single(model.PersonalRows).Id);
        Assert.True(model.BusinessRows.Single(row => row.Id == 3).CanToggle);
        Assert.False(model.BusinessRows.Single(row => row.Id == 4).CanToggle);
        await model.OpenPlanCommand.ExecuteAsync(model.BusinessRows.Single(row => row.Id == 3));
        Assert.Equal(2, model.Features.Count);
        model.Editor.Name = "  Команда  ";
        model.Editor.Description = "Обновлённое описание";
        model.Editor.Price = 2000;
        model.Editor.DiscountPrice = 1750;
        model.Editor.DurationDays = 90;
        model.Editor.VehicleLimit = 20;
        model.Editor.Order = 4;
        model.Editor.Group = "Рабочие";
        model.Editor.IsDemoPlan = true;
        await model.SavePlanCommand.ExecuteAsync(null);

        var update = Assert.Single(handler.Mutations, request =>
            request.Method == "PUT" && request.Path == "/api/v1/admin/subscription-plans/3");
        using var updateBody = JsonDocument.Parse(update.Body!);
        var plan = updateBody.RootElement;
        Assert.Equal("Команда", plan.GetProperty("name").GetString());
        Assert.Equal("business", plan.GetProperty("planTypeCode").GetString());
        Assert.Equal("Рабочие", plan.GetProperty("group").GetString());
        Assert.Equal(90, plan.GetProperty("durationDays").GetInt32());
        Assert.Equal(20, plan.GetProperty("vehicleLimit").GetInt32());
        Assert.True(plan.GetProperty("isActive").GetBoolean());
        Assert.True(plan.GetProperty("isDemoPlan").GetBoolean());
        Assert.True(model.HasMessage);

        await model.OpenPlanCommand.ExecuteAsync(model.PersonalRows.Single());
        Assert.True(model.IsReadOnly);
        Assert.False(model.SavePlanCommand.CanExecute(null));
        Assert.Empty(model.Features);
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task FeatureValuesSaveForAllDurationsAndDeleteRequiresConfirmation()
    {
        using var handler = new SubscriptionPlansHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();
        await model.OpenPlanCommand.ExecuteAsync(model.BusinessRows.Single(row => row.Id == 3));
        model.Features[0].BoolValue = false;
        model.Features[1].IntValue = 40;
        await model.SaveFeaturesCommand.ExecuteAsync(null);

        var featureSave = Assert.Single(handler.Mutations, request => request.Path.EndsWith("/features", StringComparison.Ordinal));
        using var body = JsonDocument.Parse(featureSave.Body!);
        var values = body.RootElement.GetProperty("features").EnumerateArray().ToArray();
        Assert.False(values[0].GetProperty("boolValue").GetBoolean());
        Assert.Equal(40, values[1].GetProperty("intValue").GetInt32());
        Assert.Contains("всех сроков оплаты", model.Message, StringComparison.OrdinalIgnoreCase);

        model.SelectedPlan = model.Plans.Single(plan => plan.Id == 4);
        model.IsCreating = false;
        Assert.False(model.CanDeletePlan);
        model.RequestDeleteCommand.Execute(null);
        Assert.False(model.IsDeleteConfirmationOpen);

        model.SelectedPlan = model.Plans.Single(plan => plan.Id == 3);
        model.RequestDeleteCommand.Execute(null);
        Assert.True(model.IsDeleteConfirmationOpen);
        Assert.True(model.ConfirmDeleteCommand.CanExecute(null));
        model.CancelDeleteCommand.Execute(null);
        Assert.False(model.IsDeleteConfirmationOpen);
        Assert.DoesNotContain(handler.Mutations, request => request.Method == "DELETE");
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task SystemFreeKeepsItsRulesAndAllowsQuotaChanges()
    {
        using var handler = new SubscriptionPlansHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();

        var freeRow = Assert.Single(model.BusinessRows, row => row.Id == 4);
        Assert.False(freeRow.CanToggle);
        Assert.Equal("Бесплатно", freeRow.PriceLabel);
        Assert.Equal("Бессрочно", freeRow.DurationLabel);
        await model.OpenPlanCommand.ExecuteAsync(freeRow);

        Assert.True(model.Editor.IsSystemFree);
        Assert.False(model.CanEditActiveFlag);
        Assert.False(model.CanEditDefaultFlag);
        Assert.False(model.CanEditDemoFlag);
        Assert.False(model.CanDeletePlan);
        model.Editor.Price = 500;
        model.Editor.DurationDays = 30;
        model.Editor.VehicleLimit = 300;
        await model.SavePlanCommand.ExecuteAsync(null);

        var update = Assert.Single(handler.Mutations, request => request.Method == "PUT");
        using var updateBody = JsonDocument.Parse(update.Body!);
        Assert.Equal(0, updateBody.RootElement.GetProperty("price").GetDecimal());
        Assert.Equal(30, updateBody.RootElement.GetProperty("durationDays").GetInt32());
        Assert.Equal("workspace.free.v1", updateBody.RootElement.GetProperty("group").GetString());
        Assert.Equal(300, updateBody.RootElement.GetProperty("vehicleLimit").GetInt32());
        Assert.True(updateBody.RootElement.GetProperty("isActive").GetBoolean());
        Assert.False(updateBody.RootElement.GetProperty("isDefaultPlan").GetBoolean());
        Assert.False(updateBody.RootElement.GetProperty("isDemoPlan").GetBoolean());
    }

    [Fact]
    public async Task SavingNewPlanSwitchesToEditingAndPreventsDuplicatePost()
    {
        using var handler = new SubscriptionPlansHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();
        model.CreatePlanCommand.Execute(null);
        model.Editor.Name = "Новый тариф";
        await model.SavePlanCommand.ExecuteAsync(null);
        Assert.False(model.IsCreating);
        Assert.Equal(3, model.SelectedPlan!.Id);
        Assert.Equal("Организация", model.Editor.Name);
        model.Editor.Name = "Изменённый тариф";
        await model.SavePlanCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "POST", "PUT" }, handler.Mutations.Select(item => item.Method));
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task UnconfirmedMutationRequiresRefreshBeforeRetry()
    {
        using var handler = new SubscriptionPlansHandler { FailNextMutation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();
        model.CreatePlanCommand.Execute(null);
        model.Editor.Name = "Новый тариф";
        await model.SavePlanCommand.ExecuteAsync(null);
        Assert.True(model.HasError);
        Assert.False(model.SavePlanCommand.CanExecute(null));
        Assert.True(model.RefreshCommand.CanExecute(null));
        Assert.Single(handler.Mutations);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.SavePlanCommand.CanExecute(null));
        await model.DeactivateAsync(true);
    }

    [Fact]
    public async Task SuccessfulToggleWithFailedReloadCannotToggleStaleRowAgain()
    {
        using var handler = new SubscriptionPlansHandler { FailReloadAfterMutation = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();
        var row = model.BusinessRows.Single(item => item.Id == 3);
        await model.TogglePlanCommand.ExecuteAsync(row);
        Assert.Single(handler.Mutations);
        Assert.True(model.HasError);
        Assert.False(model.TogglePlanCommand.CanExecute(row));
        Assert.True(model.RefreshCommand.CanExecute(null));
        handler.FailReloadAfterMutation = false;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.True(model.TogglePlanCommand.CanExecute(model.BusinessRows.Single(item => item.Id == 3)));
        await model.DeactivateAsync(true);
    }

    [AvaloniaTheory]
    [InlineData(1160)]
    [InlineData(592)]
    [InlineData(2000)]
    public async Task EditorRendersFeatureFieldsAndScreenshotCanBeCaptured(int width)
    {
        using var handler = new SubscriptionPlansHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();
        await model.OpenPlanCommand.ExecuteAsync(model.BusinessRows.Single(row => row.Id == 3));
        var window = new Window { Width = width, Height = 900, Content = new SubscriptionPlansView { DataContext = model } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var name = window.GetVisualDescendants().OfType<TextBox>()
                .Single(control => AutomationProperties.GetAutomationId(control) == "PlanName");
            Assert.Equal("Организация", name.Text);
            Assert.Contains(window.GetVisualDescendants().OfType<CheckBox>(), checkBox => checkBox.Content?.ToString() == "Участники");
            Assert.Contains(window.GetVisualDescendants().OfType<NumericUpDown>(), numeric => numeric.Value == 15);
            SubscriptionPlansViewTests.SaveScreenshot(window, $"subscription-plans-editor-{width}.png");
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(592)]
    [InlineData(1160)]
    [InlineData(2000)]
    public async Task ClosedEditorShowsFullWidthPlanRowsWithExplicitOpenActions(int width)
    {
        using var handler = new SubscriptionPlansHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await SubscriptionPlansViewTests.LoginAsync(client);
        var model = new SubscriptionPlansViewModel(client);
        await model.ActivateAsync();
        var window = new Window { Width = width, Height = 900, Content = new SubscriptionPlansView { DataContext = model } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.False(model.IsEditorOpen);
            Assert.Equal(2, model.BusinessRows.Count);
            Assert.Single(model.PersonalRows);
            Assert.Equal("2 тарифа", model.BusinessPlanCountLabel);
            Assert.Equal("1 тариф", model.PersonalPlanCountLabel);
            Assert.Equal(3, window.GetVisualDescendants().OfType<Button>()
                .Count(button => AutomationProperties.GetAutomationId(button) == "OpenSubscriptionPlan"));
            SubscriptionPlansViewTests.SaveScreenshot(window, $"subscription-plans-list-{width}.png");
        }
        finally
        {
            await model.DeactivateAsync(true);
            window.Close();
        }
    }

    private static async Task LoginAsync(AdminSessionClient client)
    {
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
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
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = File.Create(Path.Combine(directory, fileName));
        frame.Save(stream, PngBitmapEncoderOptions.Default);
    }

    private sealed class SubscriptionPlansHandler : HttpMessageHandler
    {
        private readonly DateTime _created = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

        public bool FailNextMutation { get; set; }

        public bool FailReloadAfterMutation { get; set; }

        public List<(string Method, string Path, string? Body)> Mutations { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path is "/api/v1/auth/login" or "/api/v1/auth/refresh")
            {
                return SubscriptionPlansViewTests.Json(HttpStatusCode.OK, new
                {
                    accessToken = "access-token",
                    accessTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30),
                    refreshToken = "refresh",
                    refreshTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                    user = new { id = "admin", email = "contact@example.invalid" }
                });
            }

            if (path == "/api/v1/admin/current-user")
            {
                return SubscriptionPlansViewTests.Json(HttpStatusCode.OK, new
                {
                    id = "admin", email = "contact@example.invalid", isSystem = false,
                    permissions = new[] { "Admin.Access", "Admin.Configuration.Manage" }
                });
            }

            if (path == "/api/v1/admin/subscription-plans" && request.Method == HttpMethod.Get)
            {
                if (this.FailReloadAfterMutation && this.Mutations.Count > 0)
                {
                    throw new HttpRequestException("Не удалось обновить список после операции.");
                }

                return SubscriptionPlansViewTests.Json(HttpStatusCode.OK, new[]
                {
                    this.Plan(1, "Личный", "personal", true, true, false, false, 0),
                    this.Plan(3, "Организация", "business", true, false, false, true, 15),
                    this.Plan(4, "Бесплатный", "business", true, false, false, true, 5, "workspace.free.v1")
                });
            }

            if (path == "/api/v1/admin/subscription-plans/3/features" && request.Method == HttpMethod.Get)
            {
                return SubscriptionPlansViewTests.Json(HttpStatusCode.OK, new[]
                {
                    new { featureId = 5, code = "multi_user", name = "Участники", description = "Доступ участников",
                        category = "Команда", valueType = "Boolean", isAssigned = true, boolValue = (bool?)true, intValue = (int?)null },
                    new { featureId = 6, code = "vehicle_limit", name = "Лимит техники", description = "Количество машин",
                        category = "Лимиты", valueType = "Integer", isAssigned = true, boolValue = (bool?)null, intValue = (int?)15 }
                });
            }

            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            this.Mutations.Add((request.Method.Method, path, body));
            if (this.FailNextMutation)
            {
                this.FailNextMutation = false;
                throw new HttpRequestException("Соединение прервано после отправки запроса.");
            }

            return SubscriptionPlansViewTests.Json(HttpStatusCode.OK, new { id = 3, name = "Организация", message = "Функции тарифа сохранены для всех сроков оплаты." });
        }

        private object Plan(int id, string name, string type, bool active, bool isDefault, bool isDemo, bool canDelete, int vehicleLimit, string group = "Основные") => new
        {
            id,
            name,
            description = "Описание тарифа для организаций.",
            price = group == "workspace.free.v1" ? 0m : 1500m,
            discountPrice = (decimal?)null,
            effectivePrice = group == "workspace.free.v1" ? 0m : 1500m,
            hasDiscount = false,
            durationDays = 30,
            isActive = active,
            vehicleLimit,
            order = id,
            group,
            planTypeCode = type,
            planTypeName = type == "business" ? "Организационный" : "Личный",
            isDemoPlan = isDemo,
            isDefaultPlan = isDefault,
            hasUsageHistory = !canDelete,
            canDelete,
            createdAt = this._created
        };
    }

    private static HttpResponseMessage Json(HttpStatusCode status, object content) =>
        new(status) { Content = JsonContent.Create(content) };
}
