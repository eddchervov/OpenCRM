using System.Globalization;
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

public sealed class PromoCodesTests
{
    [AvaloniaFact]
    public async Task PromoCodeEditorsShowStableSelectionsAndSaveDiscountForRussianCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        using var handler = new PromoCodesHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", CancellationToken.None);
        var model = new PromoCodesViewModel(client);
        var window = new Window { Width = 1160, Height = 1050, Content = new PromoCodesView { DataContext = model } };
        window.Show();
        try
        {
            await model.ActivateAsync();
            Dispatcher.UIThread.RunJobs();
            PromoCodesTests.SaveScreenshot(window, "promo-codes-list.png");

            var gift = model.Rows.Single(row => row.Id == 8);
            await model.OpenRowCommand.ExecuteAsync(gift);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(model.BenefitOptions[0], model.SelectedBenefit);
            var benefit = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(comboBox => AutomationProperties.GetAutomationId(comboBox) == "PromoCodeBenefit");
            Assert.Same(model.SelectedBenefit, benefit.SelectedItem);
            Assert.Contains(benefit.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Подарочные дни");
            var giftPlan = window.GetVisualDescendants().OfType<ComboBox>()
                .Single(comboBox => AutomationProperties.GetAutomationId(comboBox) == "PromoCodeGiftPlan");
            Assert.Same(model.GiftPlan, giftPlan.SelectedItem);
            Assert.True(giftPlan.Bounds.Width > 0 && giftPlan.Bounds.Height > 0);
            PromoCodesTests.SaveScreenshot(window, "promo-codes-gift-editor.png");
            foreach (var width in new[] { 592, 2000 })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                PromoCodesTests.SaveScreenshot(window, $"promo-codes-gift-editor-{width}.png");
            }
            window.Width = 1160;
            Assert.Equal("Бизнес 2", Assert.IsType<AdminSubscriptionPlan>(model.GiftPlan).Name);
            await model.CloseEditorCommand.ExecuteAsync(null);
            window.Width = 900;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetAutomationId(button) == "PromoCodeOpen" && button.Bounds.Width > 0);
            PromoCodesTests.SaveScreenshot(window, "promo-codes-list-900.png");
            window.Width = 592;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button =>
                AutomationProperties.GetAutomationId(button) == "PromoCodeOpen" && button.Bounds.Width > 0);
            PromoCodesTests.SaveScreenshot(window, "promo-codes-list-min-shell.png");
            window.Width = 1160;

            var discount = model.Rows.Single(row => row.Id == 7);
            await model.OpenRowCommand.ExecuteAsync(discount);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("12,5", model.DiscountValueText);
            Assert.False(model.CanEditScope);
            PromoCodesTests.SaveScreenshot(window, "promo-codes-applied-discount.png");
            Assert.Contains(window.GetVisualDescendants().OfType<Button>(), button =>
                button.CommandParameter is AdminSubscriptionPlan plan && plan.Id == 11 && button.Bounds.Width > 0);
            var scrollViewer = window.GetVisualDescendants().OfType<ScrollViewer>()
                .First(viewer => viewer.Extent.Height > viewer.Bounds.Height);
            scrollViewer.Offset = new Vector(0, scrollViewer.Extent.Height);
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "contact@example.invalid");
            PromoCodesTests.SaveScreenshot(window, "promo-codes-usage-history.png");
            scrollViewer.Offset = Vector.Zero;
            Dispatcher.UIThread.RunJobs();

            model.CreateCommand.Execute(null);
            model.Code = " DETAIL15 ";
            model.Name = "Скидка для детейлинга";
            model.SelectedBenefit = model.BenefitOptions.Single(option => option.Code == "percentage");
            model.DiscountValueText = "12,5";
            model.SelectedPlans = [model.AvailablePaidPlans.Single(plan => plan.Id == 11)];
            model.AppliesToAllScenarios = false;
            await model.SaveCommand.ExecuteAsync(null);
            Assert.Empty(handler.Mutations);
            Assert.Contains("направление", model.ErrorMessage, StringComparison.OrdinalIgnoreCase);

            model.DetailingScenario = true;
            PromoCodesTests.SaveScreenshot(window, "promo-codes-discount-editor.png");
            await model.SaveCommand.ExecuteAsync(null);

            using var request = JsonDocument.Parse(Assert.Single(handler.Mutations).Body);
            Assert.Equal("DETAIL15", request.RootElement.GetProperty("code").GetString());
            Assert.Equal(12.5m, request.RootElement.GetProperty("discountValue").GetDecimal());
            Assert.False(request.RootElement.GetProperty("appliesToAllPaidPlans").GetBoolean());
            Assert.Equal(new[] { 11 }, request.RootElement.GetProperty("subscriptionPlanIds").EnumerateArray()
                .Select(planId => planId.GetInt32()).ToArray());
            Assert.Equal(new[] { "detailing" }, request.RootElement.GetProperty("scenarioCodes").EnumerateArray()
                .Select(scenario => scenario.GetString()).ToArray());
            Assert.Null(model.SelectedPromoCode);
            await model.DeactivateAsync(clear: true);
        }
        finally
        {
            await model.DeactivateAsync(clear: true);
            window.Close();
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task LegacyPromoCodePreservesItsImmutableBenefitWhenEditingMetadata()
    {
        using var handler = new PromoCodesHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", CancellationToken.None);
        var model = new PromoCodesViewModel(client);
        await model.ActivateAsync();
        await model.OpenRowCommand.ExecuteAsync(model.Rows.Single(row => row.Id == 9));

        model.Name = "Новое имя старого формата";
        await model.SaveCommand.ExecuteAsync(null);

        using var request = JsonDocument.Parse(Assert.Single(handler.Mutations).Body);
        Assert.Equal("registration", request.RootElement.GetProperty("discountTypeCode").GetString());
        Assert.Equal(14, request.RootElement.GetProperty("demoDurationDays").GetInt32());
        Assert.Empty(request.RootElement.GetProperty("subscriptionPlanIds").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, request.RootElement.GetProperty("giftTemplatePlanId").ValueKind);
        await model.DeactivateAsync(clear: true);
    }

    [Fact]
    public async Task UnknownCreateOutcomeBlocksDuplicateUntilRefresh()
    {
        using var handler = new PromoCodesHandler { FailCreateWithServerError = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", CancellationToken.None);
        var model = new PromoCodesViewModel(client);
        await model.ActivateAsync();
        model.CreateCommand.Execute(null);
        model.Code = "UNKNOWN";
        model.Name = "Неопределённое создание";
        model.GiftPlan = model.AvailablePaidPlans[0];

        await model.SaveCommand.ExecuteAsync(null);

        Assert.True(model.IsMutationUncertain);
        Assert.False(model.CanEdit);
        Assert.Single(handler.Mutations);
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.IsMutationUncertain);
        Assert.True(model.CanEdit);
        Assert.Single(handler.Mutations);
        await model.DeactivateAsync(clear: true);
    }

    [Fact]
    public async Task SuccessfulUpdateWithFailedReloadBlocksFurtherChangesUntilRefresh()
    {
        using var handler = new PromoCodesHandler { FailPromoCodeListAfterUpdate = true };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", CancellationToken.None);
        var model = new PromoCodesViewModel(client);
        await model.ActivateAsync();
        await model.OpenRowCommand.ExecuteAsync(model.Rows.Single(row => row.Id == 9));
        model.Name = "Обновлённое имя";

        await model.SaveCommand.ExecuteAsync(null);

        Assert.True(model.IsMutationUncertain);
        Assert.False(model.CanEdit);
        Assert.Equal("Обновлённое имя", model.Name);
        Assert.Single(handler.Mutations);
        handler.FailPromoCodeListAfterUpdate = false;
        await model.RefreshCommand.ExecuteAsync(null);
        Assert.False(model.IsMutationUncertain);
        Assert.True(model.CanEdit);
        Assert.Single(handler.Mutations);
        await model.DeactivateAsync(clear: true);
    }

    [Fact]
    public async Task CalendarDatesAreSavedInUtcAndCanBeCleared()
    {
        using var handler = new PromoCodesHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://admin.test/") };
        using var client = new AdminSessionClient(http);
        await client.LoginAsync("contact@example.invalid", "password", TestContext.Current.CancellationToken);
        var model = new PromoCodesViewModel(client);
        await model.ActivateAsync();
        model.CreateCommand.Execute(null);
        model.Code = "CALENDAR30";
        model.Name = "Проверка календаря";
        model.GiftPlan = model.AvailablePaidPlans.First(plan => plan.IsActive);
        var from = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Unspecified);
        var until = from.AddDays(30).AddHours(8);
        model.ValidFromLocal = from;
        model.ValidUntilLocal = until;
        await model.SaveCommand.ExecuteAsync(null);
        using var body = JsonDocument.Parse(Assert.Single(handler.Mutations).Body);
        Assert.Equal(DateTime.SpecifyKind(from, DateTimeKind.Local).ToUniversalTime(), body.RootElement.GetProperty("validFrom").GetDateTime());
        Assert.Equal(DateTime.SpecifyKind(until, DateTimeKind.Local).ToUniversalTime(), body.RootElement.GetProperty("validUntil").GetDateTime());
        model.ValidFromLocal = null;
        model.ValidUntilLocal = null;
        Assert.Empty(model.ValidFromText);
        Assert.Empty(model.ValidUntilText);
        await model.DeactivateAsync(true);
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

    private sealed class PromoCodesHandler : HttpMessageHandler
    {
        private static readonly DateTime s_now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        public List<(string Path, string Body)> Mutations { get; } = [];
        public bool FailCreateWithServerError { get; init; }
        public bool FailPromoCodeListAfterUpdate { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/v1/auth/login")
            {
                return JsonResponse(HttpStatusCode.OK, CreateTokens());
            }

            if (path == "/api/v1/admin/current-user")
            {
                return JsonResponse(HttpStatusCode.OK, new
                {
                    id = "admin-1",
                    email = "contact@example.invalid",
                    roles = new[] { "Admin" },
                    permissions = new[] { "Admin.Access", "Admin.Configuration.Manage" }
                });
            }

            if (path == "/api/v1/admin/promo-codes" && request.Method == HttpMethod.Get)
            {
                if (this.FailPromoCodeListAfterUpdate && this.Mutations.Any(mutation => mutation.Path.EndsWith("/9", StringComparison.Ordinal)))
                {
                    return JsonResponse(HttpStatusCode.InternalServerError, new { message = "Не удалось обновить список." });
                }

                return JsonResponse(HttpStatusCode.OK, new[] { CreatePromoCode(7), CreatePromoCode(8), CreateLegacyPromoCode() });
            }

            if (path == "/api/v1/admin/subscription-plans")
            {
                return JsonResponse(HttpStatusCode.OK, new[]
                {
                    new { id = 11, name = "Бизнес 1", planTypeCode = "business", isActive = true, isDefaultPlan = false, isDemoPlan = false, order = 1, durationDays = 30 },
                    new { id = 12, name = "Бизнес 2", planTypeCode = "business", isActive = true, isDefaultPlan = false, isDemoPlan = false, order = 2, durationDays = 90 }
                });
            }

            if (path.EndsWith("/usages", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                var promoCodeId = int.Parse(path.Split('/')[^2], CultureInfo.InvariantCulture);
                return JsonResponse(HttpStatusCode.OK, promoCodeId == 7
                    ? new[] { new { id = 1L, userId = "user-1", email = "contact@example.invalid", organizationId = Guid.NewGuid(), organizationName = "Детейлинг", redeemedAt = s_now, assignedAt = s_now, demoAppliedAt = (DateTime?)null, applicationStatus = "claimed", discountValidUntil = (DateTime?)null, subscriptionPaymentId = 503, sourceCode = "share" } }
                    : Array.Empty<object>());
            }

            if (path == "/api/v1/admin/promo-codes" && request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                this.Mutations.Add((path, body));
                if (this.FailCreateWithServerError)
                {
                    return JsonResponse(HttpStatusCode.InternalServerError, new { message = "Неизвестный результат." });
                }

                return JsonResponse(HttpStatusCode.Created, CreatePromoCode(101));
            }

            if (path == "/api/v1/admin/promo-codes/9" && request.Method == HttpMethod.Put)
            {
                var body = await request.Content!.ReadAsStringAsync(cancellationToken);
                this.Mutations.Add((path, body));
                return JsonResponse(HttpStatusCode.OK, CreateLegacyPromoCode());
            }

            throw new InvalidOperationException($"Неожиданный запрос: {request.Method} {path}");
        }

        private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) =>
            new(statusCode) { Content = JsonContent.Create(value) };

        private static object CreateTokens()
        {
            var now = DateTimeOffset.UtcNow;
            return new
            {
                accessToken = "access-token",
                accessTokenExpiresAt = now.AddHours(1),
                refreshToken = "refresh-token",
                refreshTokenExpiresAt = now.AddDays(10),
                tokenType = "Bearer",
                user = new { id = "admin-1", email = "contact@example.invalid", userName ="admin", roles = new[] { "Admin" } }
            };
        }

        private static object CreatePromoCode(int id) => new
        {
            id,
            code = id == 8 ? "GIFT30" : "ORG12",
            name = id == 8 ? "Подарочные дни" : "Скидка организациям",
            description = "Описание",
            isActive = true,
            validFrom = (DateTime?)null,
            validUntil = (DateTime?)null,
            maximumRedemptions = (int?)null,
            redemptionCount = 0,
            remainingRedemptions = (int?)null,
            demoDurationDays = (int?)null,
            discountTypeCode = id == 8 ? "bonus_days" : "percentage",
            discountValue = id == 8 ? (decimal?)null : 12.5m,
            discountValidityDays = id == 8 ? (int?)null : 45,
            discountPaymentLimitPerUser = (int?)null,
            appliesToAllPaidPlans = false,
            subscriptionPlanIds = id == 8 ? Array.Empty<int>() : new[] { 11 },
            subscriptionPlanNames = id == 8 ? Array.Empty<string>() : new[] { "Бизнес 1" },
            bonusDays = id == 8 ? 30 : (int?)null,
            maximumOrganizationApplications = 100,
            organizationApplicationCount = id == 7 ? 1 : 0,
            remainingOrganizationApplications = id == 7 ? 99 : 100,
            appliesToAllScenarios = id == 8,
            scenarioCodes = id == 8 ? Array.Empty<string>() : new[] { "detailing" },
            giftTemplatePlanId = id == 8 ? 12 : (int?)null,
            isOrganizationCode = true,
            createdAt = s_now,
            updatedAt = s_now
        };

        private static object CreateLegacyPromoCode() => new
        {
            id = 9,
            code = "OLDDEMO",
            name = "Старый промокод",
            description = "Старый формат",
            isActive = true,
            validFrom = (DateTime?)null,
            validUntil = (DateTime?)null,
            maximumRedemptions = 50,
            redemptionCount = 3,
            remainingRedemptions = 47,
            demoDurationDays = 14,
            discountTypeCode = "registration",
            discountValue = (decimal?)null,
            discountValidityDays = (int?)null,
            discountPaymentLimitPerUser = (int?)null,
            appliesToAllPaidPlans = true,
            subscriptionPlanIds = Array.Empty<int>(),
            subscriptionPlanNames = Array.Empty<string>(),
            bonusDays = (int?)null,
            maximumOrganizationApplications = (int?)null,
            organizationApplicationCount = 0,
            remainingOrganizationApplications = (int?)null,
            appliesToAllScenarios = true,
            scenarioCodes = Array.Empty<string>(),
            giftTemplatePlanId = (int?)null,
            isOrganizationCode = false,
            createdAt = s_now,
            updatedAt = s_now
        };
    }
}
