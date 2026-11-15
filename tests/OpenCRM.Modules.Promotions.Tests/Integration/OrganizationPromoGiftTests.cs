using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Modules.Promotions.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[Collection(PromotionsDatabaseCollection.Name)]
public sealed class OrganizationPromoGiftTests(PromotionsDatabaseFixture fixture)
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(100);

    private static async Task<OrganizationPromoApplicationDto> ApplyAsync(
        IServiceProvider provider, string actorUserId, Guid organizationId, string code)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
            .ApplyAsync(actorUserId, organizationId, code);
    }

    [Fact]
    public async Task ApplyWithoutActiveSubscriptionGrantsImmediatePromoAccess()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var template = await fixture.CreatePlanAsync(context, "Бизнес подарок");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);
        var before = DateTime.UtcNow.AddSeconds(-1);

        var dto = await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        var after = DateTime.UtcNow.AddSeconds(1);
        await using var check = fixture.CreateContext();
        var application = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Consumed, application.Status);
        Assert.Null(application.SubscriptionPaymentId);
        Assert.True((application.AccessUntil!.Value - dto.AccessUntil!.Value).Duration() <= Tolerance);

        var subscription = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.Equal(template.Id, subscription.SubscriptionPlanId);
        Assert.Equal("promo", subscription.SourceCode);
        Assert.InRange(subscription.StartDate, before, after);
        Assert.InRange(subscription.EndDate - subscription.StartDate, TimeSpan.FromDays(30) - Tolerance, TimeSpan.FromDays(30) + Tolerance);
        Assert.InRange((subscription.PromoDaysStartDate!.Value - subscription.StartDate).Duration(), TimeSpan.Zero, Tolerance);

        Assert.Equal(1, await check.PromoCodeApplications.CountAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id));
        Assert.False(await check.SubscriptionPayments.AnyAsync(payment => payment.PromoCodeId == promo.Id));
    }

    [Fact]
    public async Task ApplyWithExpiredDemoStartsGiftFromNowInsteadOfDemoEnd()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо истекшее", isDemo: true);
        var template = await fixture.CreatePlanAsync(context, "Подарок после демо");
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-35),
            EndDate = DateTime.UtcNow.AddDays(-5),
            SourceCode = "demo"
        });
        await context.SaveChangesAsync();
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        var after = DateTime.UtcNow.AddSeconds(1);
        await using var check = fixture.CreateContext();
        var oldDemo = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SubscriptionPlanId == demoPlan.Id);
        Assert.InRange(oldDemo.EndDate, DateTime.UtcNow.AddDays(-5).AddSeconds(-1), DateTime.UtcNow.AddDays(-5).AddSeconds(1));

        var gift = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "promo");
        Assert.Equal(template.Id, gift.SubscriptionPlanId);
        Assert.InRange(gift.StartDate, before, after);
        Assert.InRange(gift.PromoDaysStartDate!.Value, before, after);
        // Критическая регрессия: отсчёт от текущего момента, а не от истекшего конца демо.
        Assert.InRange(gift.EndDate, before.AddDays(30), after.AddDays(30));
        Assert.True(gift.EndDate > DateTime.UtcNow.AddDays(29));
    }

    [Fact]
    public async Task ApplyWithActiveDemoClosesDemoAndStartsGiftFromDemoEnd()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо активное", isDemo: true);
        var template = await fixture.CreatePlanAsync(context, "Подарок поверх демо");
        var demoEnd = DateTime.UtcNow.AddDays(5);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-25),
            EndDate = demoEnd,
            SourceCode = "demo"
        });
        await context.SaveChangesAsync();
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        var after = DateTime.UtcNow.AddSeconds(1);
        await using var check = fixture.CreateContext();
        var oldDemo = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SubscriptionPlanId == demoPlan.Id);
        Assert.InRange(oldDemo.EndDate, before, after);

        var gift = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "promo");
        Assert.Equal(template.Id, gift.SubscriptionPlanId);
        Assert.InRange(gift.StartDate, before, after);
        Assert.InRange(gift.EndDate, demoEnd.AddDays(30) - Tolerance, demoEnd.AddDays(30) + Tolerance);
        Assert.InRange(gift.PromoDaysStartDate!.Value, demoEnd - Tolerance, demoEnd + Tolerance);
        Assert.False(await check.SubscriptionPayments.AnyAsync(payment => payment.PromoCodeId == promo.Id));
    }

    [Fact]
    public async Task ApplyWithActivePaidSubscriptionExtendsItWithoutChangingPlanOrSource()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var paidPlan = await fixture.CreatePlanAsync(context, "Оплаченный бизнес");
        var template = await fixture.CreatePlanAsync(context, "Другой подарочный тариф");
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = paidPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-20),
            EndDate = DateTime.UtcNow.AddDays(10),
            SourceCode = "manual"
        });
        await context.SaveChangesAsync();
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        // Перечитываем засеянную подписку, чтобы сравнивать с фактическим значением в PostgreSQL.
        await using (var seedCheck = fixture.CreateContext())
        {
            var seeded = await seedCheck.OrganizationSubscriptions.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id);

            await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

            await using var check = fixture.CreateContext();
            var extended = await check.OrganizationSubscriptions.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id);
            Assert.Equal(seeded.Id, extended.Id);
            Assert.Equal(paidPlan.Id, extended.SubscriptionPlanId);
            Assert.Equal("manual", extended.SourceCode);
            Assert.InRange(extended.EndDate, seeded.EndDate.AddDays(30) - Tolerance, seeded.EndDate.AddDays(30) + Tolerance);
        }

        await using var final = fixture.CreateContext();
        Assert.Equal(1, await final.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await final.SubscriptionPayments.AnyAsync(payment => payment.PromoCodeId == promo.Id));
    }

    [Fact]
    public async Task ConsecutiveGiftCodesExtendSamePromoSubscriptionAndDuplicatesAreRejected()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var template = await fixture.CreatePlanAsync(context, "Продлеваемый подарок");
        var firstPromo = await fixture.CreateGiftCodeAsync(context, template.Id, days: 30);
        var secondPromo = await fixture.CreateGiftCodeAsync(context, template.Id, days: 7);
        var colleague = new ApplicationUser { Email = $"promotions-{Guid.NewGuid():N}@example.test" };
        context.ApplicationUsers.Add(colleague);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = colleague.Id,
            RoleCode = OrganizationRoleCodes.Owner
        });
        await context.SaveChangesAsync();
        var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, firstPromo.Code);

        await using var afterFirst = fixture.CreateContext();
        var first = await afterFirst.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id);
        var firstStart = first.StartDate;
        var firstPromoDaysStart = first.PromoDaysStartDate!.Value;
        var firstEnd = first.EndDate;

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, secondPromo.Code);

        await using var afterSecond = fixture.CreateContext();
        var second = await afterSecond.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.Equal(first.Id, second.Id);
        Assert.InRange((second.StartDate - firstStart).Duration(), TimeSpan.Zero, Tolerance);
        Assert.InRange((second.PromoDaysStartDate!.Value - firstPromoDaysStart).Duration(), TimeSpan.Zero, Tolerance);
        Assert.InRange(second.EndDate, firstEnd.AddDays(7) - Tolerance, firstEnd.AddDays(7) + Tolerance);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, organization.OwnerUserId, organization.Id, firstPromo.Code));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, colleague.Id, organization.Id, firstPromo.Code));

        await using var final = fixture.CreateContext();
        var unchanged = await final.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id);
        Assert.InRange(unchanged.EndDate, firstEnd.AddDays(7) - Tolerance, firstEnd.AddDays(7) + Tolerance);
        Assert.Equal(2, await final.PromoCodeApplications.CountAsync(item => item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task SameGiftCodeAppliesToDifferentOrganizationsOfSameOwner()
    {
        await using var context = fixture.CreateContext();
        var first = await fixture.CreateOrganizationAsync(context);
        var second = await fixture.CreateOrganizationAsync(context, first.OwnerUserId);
        var template = await fixture.CreatePlanAsync(context, "Общий подарок");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, first.OwnerUserId, first.Id, promo.Code);
        await ApplyAsync(provider, second.OwnerUserId, second.Id, promo.Code);

        await using var check = fixture.CreateContext();
        var applications = await check.PromoCodeApplications.AsNoTracking()
            .Where(item => item.PromoCodeId == promo.Id)
            .ToListAsync();
        Assert.Equal(2, applications.Count);
        Assert.Contains(applications, item => item.OrganizationId == first.Id);
        Assert.Contains(applications, item => item.OrganizationId == second.Id);
        Assert.All(applications, item => Assert.Equal(PromoCodeApplicationStatuses.Consumed, item.Status));
        Assert.Equal(2, await check.OrganizationSubscriptions
            .CountAsync(item => item.SourceCode == "promo" && (item.OrganizationId == first.Id || item.OrganizationId == second.Id)));
    }

    [Fact]
    public async Task GlobalApplicationCapAllowsOnlyOneConcurrentWinner()
    {
        await using (var seed = fixture.CreateContext())
        {
            var template = await fixture.CreatePlanAsync(seed, "Подарок с лимитом");
            var promo = await fixture.CreateGiftCodeAsync(seed, template.Id, cap: 1);
            var first = await fixture.CreateOrganizationAsync(seed);
            var second = await fixture.CreateOrganizationAsync(seed);
            var promoCode = promo.Code;
            var firstId = first.Id;
            var secondId = second.Id;
            var firstOwner = first.OwnerUserId;
            var secondOwner = second.OwnerUserId;
            var promoId = promo.Id;

            await using var firstContext = fixture.CreateContext();
            await using var secondContext = fixture.CreateContext();
            var firstProvider = fixture.CreateServices(firstContext);
            var secondProvider = fixture.CreateServices(secondContext);

            // Локальная функция, чтобы ловить только ожидаемую ошибку лимита.
            async Task<bool> TryApply(IServiceProvider provider, string actor, Guid organizationId)
            {
                try
                {
                    await ApplyAsync(provider, actor, organizationId, promoCode);
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            var results = await Task.WhenAll(
                TryApply(firstProvider, firstOwner, firstId),
                TryApply(secondProvider, secondOwner, secondId));

            Assert.Single(results, success => success);
            Assert.Single(results, success => !success);

            await using var check = fixture.CreateContext();
            Assert.Equal(1, await check.PromoCodeApplications.CountAsync(item => item.PromoCodeId == promoId));
            Assert.Equal(1, await check.OrganizationSubscriptions.CountAsync(item =>
                item.SourceCode == "promo" && (item.OrganizationId == firstId || item.OrganizationId == secondId)));
        }
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("expired")]
    [InlineData("notstarted")]
    [InlineData("scenario")]
    public async Task ApplyRejectsInvalidGiftCodeWithoutSideEffects(string caseKind)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context, scenario: caseKind == "scenario" ? "service-station" : "detailing");
        var template = await fixture.CreatePlanAsync(context, "Недоступный подарок");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);

        switch (caseKind)
        {
            case "inactive":
                promo.IsActive = false;
                break;
            case "expired":
                promo.ValidUntil = DateTime.UtcNow.AddDays(-1);
                break;
            case "notstarted":
                promo.ValidFrom = DateTime.UtcNow.AddDays(1);
                break;
            default:
                promo.AppliesToAllScenarios = false;
                context.PromoCodeScenarios.Add(new PromoCodeScenario { PromoCodeId = promo.Id, ScenarioCode = "detailing" });
                break;
        }

        await context.SaveChangesAsync();
        var provider = fixture.CreateServices(context);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code));

        await using var check = fixture.CreateContext();
        var persisted = await check.PromoCodes.AsNoTracking().SingleAsync(item => item.Id == promo.Id);
        if (caseKind == "scenario")
        {
            Assert.False(persisted.AppliesToAllScenarios);
        }

        Assert.False(await check.PromoCodeApplications.AnyAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id));
        Assert.False(await check.OrganizationSubscriptions.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await check.SubscriptionPayments.AnyAsync(payment => payment.PromoCodeId == promo.Id));
    }

    [Fact]
    public async Task AuditFailureAfterGrantRollsBackEverythingAndAllowsRetry()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var template = await fixture.CreatePlanAsync(context, "Подарок с сбоем аудита");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id, cap: 1);
        var organizationId = organization.Id;
        var ownerUserId = organization.OwnerUserId;
        var promoId = promo.Id;
        var promoCode = promo.Code;
        var auditReached = false;
        var provider = fixture.CreateServices(context, auditOverride: (dto, saveChanges) =>
        {
            auditReached = true;

            return Task.FromException(new InvalidOperationException("Аудит недоступен."));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, ownerUserId, organizationId, promoCode));

        // Аудит вызывается после выдачи подарка, значит порт выдачи уже был выполнен.
        Assert.True(auditReached);
        await using (var check = fixture.CreateContext())
        {
            Assert.False(await check.PromoCodeApplications.AnyAsync(item => item.PromoCodeId == promoId && item.OrganizationId == organizationId));
            Assert.False(await check.OrganizationSubscriptions.AnyAsync(item => item.OrganizationId == organizationId));
            Assert.False(await check.SubscriptionPayments.AnyAsync(payment => payment.PromoCodeId == promoId));
        }

        var retryProvider = fixture.CreateServices(context);
        await ApplyAsync(retryProvider, ownerUserId, organizationId, promoCode);

        await using var final = fixture.CreateContext();
        Assert.Equal(1, await final.PromoCodeApplications.CountAsync(item => item.PromoCodeId == promoId && item.OrganizationId == organizationId));
        Assert.Equal(1, await final.OrganizationSubscriptions.CountAsync(item => item.OrganizationId == organizationId && item.SourceCode == "promo"));
    }

    [Fact]
    public async Task ApplyWithoutBillingManagePermissionIsRejected()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var template = await fixture.CreatePlanAsync(context, "Подарок без прав");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        using (var scope = provider.CreateScope())
        {
            Mock.Get(scope.ServiceProvider.GetRequiredService<IOrganizationPermissionService>())
                .Setup(service => service.HasPermissionAsync(organization.OwnerUserId, organization.Id, OrganizationPermissionCodes.BillingManage))
                .ReturnsAsync(false);

            await Assert.ThrowsAsync<UnauthorizedAccessException>(
                () => scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
                    .ApplyAsync(organization.OwnerUserId, organization.Id, promo.Code));
        }

        await using var check = fixture.CreateContext();
        Assert.False(await check.PromoCodeApplications.AnyAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id));
        Assert.False(await check.OrganizationSubscriptions.AnyAsync(item => item.OrganizationId == organization.Id));
    }

}
