using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Modules.Promotions.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[Collection(PromotionsDatabaseCollection.Name)]
public sealed class OrganizationPromoDiscountTests(PromotionsDatabaseFixture fixture)
{
    /// <summary>
    /// Денежный организационный промокод с явным списком тарифов.
    /// </summary>
    private static async Task<PromoCode> CreateMoneyCodeAsync(
        OpenCRMContext context, string discountType, decimal value, int[] planIds, int? cap = null)
    {
        var promo = new PromoCode
        {
            Code = "PM" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = "Денежная скидка",
            IsOrganizationCode = true,
            DiscountTypeCode = discountType,
            DiscountValue = value,
            AppliesToAllScenarios = true,
            AppliesToAllPaidPlans = false,
            MaximumOrganizationApplications = cap,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.PromoCodes.Add(promo);
        await context.SaveChangesAsync();
        context.PromoCodePlans.AddRange(planIds.Select(id => new PromoCodePlan
        {
            PromoCodeId = promo.Id,
            SubscriptionPlanId = id
        }));
        await context.SaveChangesAsync();

        return promo;
    }

    private static async Task<OrganizationPromoApplicationDto> ApplyAsync(
        IServiceProvider provider, string actorUserId, Guid organizationId, string code)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
            .ApplyAsync(actorUserId, organizationId, code);
    }

