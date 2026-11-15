using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Modules.Promotions.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[Collection(PromotionsDatabaseCollection.Name)]
public sealed class OrganizationPromoDiscountValidityTests(PromotionsDatabaseFixture fixture)
{
    private static readonly TimeSpan s_tolerance = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// DTO денежного организационного промокода со сроком закреплённой скидки.
    /// </summary>
    private static PromoCodeSaveDto MoneyDto(int[] planIds, int? validityDays)
    {
        return new PromoCodeSaveDto
        {
            Code = "PV" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = "Денежная скидка со сроком",
            DiscountTypeCode = PromoCodeCatalogCodes.DiscountTypes.FixedAmount,
            DiscountValue = 300m,
            ValidUntil = DateTime.UtcNow.AddDays(30),
            DiscountValidityDays = validityDays,
            AppliesToAllPaidPlans = false,
            SubscriptionPlanIds = [.. planIds]
        };
    }

    private static async Task<PromoCodeAdminDto> AdminCreateAsync(
        IServiceProvider provider, PromoCodeSaveDto dto)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IPromoCodeService>().CreateAsync(dto);
    }

    private static async Task<PromoCodeAdminDto> AdminUpdateAsync(
        IServiceProvider provider, int id, PromoCodeSaveDto dto)
    {
        using var scope = provider.CreateScope();
        var updated = await scope.ServiceProvider.GetRequiredService<IPromoCodeService>().UpdateAsync(id, dto);

        return updated!;
    }

    private static async Task<OrganizationPromoApplicationDto> ApplyAsync(
        IServiceProvider provider, string actorUserId, Guid organizationId, string code)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
            .ApplyAsync(actorUserId, organizationId, code);
    }

    private static async Task<OrganizationPromoApplicationDto> GetSingleApplicationAsync(
        IServiceProvider provider, string actorUserId, Guid organizationId)
    {
        using var scope = provider.CreateScope();
        var history = await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
            .GetApplicationsAsync(actorUserId, organizationId);

        return Assert.Single(history);
    }

    private static async Task<PromotionPriceDto> CalculateAsync(
        IServiceProvider provider, Guid organizationId, int planId, decimal originalPrice)
    {
        using var scope = provider.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
            .CalculateDiscountAsync(organizationId, planId, originalPrice);
    }

    private static PaymentCreationOperation CreateOperation(
        string userId, int planId, PromotionPriceDto quote, PromoCodeAdminDto promo, Guid organizationId)
    {
        return new PaymentCreationOperation
        {
            IdempotenceKey = "validity-" + Guid.NewGuid().ToString("N"),
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

    /// <summary>
    /// Захват заявки координатором в транзакции вызывающей стороны.
    /// </summary>
    private async Task ClaimAsync(
        OpenCRMContext context, string userId, int planId, PromotionPriceDto quote,
        PromoCodeAdminDto promo, Guid organizationId)
    {
        var operation = CreateOperation(userId, planId, quote, promo, organizationId);
        context.PaymentCreationOperations.Add(operation);
        await context.SaveChangesAsync();

        using var provider = fixture.CreateServices(context);
        using var scope = provider.CreateScope();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await scope.ServiceProvider.GetRequiredService<IPromotionPaymentCoordinator>().ClaimAsync(operation);
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task ApplyWithDeadlineSnapshotsAppliedAtPlusDays()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Скидка со сроком 30 дней");
        using var provider = fixture.CreateServices(context);
        var promo = await AdminCreateAsync(provider, MoneyDto([plan.Id], 30));

        var applied = await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        // Возвращённый срок — снимок момента применения плюс 30 дней, а не срок купона.
        Assert.InRange(applied.DiscountValidUntil!.Value,
            applied.AppliedAt.AddDays(30) - s_tolerance,
            applied.AppliedAt.AddDays(30) + s_tolerance);

        var history = await GetSingleApplicationAsync(provider, organization.OwnerUserId, organization.Id);
        Assert.Equal(applied.Id, history.Id);
        Assert.InRange(history.AppliedAt,
            applied.AppliedAt - s_tolerance, applied.AppliedAt + s_tolerance);
        Assert.InRange(history.DiscountValidUntil!.Value,
            applied.DiscountValidUntil.Value - s_tolerance,
            applied.DiscountValidUntil.Value + s_tolerance);
    }

    [Fact]
    public async Task ApplyWithoutDeadlineKeepsDiscountForever()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Бессрочная закреплённая скидка");
        using var provider = fixture.CreateServices(context);
        var promo = await AdminCreateAsync(provider, MoneyDto([plan.Id], null));

        var applied = await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        // Без DiscountValidityDays закреплённая скидка бессрочна.
        Assert.Null(applied.DiscountValidUntil);
        var history = await GetSingleApplicationAsync(provider, organization.OwnerUserId, organization.Id);
        Assert.Null(history.DiscountValidUntil);

        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);
        Assert.Equal(700m, quote.FinalPrice);
    }

    [Fact]
    public async Task CouponUpdateDoesNotAlterAssignedDeadline()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Срок не меняется обновлением");
        using var provider = fixture.CreateServices(context);
        var promo = await AdminCreateAsync(provider, MoneyDto([plan.Id], 30));

        var applied = await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);
        var assignedDeadline = applied.DiscountValidUntil!.Value;

        // Администратор меняет и срок, и активность, и валидность купона — заявка уже закреплена.
        var update = MoneyDto([plan.Id], 60);
        update.Code = promo.Code;
        update.IsActive = false;
        update.ValidUntil = DateTime.UtcNow.AddDays(-1);
        await AdminUpdateAsync(provider, promo.Id, update);

        await using var check = fixture.CreateContext();
        var persisted = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.InRange(persisted.DiscountValidUntil!.Value,
            assignedDeadline - s_tolerance, assignedDeadline + s_tolerance);

        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);
        Assert.Equal(700m, quote.FinalPrice);
    }

    [Fact]
    public async Task ExpiredAppDeadlineStopsDiscountAndRejectsNewClaim()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Истёкший срок заявки");
        using var provider = fixture.CreateServices(context);
        var promo = await AdminCreateAsync(provider, MoneyDto([plan.Id], 30));

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        var validQuote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);

        // Сдвигаем хранимый срок заявки на мгновение в прошлое.
        await using (var shift = fixture.CreateContext())
        {
            var application = await shift.PromoCodeApplications
                .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
            application.DiscountValidUntil = DateTime.UtcNow.AddMilliseconds(-1);
            await shift.SaveChangesAsync();
        }

        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.False(quote.HasDiscount);
        Assert.Equal(1000m, quote.FinalPrice);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => ClaimAsync(context, organization.OwnerUserId!, plan.Id, validQuote, promo, organization.Id));

        await using var check = fixture.CreateContext();
        var persisted = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Assigned, persisted.Status);
        Assert.Null(persisted.PaymentCreationOperationId);
    }

    [Fact]
    public async Task ValidAppDeadlineAllowsClaimRegardlessOfCouponState()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Заявка сильнее купона");
        using var provider = fixture.CreateServices(context);
        var promo = await AdminCreateAsync(provider, MoneyDto([plan.Id], 30));

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);
        var quote = await CalculateAsync(provider, organization.Id, plan.Id, 1000m);
        Assert.True(quote.HasDiscount);
        Assert.Equal(700m, quote.FinalPrice);

        // Состояние купона проверяется только при Apply: после закрепления отключённый,
        // просроченный и ещё не начавшийся купон не мешают захвату в пределах срока заявки.
        var update = MoneyDto([plan.Id], 30);
        update.Code = promo.Code;
        update.IsActive = false;
        update.ValidFrom = DateTime.UtcNow.AddDays(1);
        update.ValidUntil = DateTime.UtcNow.AddDays(2);
        await AdminUpdateAsync(provider, promo.Id, update);

        await ClaimAsync(context, organization.OwnerUserId!, plan.Id, quote, promo, organization.Id);

        await using var check = fixture.CreateContext();
        var persisted = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Equal(PromoCodeApplicationStatuses.Claimed, persisted.Status);
    }

    [Fact]
    public async Task UsedCodeCannotChangePlanAndScenarioAndBenefitButCanChangeValidity()
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var planA = await fixture.CreatePlanAsync(context, "Тариф A для заморозки охвата");
        using var provider = fixture.CreateServices(context);
        var promo = await AdminCreateAsync(provider, MoneyDto([planA.Id], null));

        await ApplyAsync(provider, organization.OwnerUserId, organization.Id, promo.Code);

        await using var planBContext = fixture.CreateContext();
        var planB = await fixture.CreatePlanAsync(planBContext, "Тариф B для заморозки охвата");

        // Смена тарифа после применения запрещена.
        var planChange = MoneyDto([planB.Id], null);
        planChange.Code = promo.Code;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminUpdateAsync(provider, promo.Id, planChange));

        // Смена направления после применения запрещена.
        var scenarioChange = MoneyDto([planA.Id], null);
        scenarioChange.Code = promo.Code;
        scenarioChange.AppliesToAllScenarios = false;
        scenarioChange.ScenarioCodes = ["fleet"];
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminUpdateAsync(provider, promo.Id, scenarioChange));

        // Смена типа бонуса после применения запрещена.
        var typeChange = MoneyDto([planA.Id], null);
        typeChange.Code = promo.Code;
        typeChange.DiscountTypeCode = PromoCodeCatalogCodes.DiscountTypes.Percentage;
        typeChange.DiscountValue = 10m;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminUpdateAsync(provider, promo.Id, typeChange));

        // Тот же охват: срок скидки менять можно, снимок заявки не пересчитывается.
        var valid = MoneyDto([planA.Id], 60);
        valid.Code = promo.Code;
        await AdminUpdateAsync(provider, promo.Id, valid);

        await using var check = fixture.CreateContext();
        var persisted = await check.PromoCodeApplications.AsNoTracking()
            .SingleAsync(item => item.PromoCodeId == promo.Id && item.OrganizationId == organization.Id);
        Assert.Null(persisted.DiscountValidUntil);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3651)]
    public async Task AdminValidationRejectsInvalidDiscountValidityDays(int days)
    {
        await using var context = fixture.CreateContext();
        var plan = await fixture.CreatePlanAsync(context, "Некорректный срок скидки");
        using var provider = fixture.CreateServices(context);

        // Границы диапазона 1..3650 отклоняются при создании администратором.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AdminCreateAsync(provider, MoneyDto([plan.Id], days)));
    }

    [Fact]
    public async Task AdminValidationRejectsValidityDaysForGiftCode()
    {
        await using var context = fixture.CreateContext();
        var plan = await fixture.CreatePlanAsync(context, "Подарочный код со сроком");
        using var provider = fixture.CreateServices(context);
        var dto = new PromoCodeSaveDto
        {
            Code = "PG" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = "Подарочные дни со сроком",
            DiscountTypeCode = PromoCodeCatalogCodes.DiscountTypes.BonusDays,
            BonusDays = 30,
            GiftTemplatePlanId = plan.Id,
            DiscountValidityDays = 30,
            AppliesToAllScenarios = true
        };

        // Срок закреплённой скидки не имеет смысла для подарочного кода.
        await Assert.ThrowsAsync<InvalidOperationException>(() => AdminCreateAsync(provider, dto));
    }
}
