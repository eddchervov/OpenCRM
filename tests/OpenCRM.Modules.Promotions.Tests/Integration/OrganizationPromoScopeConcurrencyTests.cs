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
public sealed class OrganizationPromoScopeConcurrencyTests
{
    private readonly PromotionsDatabaseFixture _fixture;

    public OrganizationPromoScopeConcurrencyTests(PromotionsDatabaseFixture fixture)
    {
        this._fixture = fixture;
    }

    [Fact]
    public async Task UpdateDuringApplyCannotChangeAssignedScope()
    {
        // Готовим организацию и два тарифа: закрепленный охват (planA) и альтернативу (planB).
        await using var seedContext = this._fixture.CreateContext();
        var organization = await this._fixture.CreateOrganizationAsync(seedContext);
        var ownerUserId = organization.OwnerUserId;
        var planA = await this._fixture.CreatePlanAsync(seedContext, "Охват A");
        var planB = await this._fixture.CreatePlanAsync(seedContext, "Охват B");

        // Код с фиксированной скидкой 300 и явным охватом по planA.
        var saveDto = new PromoCodeSaveDto
        {
            Code = "PS" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = "Гонка охвата",
            DiscountTypeCode = PromoCodeCatalogCodes.DiscountTypes.FixedAmount,
            DiscountValue = 300m,
            AppliesToAllPaidPlans = false,
            SubscriptionPlanIds = [planA.Id],
            AppliesToAllScenarios = true
        };

        await using (var createContext = this._fixture.CreateContext())
        {
            using var createProvider = this._fixture.CreateServices(createContext);
            using var createScope = createProvider.CreateScope();
            var created = await createScope.ServiceProvider
                .GetRequiredService<IPromoCodeService>()
                .CreateAsync(saveDto);
            saveDto.Code = created.Code;
        }

        int promoId;
        await using (var readContext = this._fixture.CreateContext())
        {
            var promo = await readContext.PromoCodes.SingleAsync(code => code.Code == saveDto.Code);
            promoId = promo.Id;
        }

        // Контекст применения: аудит-шлюз замораживает транзакцию после входа,
        // чтобы удерживать блокировки строки промокода до явного освобождения.
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var applyContext = this._fixture.CreateContext();
        using var applyProvider = this._fixture.CreateServices(applyContext,
            auditOverride: (dto, save) =>
            {
                entered.TrySetResult();
                return release.Task;
            });
        using var applyScope = applyProvider.CreateScope();
        var applyTask = applyScope.ServiceProvider
            .GetRequiredService<IOrganizationPromoCodeService>()
            .ApplyAsync(ownerUserId, organization.Id, saveDto.Code);

        Task<PromoCodeAdminDto?>? updateTask = null;
        await using (var adminContext = this._fixture.CreateContext())
        using (var updateProvider = this._fixture.CreateServices(adminContext))
        {
            using var updateScope = updateProvider.CreateScope();
            // Попытка сменить охват на planB во время удерживаемой транзакции применения.
            var updateDto = new PromoCodeSaveDto
            {
                Code = saveDto.Code,
                Name = "Гонка охвата",
                DiscountTypeCode = PromoCodeCatalogCodes.DiscountTypes.FixedAmount,
                DiscountValue = 300m,
                AppliesToAllPaidPlans = false,
                SubscriptionPlanIds = [planB.Id],
                AppliesToAllScenarios = true
            };
            // Доказываем реальное ожидание: наблюдаем ждущий на локе бэкенд в pg_stat_activity.
            var hasObservedLock = false;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                updateTask = updateScope.ServiceProvider
                    .GetRequiredService<IPromoCodeService>()
                    .UpdateAsync(promoId, updateDto);

                await using var probeContext = this._fixture.CreateContext();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline)
                {
                    const string sql =
                        "SELECT COUNT(*)::int AS \"Value\" FROM pg_stat_activity " +
                        "WHERE datname = current_database() AND wait_event_type = 'Lock' " +
                        "AND query LIKE '%PromoCodes%' AND pid <> pg_backend_pid()";
                    var waiting = await probeContext.Database
                        .SqlQueryRaw<int>(sql)
                        .SingleAsync();
                    if (waiting > 0)
                    {
                        hasObservedLock = true;
                        break;
                    }

                    // Задержка только внутри теста, без Thread.Sleep.
                    await Task.Delay(20);
                }

                Assert.True(hasObservedLock,
                    "Обновление не ждало блокировку строки промокода — гонка не воспроизведена.");

                release.TrySetResult();
                await applyTask.WaitAsync(TimeSpan.FromSeconds(10));
                await Assert.ThrowsAsync<InvalidOperationException>(() => updateTask!.WaitAsync(TimeSpan.FromSeconds(10)));
            }
            finally
            {
                release.TrySetResult();
                await applyTask.WaitAsync(TimeSpan.FromSeconds(10));
                if (updateTask is not null)
                {
                    try
                    {
                        await updateTask.WaitAsync(TimeSpan.FromSeconds(10));
                    }
                    catch (InvalidOperationException)
                    {
                        // Отклонение изменения охвата уже проверяется выше.
                    }
                }
            }
        }

        // Проверяем закрепленный охват: ровно одна связь с planA и прежняя выгода.
        await using (var checkContext = this._fixture.CreateContext())
        {
            var promo = await checkContext.PromoCodes
                .Include(code => code.Plans)
                .SingleAsync(code => code.Id == promoId);
            var planIds = promo.Plans.Select(plan => plan.SubscriptionPlanId).ToList();
            Assert.Single(planIds);
            Assert.DoesNotContain(planB.Id, planIds);
            Assert.Equal(planA.Id, planIds.Single());
            Assert.Equal(300m, promo.DiscountValue);
            Assert.Equal(1, await checkContext.PromoCodeApplications.CountAsync(app => app.PromoCodeId == promoId));
        }

        // Пересчет котировки: planA со скидкой 700, planB без скидки 1000.
        await using (var quoteContext = this._fixture.CreateContext())
        using (var quoteProvider = this._fixture.CreateServices(quoteContext))
        {
            using var quoteScope = quoteProvider.CreateScope();
            var promoService = quoteScope.ServiceProvider
                .GetRequiredService<IOrganizationPromoCodeService>();

            var quoteA = await promoService.CalculateDiscountAsync(organization.Id, planA.Id, 1000m);
            Assert.Equal(700m, quoteA.FinalPrice);
            Assert.True(quoteA.HasDiscount);

            var quoteB = await promoService.CalculateDiscountAsync(organization.Id, planB.Id, 1000m);
            Assert.Equal(1000m, quoteB.FinalPrice);
            Assert.False(quoteB.HasDiscount);
        }
    }
}