    private static async Task<PromotionPriceDto> CalculateAsync(
        IServiceProvider provider, Guid organizationId, int planId, decimal originalPrice)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
            .CalculateDiscountAsync(organizationId, planId, originalPrice);
    }

    /// <summary>
    /// Операция покупки рабочего пространства по рассчитанной цене со скидкой.
    /// Пользователь должен быть действующим участником организации.
    /// </summary>
    private static PaymentCreationOperation CreateOperation(
        string userId, int planId, PromotionPriceDto quote, PromoCode promo, Guid organizationId)
    {
        return new PaymentCreationOperation
        {
            IdempotenceKey = "promo-" + Guid.NewGuid().ToString("N"),
            UserId = userId,
            SubscriptionPlanId = planId,
            OperationType = $"yookassa_workspace_purchase:{organizationId:D}",
            Amount = quote.FinalPrice,
            OriginalAmount = quote.OriginalPrice,
            PromoDiscountAmount = quote.DiscountAmount,
            PromoCodeId = promo.Id,
            PromoCodeSnapshot = promo.Code,
            PromoCodeApplicationId = quote.PromoCodeApplicationId,
            OrganizationId = organizationId,
            Description = "Покупка рабочего пространства",
            BuyerTypeCode = "individual",
            RequestTimestamp = DateTime.UtcNow,
            RequestBody = "{}",
            Status = PaymentCreationOperationStatuses.Prepared,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static SubscriptionPayment CreatePayment(PaymentCreationOperation operation, int planId)
    {
        return new SubscriptionPayment
        {
            UserId = operation.UserId,
            SubscriptionPlanId = planId,
            Amount = operation.Amount,
            PaymentMethod = operation.OperationType,
            PaymentStatus = "pending",
            ExternalPaymentId = Guid.NewGuid().ToString(),
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Захват заявки в транзакции вызывающей стороны с сохранением и коммитом.
    /// </summary>
    private async Task<PaymentCreationOperation> ClaimAsync(
        OpenCRMContext context, string userId, int planId, PromotionPriceDto quote,
        PromoCode promo, Guid organizationId, string? operationType = null)
    {
        var operation = CreateOperation(userId, planId, quote, promo, organizationId);

        if (operationType is not null)
        {
            operation.OperationType = operationType;
        }

        context.PaymentCreationOperations.Add(operation);
        await context.SaveChangesAsync();

        using var provider = fixture.CreateServices(context);
        using var scope = provider.CreateScope();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IPromotionPaymentCoordinator>().ClaimAsync(operation);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return operation;
    }

    /// <summary>
    /// Привязка реального платежа к захваченной заявке внутри транзакции.
    /// </summary>
    private async Task<SubscriptionPayment> AttachPaymentAsync(
        OpenCRMContext context, PaymentCreationOperation operation, int planId)
    {
        var payment = CreatePayment(operation, planId);
        context.SubscriptionPayments.Add(payment);

        using var provider = fixture.CreateServices(context);
        using var scope = provider.CreateScope();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IPromotionPaymentCoordinator>()
            .AttachPaymentAsync(operation, payment);

        // Биллинг связывает операцию с созданным платежом до сохранения.
        operation.SubscriptionPayment = payment;
        await context.SaveChangesAsync();
        await transaction.CommitAsync();

        return payment;
    }

    private async Task MarkPaidAsync(int paymentId)
    {
        await using var context = fixture.CreateContext();
        var payment = await context.SubscriptionPayments.SingleAsync(item => item.Id == paymentId);
        payment.PaidAt = DateTime.UtcNow;
        payment.PaymentStatus = "succeeded";
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task PercentageTargetedPlanGivesDiscountOnlyForListedPlan()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var monthly = await fixture.CreatePlanAsync(context, "Месячный бизнес");
        var annual = await fixture.CreatePlanAsync(context, "Годовой бизнес");
        annual.Price = 10000m;
        await context.SaveChangesAsync();
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 50m, [monthly.Id]);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        var monthlyQuote = await CalculateAsync(provider, organization.Id, monthly.Id, 1000m);
        Assert.True(monthlyQuote.HasDiscount);
        Assert.Equal(1000m, monthlyQuote.OriginalPrice);
        Assert.Equal(500m, monthlyQuote.FinalPrice);
        Assert.Equal(500m, monthlyQuote.DiscountAmount);
        Assert.Equal(promo.Id, monthlyQuote.PromoCodeId);
        Assert.Equal(promo.Code, monthlyQuote.PromoCode);
        Assert.NotNull(monthlyQuote.PromoCodeApplicationId);

        var annualQuote = await CalculateAsync(provider, organization.Id, annual.Id, 10000m);
        Assert.False(annualQuote.HasDiscount);
        Assert.Equal(10000m, annualQuote.FinalPrice);
        Assert.Equal(0m, annualQuote.DiscountAmount);
        Assert.Null(annualQuote.PromoCodeId);
        Assert.Null(annualQuote.PromoCodeApplicationId);
    }

    [Fact]
    public async Task FixedAmountDiscountAppliesAndFloorIsOneRuble()
    {
        await using var context = fixture.CreateContext();
        var first = await fixture.CreateOrganizationAsync(context);
        var second = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Фиксированная скидка");
        var rubles150 = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.FixedAmount, 150m, [plan.Id]);
        var rubles1500 = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.FixedAmount, 1500m, [plan.Id]);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, first.OwnerUserId, first.Id, rubles150.Code);
        await ApplyAsync(provider, second.OwnerUserId, second.Id, rubles1500.Code);

        var firstQuote = await CalculateAsync(provider, first.Id, plan.Id, 1000m);
        Assert.True(firstQuote.HasDiscount);
        Assert.Equal(850m, firstQuote.FinalPrice);
        Assert.Equal(150m, firstQuote.DiscountAmount);

        var secondQuote = await CalculateAsync(provider, second.Id, plan.Id, 1000m);
        Assert.True(secondQuote.HasDiscount);
        Assert.Equal(1m, secondQuote.FinalPrice);
        Assert.Equal(999m, secondQuote.DiscountAmount);
    }

    [Fact]
    public async Task ExpiredAfterApplyStillDiscountAndClaimAllowed()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Просроченная скидка");
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 50m, [plan.Id]);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        // После закрепления истечение срока промокода не отменяет скидку.
        await using (var update = fixture.CreateContext())
        {
            var persisted = await update.PromoCodes.SingleAsync(item => item.Id == promo.Id);
            persisted.ValidUntil = DateTime.UtcNow.AddDays(-1);
            await update.SaveChangesAsync();
        }

        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);
        Assert.Equal(500m, quote.FinalPrice);

        var operation = await ClaimAsync(context, organization.OwnerUserId!, plan.Id, quote, promo, organization.Id);

        await using var check = fixture.CreateContext();
        var application = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Claimed, application.Status);
        Assert.Equal(operation.Id, application.PaymentCreationOperationId);
        Assert.Equal(1, await check.PromoCodeApplications
            .CountAsync(item => item.PromoCodeId == promo.Id));

        // После истечения или отключения кода новые организации уже не могут его применить.
        var other = await fixture.CreateOrganizationAsync(context);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, other.OwnerUserId, other.Id, promo.Code));
        Assert.Equal(1, await check.PromoCodeApplications
            .CountAsync(item => item.PromoCodeId == promo.Id));
    }

    [Fact]
    public async Task InactiveAfterApplyStillDiscountAndClaimAllowed()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Отключённая скидка");
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 50m, [plan.Id]);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        // Отключение промокода после закрепления не отменяет скидку.
        await using (var update = fixture.CreateContext())
        {
            var persisted = await update.PromoCodes.SingleAsync(item => item.Id == promo.Id);
            persisted.IsActive = false;
            await update.SaveChangesAsync();
        }

        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);
        Assert.Equal(500m, quote.FinalPrice);

        var operation = await ClaimAsync(context, organization.OwnerUserId!, plan.Id, quote, promo, organization.Id);

        await using var check = fixture.CreateContext();
        var application = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Claimed, application.Status);
        Assert.Equal(operation.Id, application.PaymentCreationOperationId);

        // После истечения или отключения кода новые организации уже не могут его применить.
        var other = await fixture.CreateOrganizationAsync(context);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, other.OwnerUserId, other.Id, promo.Code));
    }

    [Fact]
    public async Task SecondMemberSeesDiscountButSameCodeReapplyIsDenied()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Скидка для коллеги");
        var colleague = new ApplicationUser { Email = $"promotions-{Guid.NewGuid():N}@example.test" };
        context.ApplicationUsers.Add(colleague);
        context.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = organization.Id,
            UserId = colleague.Id,
            RoleCode = OrganizationRoleCodes.Owner
        });
        await context.SaveChangesAsync();
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 10m, [plan.Id]);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        using (var scope = provider.CreateScope())
        {
            var colleagueQuote = await scope.ServiceProvider.GetRequiredService<IPromoCodeService>()
                .CalculatePriceAsync(colleague.Id, organization.Id, plan.Id, 1000m);
            Assert.True(colleagueQuote.HasDiscount);
            Assert.Equal(900m, colleagueQuote.FinalPrice);
            Assert.Equal(100m, colleagueQuote.DiscountAmount);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, colleague.Id, organization.Id, promo.Code));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code));

        await using var check = fixture.CreateContext();
        Assert.Equal(1, await check.PromoCodeApplications
            .CountAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task AssignedApplicationCountsTowardCapWithoutPayment()
    {
        await using var context = fixture.CreateContext();
        var plan = await fixture.CreatePlanAsync(context, "Скидка с лимитом");
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 30m, [plan.Id], cap: 1);
        var first = await fixture.CreateOrganizationAsync(context);
        var second = await fixture.CreateOrganizationAsync(context);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, first.OwnerUserId, first.Id, promo.Code);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApplyAsync(provider, second.OwnerUserId, second.Id, promo.Code));

        var quote = await CalculateAsync(provider, first.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);
        Assert.Equal(700m, quote.FinalPrice);

        await using var check = fixture.CreateContext();
        var application = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == first.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Assigned, application.Status);
        Assert.False(await check.SubscriptionPayments.AnyAsync(payment => payment.PromoCodeId == promo.Id));
    }

    [Fact]
    public async Task ClaimAttachConsumeFlowIsSingleUseAndIdempotent()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Полный цикл скидки");
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 50m, [plan.Id], cap: 2);
        var ownerUserId = organization.OwnerUserId!;
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, ownerUserId, organization.Id, promo.Code);
        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);

        var operation = await ClaimAsync(context, ownerUserId, plan.Id, quote, promo, organization.Id);

        await using (var afterClaim = fixture.CreateContext())
        {
            var claimed = await afterClaim.PromoCodeApplications.AsNoTracking()
                .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
            Assert.Equal(PromoCodeApplicationStatuses.Claimed, claimed.Status);
            Assert.Equal(operation.Id, claimed.PaymentCreationOperationId);
        }

        // Вторая операция не может захватить заявку: первая ещё не в терминальном статусе.
        var competitor = CreateOperation(ownerUserId, plan.Id, quote, promo, organization.Id);
        context.PaymentCreationOperations.Add(competitor);
        await context.SaveChangesAsync();
        using (var competitorProvider = fixture.CreateServices(context))
        {
            using var competitorScope = competitorProvider.CreateScope();
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => competitorScope.ServiceProvider.GetRequiredService<IPromotionPaymentCoordinator>()
                    .ClaimAsync(competitor));
            await transaction.RollbackAsync();
        }

        var payment = await AttachPaymentAsync(context, operation, plan.Id);

        // Скидка истекает после привязки, но до оплаты: расчёт уже не действует.
        await using (var update = fixture.CreateContext())
        {
            var persisted = await update.PromoCodes.SingleAsync(item => item.Id == promo.Id);
            persisted.ValidUntil = DateTime.UtcNow.AddHours(-1);
            await update.SaveChangesAsync();
        }

        await MarkPaidAsync(payment.Id);

        using (var consumeProvider = fixture.CreateServices(context))
        {
            using var consumeScope = consumeProvider.CreateScope();
            var coordinator = consumeScope.ServiceProvider.GetRequiredService<IPromotionPaymentCoordinator>();
            await using var transaction = await context.Database.BeginTransactionAsync();
            var paid = await context.SubscriptionPayments.SingleAsync(item => item.Id == payment.Id);
            await context.Entry(paid).ReloadAsync();

            // Погашение не проверяет срок промокода и идемпотентно при повторном вызове.
            await coordinator.ConsumeAsync(paid, organization.Id);
            await context.SaveChangesAsync();
            await coordinator.ConsumeAsync(paid, organization.Id);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using var check = fixture.CreateContext();
        var consumed = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Consumed, consumed.Status);
        Assert.NotNull(consumed.ConsumedAt);
        Assert.Equal(payment.Id, consumed.SubscriptionPaymentId);

        var persistedPayment = await check.SubscriptionPayments.AsNoTracking()
            .SingleAsync(item => item.Id == payment.Id);
        Assert.Equal(operation.PromoCodeApplicationId, persistedPayment.PromoCodeApplicationId);
        Assert.Equal(promo.Id, persistedPayment.PromoCodeId);
        Assert.Equal(promo.Code, persistedPayment.PromoCodeSnapshot);
        Assert.Equal(1000m, persistedPayment.OriginalAmount);
        Assert.Equal(500m, persistedPayment.PromoDiscountAmount);
        Assert.Equal(500m, persistedPayment.Amount);

        // После погашения будущая цена снова полная.
        var futureQuote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.False(futureQuote.HasDiscount);
        Assert.Equal(1000m, futureQuote.FinalPrice);
        Assert.Equal(1, await check.PromoCodeApplications
            .CountAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id));
    }

    [Fact]
    public async Task CanceledUnpaidPaymentReleasesClaimButCanceledPaidDoesNot()
    {
        await using var context = fixture.CreateContext();
        var plan = await fixture.CreatePlanAsync(context, "Отменённая оплата");
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 20m, [plan.Id]);
        var unpaid = await fixture.CreateOrganizationAsync(context);
        var paid = await fixture.CreateOrganizationAsync(context);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, unpaid.OwnerUserId, unpaid.Id, promo.Code);
        var unpaidQuote = await CalculateAsync(provider, unpaid.Id, plan.Id, 1000m);
        var unpaidOperation = await ClaimAsync(context, unpaid.OwnerUserId!, plan.Id, unpaidQuote, promo, unpaid.Id);
        var unpaidPayment = await AttachPaymentAsync(context, unpaidOperation, plan.Id);

        await using (var update = fixture.CreateContext())
        {
            var canceled = await update.SubscriptionPayments.SingleAsync(item => item.Id == unpaidPayment.Id);
            canceled.PaymentStatus = "canceled";
            await update.SaveChangesAsync();
        }

        // Неоплаченный отменённый платёж освобождает заявку: новая операция захватывает её же.
        var reuseOperation = await ClaimAsync(context, unpaid.OwnerUserId!, plan.Id, unpaidQuote, promo, unpaid.Id);

        await using (var afterReuse = fixture.CreateContext())
        {
            var application = await afterReuse.PromoCodeApplications.AsNoTracking()
                .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == unpaid.Id);
            Assert.Equal(PromoCodeApplicationStatuses.Claimed, application.Status);
            Assert.Equal(reuseOperation.Id, application.PaymentCreationOperationId);
            Assert.Null(application.SubscriptionPaymentId);
            Assert.Equal(1, await afterReuse.PromoCodeApplications
                .CountAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == unpaid.Id));
        }

        await ApplyAsync(provider, paid.OwnerUserId, paid.Id, promo.Code);
        var paidQuote = await CalculateAsync(provider, paid.Id, plan.Id, 1000m);
        var paidOperation = await ClaimAsync(context, paid.OwnerUserId!, plan.Id, paidQuote, promo, paid.Id);
        var paidPayment = await AttachPaymentAsync(context, paidOperation, plan.Id);

        await MarkPaidAsync(paidPayment.Id);

        await using (var update = fixture.CreateContext())
        {
            var canceled = await update.SubscriptionPayments.SingleAsync(item => item.Id == paidPayment.Id);
            canceled.PaymentStatus = "canceled";
            await update.SaveChangesAsync();
        }

        // Оплаченный (пусть и отменённый) платёж удерживает заявку.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ClaimAsync(context, paid.OwnerUserId!, plan.Id, paidQuote, promo, paid.Id));

        await using var check = fixture.CreateContext();
        Assert.Equal(1, await check.PromoCodeApplications
            .CountAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == paid.Id));
    }

    [Fact]
    public async Task ClaimRejectsUnknownOperationType()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Скидка с чужой операцией");
        var promo = await CreateMoneyCodeAsync(
            context, PromoCodeCatalogCodes.DiscountTypes.Percentage, 40m, [plan.Id]);
        using var provider = fixture.CreateServices(context);

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);
        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ClaimAsync(context, organization.OwnerUserId!, plan.Id, quote, promo, organization.Id,
                operationType: $"yookassa_topup:{organization.Id:D}"));

        await using var check = fixture.CreateContext();
        var application = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Assigned, application.Status);
        Assert.Null(application.PaymentCreationOperationId);
    }
}
