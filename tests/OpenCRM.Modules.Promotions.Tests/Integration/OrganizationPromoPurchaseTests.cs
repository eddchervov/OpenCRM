using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[Collection(PromotionsDatabaseCollection.Name)]
public sealed class OrganizationPromoPurchaseTests(PromotionsDatabaseFixture fixture)
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan ExactTolerance = TimeSpan.FromMilliseconds(1);

    private static async Task<SubscriptionResult> ActivateNewAsync(
        IServiceProvider provider, string userId, Guid organizationId, int planId, string paymentId)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateNewSubscriptionAsync(userId, organizationId, planId, paymentId);
    }

    private static async Task<SubscriptionResult> ActivateExtendedAsync(
        IServiceProvider provider, string userId, Guid organizationId, int planId, string paymentId)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>()
            .ActivateExtendedSubscriptionAsync(userId, organizationId, planId, paymentId);
    }

    private static async Task ApplyGiftAsync(
        IServiceProvider provider, string userId, Guid organizationId, string code)
    {
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
            .ApplyAsync(userId, organizationId, code);
    }

    private static string PurchaseMethod(Guid organizationId) => $"yookassa_workspace_purchase:{organizationId:D}";

    private static string ExtensionMethod(Guid organizationId) => $"yookassa_workspace_extension:{organizationId:D}";

    // Общий посев платежа: эмуляция подтверждения провайдера через PaidAt без реальной сети.
    private static async Task<SubscriptionPayment> SeedPaidPaymentAsync(
        OpenCRMContext context, string userId, SubscriptionPlan plan, Guid organizationId, string method)
    {
        var payment = new SubscriptionPayment
        {
            UserId = userId,
            SubscriptionPlanId = plan.Id,
            Amount = plan.EffectivePrice,
            CreatedAt = DateTime.UtcNow,
            PaidAt = DateTime.UtcNow,
            StatusUpdatedAt = DateTime.UtcNow,
            PaymentStatus = "pending",
            PaymentMethod = method,
            ExternalPaymentId = Guid.NewGuid().ToString()
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        return payment;
    }

    [Fact]
    public async Task PurchaseOverActiveDemoDoesNotCarryDemoDays()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо под покупку", isDemo: true);
        var plan = await fixture.CreatePlanAsync(context, "Бизнес покупка поверх демо");
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-25),
            EndDate = DateTime.UtcNow.AddDays(5),
            SourceCode = "demo"
        });
        await context.SaveChangesAsync();
        var payment = await SeedPaidPaymentAsync(
            context, organization.OwnerUserId, plan, organization.Id, PurchaseMethod(organization.Id));
        var provider = fixture.CreateServices(context);
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await ActivateNewAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        var after = DateTime.UtcNow.AddSeconds(1);
        await using var check = fixture.CreateContext();
        var paid = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
        Assert.Equal(plan.Id, paid.SubscriptionPlanId);
        Assert.InRange(paid.StartDate, before, after);
        // Критично: чистый демо-остаток не переносится, длительность строго 30 дней.
        Assert.InRange((paid.EndDate - paid.StartDate).Duration(),
            TimeSpan.FromDays(30) - ExactTolerance, TimeSpan.FromDays(30) + ExactTolerance);
        Assert.Null(paid.PromoDaysStartDate);

        var oldDemo = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SubscriptionPlanId == demoPlan.Id);
        Assert.InRange(oldDemo.EndDate, before, after);
    }

    [Fact]
    public async Task PurchaseAfterGiftCarriesGiftRemainderAndClosesGiftRow()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var template = await fixture.CreatePlanAsync(context, "Шаблон подарка перед покупкой");
        var plan = await fixture.CreatePlanAsync(context, "Бизнес после подарка");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        await ApplyGiftAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        // Фиксируем состояние подарочной строки до покупки из чистого контекста.
        await using (var seedCheck = fixture.CreateContext())
        {
            var gift = await seedCheck.OrganizationSubscriptions.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id);
            Assert.Equal("promo", gift.SourceCode);
            var giftPreviousEnd = gift.EndDate;
            var giftStart = gift.StartDate;
            var giftPromoDaysStart = gift.PromoDaysStartDate;

            var payment = await SeedPaidPaymentAsync(
                context, organization.OwnerUserId, plan, organization.Id, PurchaseMethod(organization.Id));
            var result = await ActivateNewAsync(
                provider, organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

            Assert.True(result.Success);
            await using var check = fixture.CreateContext();
            var paid = await check.OrganizationSubscriptions.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
            Assert.Equal(plan.Id, paid.SubscriptionPlanId);
            // Подарочный остаток приплюсован к оплаченному периоду.
            Assert.InRange(paid.EndDate, giftPreviousEnd.AddDays(30) - Tolerance, giftPreviousEnd.AddDays(30) + Tolerance);

            var closedGift = await check.OrganizationSubscriptions.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "promo");
            Assert.True(closedGift.EndDate <= DateTime.UtcNow);
            // Начало и старт подарочных дней исходной строки не переписываются.
            Assert.InRange((closedGift.StartDate - giftStart).Duration(), TimeSpan.Zero, Tolerance);
            Assert.InRange((closedGift.PromoDaysStartDate!.Value - giftPromoDaysStart!.Value).Duration(), TimeSpan.Zero, Tolerance);
        }
    }

    [Fact]
    public async Task PurchaseAfterDemoPlusGiftCountsOnlyGiftDays()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var demoPlan = await fixture.CreatePlanAsync(context, "Демо плюс подарок", isDemo: true);
        var template = await fixture.CreatePlanAsync(context, "Шаблон после демо");
        var plan = await fixture.CreatePlanAsync(context, "Бизнес после демо и подарка");
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = DateTime.UtcNow.AddDays(-25),
            EndDate = DateTime.UtcNow.AddDays(5),
            SourceCode = "demo"
        });
        await context.SaveChangesAsync();
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        await ApplyGiftAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        var payment = await SeedPaidPaymentAsync(
            context, organization.OwnerUserId, plan, organization.Id, PurchaseMethod(organization.Id));
        var result = await ActivateNewAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        await using var check = fixture.CreateContext();
        var paid = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
        // Остаток демо не учитывается: 30 оплаченных + 30 подарочных, а не 65.
        Assert.InRange((paid.EndDate - paid.StartDate).Duration(),
            TimeSpan.FromDays(60) - Tolerance, TimeSpan.FromDays(60) + Tolerance);
        Assert.Null(paid.PromoDaysStartDate);

        var oldDemo = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SubscriptionPlanId == demoPlan.Id);
        Assert.True(oldDemo.EndDate <= DateTime.UtcNow);

        var closedGift = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "promo");
        Assert.True(closedGift.EndDate <= DateTime.UtcNow);
        // Старт подарочных дней сохранён на прежней границе демо.
        Assert.NotNull(closedGift.PromoDaysStartDate);
    }

    [Fact]
    public async Task PurchaseWithElapsedGiftCountsRemainderFromNow()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var template = await fixture.CreatePlanAsync(context, "Шаблон истекающего подарка");
        var plan = await fixture.CreatePlanAsync(context, "Бизнес поверх истекающего подарка");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        await ApplyGiftAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        // Симулируем прошедшее время: подарок уже частично израсходован.
        var gift = await context.OrganizationSubscriptions
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "promo");
        var elapsedStart = DateTime.UtcNow.AddDays(-10);
        var elapsedPromoStart = DateTime.UtcNow.AddDays(-5);
        var oldGiftEnd = DateTime.UtcNow.AddDays(25);
        gift.StartDate = elapsedStart;
        gift.PromoDaysStartDate = elapsedPromoStart;
        gift.EndDate = oldGiftEnd;
        await context.SaveChangesAsync();

        var payment = await SeedPaidPaymentAsync(
            context, organization.OwnerUserId, plan, organization.Id, PurchaseMethod(organization.Id));
        var result = await ActivateNewAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        await using var check = fixture.CreateContext();
        var paid = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
        // Остаток считается от текущего момента: 30 + 25 = 55 дней, а не 56.
        Assert.InRange(paid.EndDate, oldGiftEnd.AddDays(30) - Tolerance, oldGiftEnd.AddDays(30) + Tolerance);
        Assert.InRange((paid.EndDate - paid.StartDate).Duration(),
            TimeSpan.FromDays(55) - TimeSpan.FromMinutes(5), TimeSpan.FromDays(55) + TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task PurchaseDifferentPlanWhileGiftActiveIsAllowed()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var template = await fixture.CreatePlanAsync(context, "Шаблон активного подарка");
        var plan = await fixture.CreatePlanAsync(context, "Другой оплаченный тариф");
        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        await ApplyGiftAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        var payment = await SeedPaidPaymentAsync(
            context, organization.OwnerUserId, plan, organization.Id, PurchaseMethod(organization.Id));
        var result = await ActivateNewAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(result.Success);
        await using var check = fixture.CreateContext();
        var paid = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
        Assert.Equal(plan.Id, paid.SubscriptionPlanId);
        // Тариф отличается от шаблона подарка, но покупка не блокируется.
        Assert.NotEqual(template.Id, paid.SubscriptionPlanId);
    }

    [Fact]
    public async Task DuplicateWebhookActivationIsIdempotent()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Бизнес идемпотентность");
        var payment = await SeedPaidPaymentAsync(
            context, organization.OwnerUserId, plan, organization.Id, PurchaseMethod(organization.Id));
        var provider = fixture.CreateServices(context);

        var first = await ActivateNewAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);
        var second = await ActivateNewAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, payment.ExternalPaymentId!);

        Assert.True(first.Success);
        Assert.True(second.Success);
        await using var check = fixture.CreateContext();
        var paid = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
        Assert.InRange((paid.EndDate - paid.StartDate).Duration(),
            TimeSpan.FromDays(30) - ExactTolerance, TimeSpan.FromDays(30) + ExactTolerance);
        var persisted = await check.SubscriptionPayments.AsNoTracking()
            .SingleAsync(item => item.Id == payment.Id);
        Assert.Equal("completed", persisted.PaymentStatus);
    }

    [Fact]
    public async Task PreparedExtensionAfterGiftExtendsByGiftAndPlanDays()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Бизнес подготовленное продление");
        var template = await fixture.CreatePlanAsync(context, "Шаблон подарка перед продлением");
        var method = ExtensionMethod(organization.Id);
        var oldEnd = DateTime.UtcNow.AddDays(3);
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-27),
            EndDate = oldEnd,
            SourceCode = "paid"
        });
        await context.SaveChangesAsync();

        // Подготовленный платеж еще не подтвержден провайдером и связан с операцией создания.
        var payment = new SubscriptionPayment
        {
            UserId = organization.OwnerUserId,
            SubscriptionPlanId = plan.Id,
            Amount = plan.EffectivePrice,
            CreatedAt = DateTime.UtcNow,
            PaidAt = null,
            StatusUpdatedAt = DateTime.UtcNow,
            PaymentStatus = "pending",
            PaymentMethod = method,
            ExternalPaymentId = Guid.NewGuid().ToString()
        };
        context.SubscriptionPayments.Add(payment);
        context.PaymentCreationOperations.Add(new PaymentCreationOperation
        {
            IdempotenceKey = Guid.NewGuid().ToString(),
            UserId = organization.OwnerUserId,
            SubscriptionPlanId = plan.Id,
            OperationType = method,
            Amount = plan.EffectivePrice,
            BuyerTypeCode = "individual",
            RequestTimestamp = DateTime.UtcNow,
            RequestBody = "{}",
            Status = PaymentCreationOperationStatuses.Created,
            CreatedAt = DateTime.UtcNow,
            SubscriptionPayment = payment,
            ExternalPaymentId = payment.ExternalPaymentId
        });
        await context.SaveChangesAsync();

        var promo = await fixture.CreateGiftCodeAsync(context, template.Id);
        var provider = fixture.CreateServices(context);

        // Подарок продлевает текущий оплаченный тариф до oldEnd + 30.
        await ApplyGiftAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        // Провайдер подтверждает оплату: эмуляция через PaidAt без реальной сети.
        var stored = await context.SubscriptionPayments.SingleAsync(item => item.Id == payment.Id);
        stored.PaidAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var result = await ActivateExtendedAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, stored.ExternalPaymentId!);

        Assert.True(result.Success);
        await using var check = fixture.CreateContext();
        var subscription = await check.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
        // Доверенное продление: старый конец + 30 подарочных + 30 оплаченных дней.
        Assert.InRange(subscription.EndDate, oldEnd.AddDays(60) - Tolerance, oldEnd.AddDays(60) + Tolerance);

        var repeat = await ActivateExtendedAsync(
            provider, organization.OwnerUserId, organization.Id, plan.Id, stored.ExternalPaymentId!);
        Assert.True(repeat.Success);
        await using var final = fixture.CreateContext();
        var unchanged = await final.OrganizationSubscriptions.AsNoTracking()
            .SingleAsync(item => item.OrganizationId == organization.Id && item.SourceCode == "paid");
        Assert.InRange((unchanged.EndDate - subscription.EndDate).Duration(), TimeSpan.Zero, Tolerance);
        var persisted = await final.SubscriptionPayments.AsNoTracking()
            .SingleAsync(item => item.Id == payment.Id);
        Assert.Equal("completed", persisted.PaymentStatus);
    }
}
