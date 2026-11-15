using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Billing.Features;
using OpenCRM.Modules.Billing;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Payments;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class OrganizationDemoTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveDemoIsHiddenAndCannotBeActivatedAgain(bool testAccount)
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(
            context, testAccount, OrganizationTypeCodes.Fleet);
        var service = OrganizationDemoTests.CreateService(context);
        var demo = (await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id)).First(plan => plan.IsDemoPlan);

        Assert.True((await service.ActivateDemoAsync(organization.OwnerUserId, organization.Id, demo.Id)).Success);
        Assert.False((await service.CheckDemoAvailabilityAsync(organization.OwnerUserId, organization.Id)).Available);
        Assert.DoesNotContain(await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id), plan => plan.IsDemoPlan);
        Assert.False((await service.ActivateDemoAsync(organization.OwnerUserId, organization.Id, demo.Id)).Success);
        Assert.Equal(1, await context.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ActiveAndPastSubscriptionsHideOrdinaryDemo(bool paid, bool expired)
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(
            context, scenario: OrganizationTypeCodes.Fleet);
        var service = OrganizationDemoTests.CreateService(context);
        var plans = await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id);
        var plan = plans.First(item => paid ? !item.IsDemoPlan : item.IsDemoPlan);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id, SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-20),
            EndDate = DateTime.UtcNow.AddDays(expired ? -1 : 10)
        });
        await context.SaveChangesAsync();

        Assert.False((await service.CheckDemoAvailabilityAsync(organization.OwnerUserId, organization.Id)).Available);
        Assert.DoesNotContain(await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id), item => item.IsDemoPlan);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("archived")]
    [InlineData("deleted")]
    public async Task SecondOrganizationOfSameTypeDoesNotReceiveDemo(string firstState)
    {
        await using var context = fixture.CreateContext();
        var first = await OrganizationDemoTests.CreateOrganizationAsync(
            context, scenario: OrganizationTypeCodes.Fleet);
        first.CreatedAt = DateTime.UtcNow.AddDays(-1);
        var second = new Organization
        {
            Name = "Вторая организация", OwnerUserId = first.OwnerUserId,
            PrimaryScenarioCode = first.PrimaryScenarioCode,
            Members = [new OrganizationMember { UserId = first.OwnerUserId, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(second);
        await context.SaveChangesAsync();
        var service = OrganizationDemoTests.CreateService(context);
        Assert.True((await service.CheckDemoAvailabilityAsync(first.OwnerUserId, first.Id)).Available);
        if (firstState == "archived")
        {
            first.Archive(DateTime.UtcNow);
        }

        if (firstState == "deleted")
        {
            first.Delete(DateTime.UtcNow);
        }
        await context.SaveChangesAsync();

        Assert.False((await service.CheckDemoAvailabilityAsync(first.OwnerUserId, second.Id)).Available);
        Assert.DoesNotContain(await service.GetAvailablePlansAsync(first.OwnerUserId, second.Id), plan => plan.IsDemoPlan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTestActivationCannotReplaceActiveRegularPlan(bool testAccount)
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(
            context, testAccount, OrganizationTypeCodes.Fleet);
        var audit = new Mock<IOrganizationAuditService>();
        var service = OrganizationDemoTests.CreateService(context, audit);
        var plans = await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id);
        var demo = plans.First(plan => plan.IsDemoPlan);
        var paid = plans.First(plan => !plan.IsDemoPlan);
        var existing = new OrganizationSubscription
        {
            OrganizationId = organization.Id, SubscriptionPlanId = paid.Id,
            StartDate = DateTime.UtcNow.AddDays(-1), EndDate = DateTime.UtcNow.AddDays(20)
        };
        context.OrganizationSubscriptions.Add(existing);
        await context.SaveChangesAsync();

        Assert.DoesNotContain(await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id), plan => plan.IsDemoPlan);
        var testPlans = await service.GetAvailablePlansAsync(
            organization.OwnerUserId, organization.Id, includeUnavailableDemoForTesting: true);
        Assert.Equal(testAccount, testPlans.Any(plan => plan.IsDemoPlan));
        Assert.False((await service.ActivateDemoAsync(organization.OwnerUserId, organization.Id, demo.Id)).Success);
        var result = await service.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, demo.Id);
        Assert.False(result.Success);
        Assert.Equal(1, await context.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.True(existing.EndDate > DateTime.UtcNow);
        Assert.Equal(paid.Id, (await service.GetCurrentAsync(organization.OwnerUserId, organization.Id))!.SubscriptionPlanId);
        audit.Verify(item => item.AddAsync(
            It.Is<OrganizationAuditEventCreateDto>(entry => entry.EventType == "billing.subscription.activated_test"),
            false), Times.Never);
    }

    [Fact]
    public async Task ActiveAnnualPlanCannotBeReplacedByMonthlyPlan()
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(context, testAccount: true);
        var annual = new SubscriptionPlan
        {
            Name = "Годовой тариф для проверки",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            DurationDays = 365,
            IsActive = true,
            Order = 1
        };
        var monthly = new SubscriptionPlan
        {
            Name = "Месячный тариф для проверки",
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            DurationDays = 30,
            IsActive = true,
            Order = 2
        };
        context.SubscriptionPlans.AddRange(annual, monthly);
        await context.SaveChangesAsync();

        var endDate = DateTime.UtcNow.AddDays(365);
        // PostgreSQL сохраняет дату с точностью до микросекунд.
        endDate = endDate.AddTicks(-(endDate.Ticks % 10));
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = annual.Id,
            StartDate = DateTime.UtcNow.AddDays(-1),
            EndDate = endDate,
            SourceCode = "manual"
        });
        await context.SaveChangesAsync();

        var service = OrganizationDemoTests.CreateService(context);
        Assert.False((await service.CanPurchasePlanAsync(organization.OwnerUserId, organization.Id, monthly.Id)).Success);
        Assert.False((await service.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, monthly.Id)).Success);
        Assert.Equal(annual.Id, (await service.GetCurrentAsync(organization.OwnerUserId, organization.Id))!.SubscriptionPlanId);
        Assert.Equal(endDate, await context.OrganizationSubscriptions
            .Where(item => item.OrganizationId == organization.Id).Select(item => item.EndDate).SingleAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTestActivationRequiresAccountFlagWithoutActivePlan(bool testAccount)
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(context, testAccount);
        var service = OrganizationDemoTests.CreateService(context);
        var paid = (await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id))
            .First(plan => !plan.IsDemoPlan && !plan.IsDefaultPlan);

        var result = await service.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, paid.Id);

        Assert.Equal(testAccount, result.Success);
        Assert.Equal(testAccount ? 1 : 0, await context.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task TestFlagDoesNotBypassOrganizationBillingPermission()
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(context, testAccount: true);
        var service = OrganizationDemoTests.CreateService(context, canManage: false);
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        var demoId = await context.SubscriptionPlans.Where(plan => plan.IsDemoPlan
            && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business).Select(plan => plan.Id).FirstAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateForTestingAsync(organization.OwnerUserId, organization.Id, demoId));
        Assert.False(await context.OrganizationSubscriptions.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreeOwnerCanWorkWithoutActiveSubscription(bool hadSubscription)
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(context);
        var service = OrganizationDemoTests.CreateService(context, canManage: false);
        if (hadSubscription)
        {
            var plan = await context.SubscriptionPlans.FirstAsync(item =>
                item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business && !item.IsDemoPlan && !item.IsDefaultPlan);
            context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                OrganizationId = organization.Id,
                SubscriptionPlanId = plan.Id,
                StartDate = DateTime.UtcNow.AddDays(-31),
                EndDate = DateTime.UtcNow.AddDays(-1)
            });
            await context.SaveChangesAsync();
        }

        var access = await service.GetWorkspaceAccessAsync(organization.OwnerUserId, organization.Id);

        Assert.True(access.IsFreePlan);
        Assert.False(access.IsReadOnly);
        Assert.False(access.HasActiveSubscription);
        Assert.Null(access.ReadOnlyMessage);
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing)]
    [InlineData(OrganizationTypeCodes.ServiceStation)]
    public async Task FreeOrganizationHasNoDemoAndOwnerCanWork(string scenario)
    {
        await using var context = fixture.CreateContext();
        var organization = await OrganizationDemoTests.CreateOrganizationAsync(context, scenario: scenario);
        var service = OrganizationDemoTests.CreateService(context);
        await FeatureBootstrapper.EnsureDefaultsAsync(context);
        var demoId = await context.SubscriptionPlans.Where(plan => plan.IsDemoPlan
            && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business).Select(plan => plan.Id).FirstAsync();

        var availability = await service.CheckDemoAvailabilityAsync(organization.OwnerUserId, organization.Id);
        var plans = await service.GetAvailablePlansAsync(organization.OwnerUserId, organization.Id);
        var access = await service.GetWorkspaceAccessAsync(organization.OwnerUserId, organization.Id);
        var activation = await service.ActivateDemoAsync(organization.OwnerUserId, organization.Id, demoId);

        Assert.False(availability.Available);
        Assert.DoesNotContain(plans, plan => plan.IsDemoPlan);
        Assert.True(access.IsFreePlan);
        Assert.False(access.IsReadOnly);
        Assert.False(access.HasActiveSubscription);
        Assert.Null(access.ReadOnlyMessage);
        Assert.False(activation.Success);
        Assert.False(await context.OrganizationSubscriptions.AnyAsync(item => item.OrganizationId == organization.Id));
    }

    private static async Task<Organization> CreateOrganizationAsync(
        OpenCRMContext context,
        bool testAccount = false,
        string scenario = OrganizationTypeCodes.ServiceStation)
    {
        if (!await context.SubscriptionPlans.AnyAsync(plan =>
            plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business && !plan.IsDemoPlan && !plan.IsDefaultPlan))
        {
            context.SubscriptionPlans.Add(new SubscriptionPlan
            {
                Name = "Платный бизнес-тариф для проверки",
                PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
                Price = 499,
                DurationDays = 30
            });
        }

        var user = new ApplicationUser { CanUseTestSubscriptions = testAccount };
        var organization = new Organization
        {
            Name = "Проверка демо", OwnerUser = user,
            PrimaryScenarioCode = scenario,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    private static OrganizationSubscriptionService CreateService(
        OpenCRMContext context,
        Mock<IOrganizationAuditService>? audit = null,
        bool canManage = true)
    {
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(canManage);
        var promotions = new Mock<IPromoCodeService>();
        promotions.Setup(service => service.CalculatePriceAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>()))
            .ReturnsAsync((string userId, Guid organizationId, int planId, decimal price) =>
                new PromotionPriceDto { OriginalPrice = price, FinalPrice = price });

        return new OrganizationSubscriptionService(
            OrganizationBillingTestServices.CreateCatalog(context, promotions.Object),
            OrganizationBillingTestServices.CreateEntitlement(context),
            OrganizationBillingTestServices.CreateAccess(context, permissions.Object),
            Mock.Of<IOrganizationSubscriptionCheckoutService>(),
            OrganizationBillingTestServices.CreateExtensionQuery(context, permissions.Object),
            OrganizationBillingTestServices.CreateCommands(context, permissions.Object, audit?.Object),
            Mock.Of<IOrganizationSubscriptionActivationService>(),
            OrganizationBillingTestServices.CreatePurchasePolicy(context, permissions.Object));
    }
}
