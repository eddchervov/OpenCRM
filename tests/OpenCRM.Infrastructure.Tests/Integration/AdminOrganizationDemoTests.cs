using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Testing;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Admin;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Infrastructure.Payments;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Auth;
using OpenCRM.WebApp.Controllers.Admin.Api;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class AdminOrganizationDemoTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task CanceledYooKassaPaymentIsRemovedFromPurchasesAndRevenue()
    {
        await using var context = fixture.CreateContext();
        var (organization, admin) = await CreateOrganizationAsync(context);
        var plan = new SubscriptionPlan
        {
            Name = "Тариф отменённой оплаты",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsActive = true,
            DurationDays = 30
        };
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();

        var payment = new SubscriptionPayment
        {
            UserId = organization.OwnerUserId,
            SubscriptionPlanId = plan.Id,
            Amount = 4380,
            CreatedAt = DateTime.UtcNow,
            PaidAt = DateTime.UtcNow,
            PaymentMethod = "yookassa_workspace_purchase:" + organization.Id,
            PaymentStatus = "completed",
            ExternalPaymentId = Guid.NewGuid().ToString()
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        var yooKassa = new Mock<IYooKassaService>();
        yooKassa.Setup(service => service.GetPaymentStatusAsync(payment.ExternalPaymentId))
            .ReturnsAsync(new PaymentResponse
            {
                IsSuccess = true,
                PaymentId = payment.ExternalPaymentId,
                Status = "canceled",
                Paid = false,
                Amount = payment.Amount,
                Currency = "RUB"
            });
        var billingService = AdminBillingTestServices.Create<IAdminPaymentCommandService>(context, yooKassa.Object);
        var before = await AdminBillingTestServices.CreateQuery(context).GetAsync();

        var result = await billingService.SyncPaymentWithYooKassaAsync(payment.Id, admin.Id);

        Assert.True(result.Succeeded);
        Assert.Equal("canceled", result.Status);
        Assert.Null((await context.SubscriptionPayments.AsNoTracking().SingleAsync(item => item.Id == payment.Id)).PaidAt);
        Assert.DoesNotContain(await AdminBillingTestServices.CreateQuery(context).GetPurchasesAsync(), item => item.PaymentId == payment.Id);
        var after = await AdminBillingTestServices.CreateQuery(context).GetAsync();
        Assert.Equal(payment.Amount, before.Kpis.Payments30DaysAmount - after.Kpis.Payments30DaysAmount);
        Assert.Equal(1, before.Kpis.Payments30DaysCount - after.Kpis.Payments30DaysCount);
        Assert.Contains(after.RawStatusBreakdown, item => item.Status == "canceled" && item.Group == "canceled");
    }

    [Fact]
    public async Task PaidPaymentRemainsInPurchaseRegisterWhenActivationFails()
    {
        await using var context = fixture.CreateContext();
        var (organization, admin) = await CreateOrganizationAsync(context);
        var plan = new SubscriptionPlan
        {
            Name = "Тариф с ошибкой активации",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsActive = true,
            DurationDays = 30
        };
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();

        var payment = new SubscriptionPayment
        {
            UserId = organization.OwnerUserId,
            SubscriptionPlanId = plan.Id,
            Amount = 1200,
            CreatedAt = DateTime.UtcNow,
            PaymentMethod = "yookassa_workspace_purchase:" + organization.Id,
            PaymentStatus = "pending",
            ExternalPaymentId = Guid.NewGuid().ToString(),
            BuyerTypeCode = "business",
            BuyerName = "ООО Тест",
            BuyerInn = "7707083893"
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        using var httpClient = new HttpClient();
        var yooKassaService = new YooKassaService(
            httpClient,
            NullLogger<YooKassaService>.Instance,
            Options.Create(new YooKassaConfig()),
            context);
        Assert.True(await yooKassaService.MarkPaymentPaidAsync(payment.ExternalPaymentId));
        payment.PaymentStatus = "failed";
        await context.SaveChangesAsync();

        var billingService = AdminBillingTestServices.CreateQuery(context);
        var purchase = Assert.Single(await AdminBillingTestServices.CreateQuery(context).GetPurchasesAsync(), item => item.PaymentId == payment.Id);
        Assert.Equal("failed", purchase.PaymentStatus);
        Assert.Equal("7707083893", purchase.BuyerInn);

        var receiptResult = await AdminBillingTestServices.Create<IAdminNpdReceiptService>(context).MarkNpdReceiptAsync(payment.Id, true, false, "Выдан покупателю", admin.Id);
        Assert.True(receiptResult.Succeeded);
        Assert.True(await context.AdminNpdReceiptAudits.AnyAsync(item => item.SubscriptionPaymentId == payment.Id && item.Issued));
    }

    [Fact]
    public async Task AdminCanGrantPlansWithoutPaymentAndCannotReplaceActiveAccess()
    {
        await using var context = fixture.CreateContext();
        var (organization, admin) = await CreateOrganizationAsync(context);
        var personalPlan = new SubscriptionPlan
        {
            Name = "Личный тариф для ручной выдачи",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal,
            IsActive = true,
            DurationDays = 30
        };
        var businessPlan = new SubscriptionPlan
        {
            Name = "Тариф СТО для ручной выдачи",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsActive = true,
            DurationDays = 30
        };
        context.SubscriptionPlans.AddRange(personalPlan, businessPlan);
        await context.SaveChangesAsync();
        var service = AdminBillingTestServices.Create<IAdminSubscriptionCommandService>(context);

        var userResult = await service.GrantUserPlanAsync(organization.OwnerUserId, personalPlan.Id, 45, "Пилот СТО", admin.Id);
        var organizationResult = await service.GrantOrganizationPlanAsync(organization.Id, businessPlan.Id, 60, "Пилот СТО", admin.Id);

        Assert.True(userResult.Succeeded);
        Assert.True(organizationResult.Succeeded);
        Assert.False((await service.GrantUserPlanAsync(organization.OwnerUserId, personalPlan.Id, 30, "Повтор", admin.Id)).Succeeded);
        Assert.False((await service.GrantOrganizationPlanAsync(organization.Id, businessPlan.Id, 30, "Повтор", admin.Id)).Succeeded);
        Assert.Equal("manual", (await context.UserSubscriptions.SingleAsync(item => item.UserId == organization.OwnerUserId)).SourceCode);
        Assert.Equal("manual", (await context.OrganizationSubscriptions.SingleAsync(item => item.OrganizationId == organization.Id)).SourceCode);
        Assert.Equal(2, await context.AdminSubscriptionAudits.CountAsync(item => item.Action == "granted" && item.ChangedByUserId == admin.Id));
        Assert.False(await context.SubscriptionPayments.AnyAsync(item => item.UserId == organization.OwnerUserId));

        var userSubscription = await context.UserSubscriptions.SingleAsync(item => item.UserId == organization.OwnerUserId);
        var historyId = await context.UserSubscriptionHistory
            .Where(item => item.UserId == organization.OwnerUserId)
            .Select(item => item.Id)
            .SingleAsync();
        context.UserSubscriptions.Remove(userSubscription);
        await context.SaveChangesAsync();

        var audits = await AdminBillingTestServices.CreateQuery(context).GetSubscriptionAuditsAsync("users", -historyId);
        Assert.Contains(audits, item => item.Action == "granted" && item.ChangedBy == admin.Id);
    }

    [Fact]
    public async Task AdminCanGrantAndRevokeDemoWithoutDeletingSubscriptionHistory()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка работает с тарифами организации, поэтому коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var (organization, admin) = await CreateOrganizationAsync(context, OrganizationTypeCodes.Fleet);
        var service = AdminBillingTestServices.Create<IAdminSubscriptionCommandService>(context);

        var granted = await service.GrantOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка");

        Assert.True(granted.Succeeded);
        var subscription = await context.OrganizationSubscriptions
            .Include(item => item.SubscriptionPlan)
            .SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.True(subscription.SubscriptionPlan.IsDemoPlan);
        Assert.Equal("demo", subscription.SourceCode);
        Assert.True(subscription.EndDate > DateTime.UtcNow);
        Assert.False((await service.GrantOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка")).Succeeded);

        var revoked = await service.RevokeOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка");

        Assert.True(revoked.Succeeded);
        Assert.True(subscription.EndDate < DateTime.UtcNow);
        Assert.False(subscription.AutoRenewal);
        Assert.Equal(2, await context.OrganizationAuditEvents.CountAsync(item => item.OrganizationId == organization.Id));
        var subscriptionAudits = await context.AdminSubscriptionAudits
            .Where(item => item.Scope == "organizations" && item.SubscriptionId == subscription.Id)
            .OrderBy(item => item.ChangedAt)
            .ToListAsync();
        Assert.Equal(["demo_granted", "demo_revoked"], subscriptionAudits.Select(item => item.Action));
        Assert.All(subscriptionAudits, item => Assert.Equal(admin.Id, item.ChangedByUserId));
        Assert.False((await service.RevokeOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка")).Succeeded);
        Assert.Equal(1, await context.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task ActivePaidPlanAndUnrelatedUserCannotReceiveDemo()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка работает с тарифами организации, поэтому коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var (organization, admin) = await CreateOrganizationAsync(context, OrganizationTypeCodes.Fleet);
        var paidPlan = new SubscriptionPlan
        {
            Name = "Платный тариф для проверки",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsActive = true,
            DurationDays = 30
        };
        context.SubscriptionPlans.Add(paidPlan);
        var paidSubscription = new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlan = paidPlan,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = DateTime.UtcNow.AddDays(15)
        };
        context.OrganizationSubscriptions.Add(paidSubscription);
        await context.SaveChangesAsync();
        var service = AdminBillingTestServices.Create<IAdminSubscriptionCommandService>(context);

        Assert.False((await service.GrantOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка")).Succeeded);
        Assert.False((await service.RevokeOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка")).Succeeded);
        Assert.True((await service.GrantOrganizationDemoAsync(admin.Id, organization.Id, admin.Id, "Проверка")).NotFound);
        Assert.True(paidSubscription.EndDate > DateTime.UtcNow);
        Assert.Equal(1, await context.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task UserOverviewShowsTheSamePlanAsWorkspaceWhenSubscriptionsOverlap()
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка работает с тарифами организации, поэтому коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var (organization, admin) = await CreateOrganizationAsync(context, OrganizationTypeCodes.Fleet);
        var demoPlan = await context.SubscriptionPlans.FirstAsync(item => item.IsDemoPlan
            && item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);
        var paidPlan = new SubscriptionPlan
        {
            Name = "Платный тариф с пересечением",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsActive = true,
            DurationDays = 30
        };
        context.SubscriptionPlans.Add(paidPlan);
        context.OrganizationSubscriptions.AddRange(
            new OrganizationSubscription
            {
                OrganizationId = organization.Id,
                SubscriptionPlan = paidPlan,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(10)
            },
            new OrganizationSubscription
            {
                OrganizationId = organization.Id,
                SubscriptionPlanId = demoPlan.Id,
                StartDate = DateTime.UtcNow.AddDays(-1),
                EndDate = DateTime.UtcNow.AddDays(20)
            });
        await context.SaveChangesAsync();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformPolicy:Commercial:Enabled"] = "true"
        }).Build();
        var controller = new UsersApiController(
            new AdminUsersService(context, configuration),
            Mock.Of<IAdminSubscriptionCommandService>(),
            Mock.Of<IAuthTokenService>(),
            Mock.Of<IFeatureAdminService>());

        var response = Assert.IsType<OkObjectResult>(await controller.GetOverview(organization.OwnerUserId));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(
            response.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        var organizationRow = Assert.Single(json.RootElement.GetProperty("organizations").EnumerateArray());

        Assert.Equal(demoPlan.Name, organizationRow.GetProperty("currentPlanName").GetString());
        Assert.True(organizationRow.GetProperty("currentPlanIsDemo").GetBoolean());

        var service = AdminBillingTestServices.Create<IAdminSubscriptionCommandService>(context);
        var revoked = await service.RevokeOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка");

        Assert.True(revoked.Succeeded);
        Assert.Contains("Другой действующий тариф сохранён", revoked.Message);
        Assert.True(await context.OrganizationSubscriptions.AnyAsync(item => item.OrganizationId == organization.Id
            && item.SubscriptionPlanId == paidPlan.Id && item.EndDate > DateTime.UtcNow));
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task AdminCannotGrantDemoToFreeOrganizationScenarios(string scenario)
    {
        await using var context = fixture.CreateContext();
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        // Проверка работает с тарифами организации, поэтому коммерческий режим включается явно.
        await FeatureBootstrapper.EnsureCommercialPlansAsync(context);
        var (organization, admin) = await CreateOrganizationAsync(context, scenario);
        var service = AdminBillingTestServices.Create<IAdminSubscriptionCommandService>(context);

        var result = await service.GrantOrganizationDemoAsync(organization.OwnerUserId, organization.Id, admin.Id, "Проверка");

        Assert.False(result.Succeeded);
        Assert.False(await context.OrganizationSubscriptions.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationAuditEvents.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await context.AdminSubscriptionAudits.AnyAsync(item =>
            item.Scope == "organizations" && item.ClientId == organization.Id.ToString()));
    }

    private static async Task<(Organization Organization, ApplicationUser Admin)> CreateOrganizationAsync(
        OpenCRMContext context,
        string scenario = OrganizationTypeCodes.ServiceStation)
    {
        var owner = new ApplicationUser();
        var admin = new ApplicationUser();
        var organization = new Organization
        {
            Name = "Организация для проверки демо",
            OwnerUser = owner,
            PrimaryScenarioCode = scenario,
            Members = [new OrganizationMember { User = owner, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.ApplicationUsers.Add(admin);
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return (organization, admin);
    }
}
