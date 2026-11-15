using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[Collection(PromotionsDatabaseCollection.Name)]
public sealed class OrganizationPromoPaymentFlowTests(PromotionsDatabaseFixture fixture)
{
    private static readonly TimeSpan s_exactTolerance = TimeSpan.FromMilliseconds(1);
    private static readonly TimeSpan s_annualTolerance = TimeSpan.FromMilliseconds(100);

    // Вложенный обработчик: без реальной сети, эмулирует ответ YooKassa на создание платежа.
    private sealed class FakeHandler(OpenCRMContext context) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            this.Requests++;

            // Критично: на момент сетевого вызова в контексте не должно быть внешней транзакции.
            Assert.Null(context.Database.CurrentTransaction);

            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var response = JsonSerializer.Serialize(new
            {
                id = Guid.NewGuid().ToString(),
                status = "pending",
                paid = false,
                created_at = DateTime.UtcNow,
                amount = root.GetProperty("amount").Clone(),
                metadata = root.GetProperty("metadata").Clone(),
                confirmation = new { type = "redirect", confirmation_url = "https://example.test/pay" }
            });

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrganizationPromoFlowsThroughPaymentToActivation(bool isAnnualExtensionAfterSevenMonths)
    {
        await using var context = fixture.CreateContext();
        var organization = await fixture.CreateOrganizationAsync(context);
        var plan = await fixture.CreatePlanAsync(context, "Бизнес организационная скидка");

        // Годовой сценарий: дорогой тариф на 365 дней и уже оплаченная подписка,
        // которую продлевают через 7 месяцев после закрепления скидки.
        decimal originalPrice = 1000m;
        decimal expectedFinal = 500m;
        decimal expectedDiscount = 500m;
        if (isAnnualExtensionAfterSevenMonths)
        {
            plan.Price = 10000m;
            plan.DurationDays = 365;
            originalPrice = 10000m;
            expectedFinal = 9700m;
            expectedDiscount = 300m;
            await context.SaveChangesAsync();
        }

        var promo = new PromoCode
        {
            Code = "PG" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = isAnnualExtensionAfterSevenMonths
                ? "Организационная скидка 300"
                : "Организационная скидка 50%",
            IsOrganizationCode = true,
            DiscountTypeCode = isAnnualExtensionAfterSevenMonths
                ? PromoCodeCatalogCodes.DiscountTypes.FixedAmount
                : PromoCodeCatalogCodes.DiscountTypes.Percentage,
            DiscountValue = isAnnualExtensionAfterSevenMonths ? 300m : 50m,
            AppliesToAllScenarios = true,
            AppliesToAllPaidPlans = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            Plans = [new PromoCodePlan { SubscriptionPlanId = plan.Id }]
        };
        context.PromoCodes.Add(promo);

        OrganizationSubscription seeded = new()
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = DateTime.UtcNow.AddDays(-155),
            EndDate = DateTime.UtcNow.AddDays(210),
            SourceCode = "paid"
        };
        if (isAnnualExtensionAfterSevenMonths)
        {
            context.OrganizationSubscriptions.Add(seeded);
        }

        await context.SaveChangesAsync();
        using var provider = fixture.CreateServices(context);

        // Шаг 1: применяем организационный промокод и считаем итоговую цену.
        using (var scope = provider.CreateScope())
        {
            var promoService = scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>();
            await promoService.ApplyAsync(organization.OwnerUserId, organization.Id, promo.Code);
        }

        if (isAnnualExtensionAfterSevenMonths)
        {
            using (var scope = provider.CreateScope())
            {
                var canExtend = await scope.ServiceProvider
                    .GetRequiredService<IOrganizationSubscriptionExtensionQueryService>()
                    .CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id);
                Assert.False(canExtend);
            }
        }

        PaymentPromotionDto promotionDto;
        decimal quoteFinal;
        using (var scope = provider.CreateScope())
        {
            var quote = await scope.ServiceProvider.GetRequiredService<IOrganizationPromoCodeService>()
                .CalculateDiscountAsync(organization.Id, plan.Id, plan.EffectivePrice);
            Assert.Equal(originalPrice, quote.OriginalPrice);
            Assert.Equal(expectedFinal, quote.FinalPrice);
            Assert.Equal(expectedDiscount, quote.DiscountAmount);
            quoteFinal = quote.FinalPrice;
            promotionDto = new PaymentPromotionDto
            {
                PromoCodeId = quote.PromoCodeId!.Value,
                PromoCode = quote.PromoCode!,
                OriginalAmount = quote.OriginalPrice,
                DiscountAmount = quote.DiscountAmount,
                OrganizationId = quote.OrganizationId,
                PromoCodeApplicationId = quote.PromoCodeApplicationId
            };
        }

        // Шаг 1a: имитируем прошедшие 7 месяцев сдвигом хранимых дат,
        // не ожидая реального времени. Срок закреплённой скидки не задан —
        // бессрочная заявка сохраняет цену и возможность продления.
        DateTime endBeforePayment = seeded.EndDate;
        if (isAnnualExtensionAfterSevenMonths)
        {
            await using (var shift = fixture.CreateContext())
            {
                var application = await shift.PromoCodeApplications
                    .SingleAsync(item => item.OrganizationId == organization.Id);
                application.CreatedAt = DateTime.UtcNow.AddMonths(-7);

                var code = await shift.PromoCodes.SingleAsync(item => item.Id == promo.Id);
                code.ValidUntil = DateTime.UtcNow.AddMonths(-6);
                code.IsActive = false;

                var subscription = await shift.OrganizationSubscriptions
                    .SingleAsync(item => item.Id == seeded.Id);
                subscription.EndDate = DateTime.UtcNow.AddDays(3);
                endBeforePayment = subscription.EndDate;

                await shift.SaveChangesAsync();
            }

            await using (var later = fixture.CreateContext())
            {
                using var laterProvider = fixture.CreateServices(later);
                using (var scope = laterProvider.CreateScope())
                {
                    var laterQuote = await scope.ServiceProvider
                        .GetRequiredService<IOrganizationPromoCodeService>()
                        .CalculateDiscountAsync(organization.Id, plan.Id, plan.EffectivePrice);
                    Assert.True(laterQuote.HasDiscount);
                    Assert.Equal(expectedFinal, laterQuote.FinalPrice);
                }

                using (var scope = laterProvider.CreateScope())
                {
                    var canExtend = await scope.ServiceProvider
                        .GetRequiredService<IOrganizationSubscriptionExtensionQueryService>()
                        .CanExtendSubscriptionAsync(organization.OwnerUserId, organization.Id);
                    Assert.True(canExtend);
                }
            }
        }

        // Шаг 2: создаем платеж через реальный сервис с DI-координатором и фейковым сетевым слоем.
        IPromotionPaymentCoordinator coordinator;
        using (var scope = provider.CreateScope())
        {
            coordinator = scope.ServiceProvider.GetRequiredService<IPromotionPaymentCoordinator>();
        }

        var handler = new FakeHandler(context);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var service = new YooKassaService(
            httpClient,
            NullLogger<YooKassaService>.Instance,
            Options.Create(new YooKassaConfig
            {
                ShopId = "test",
                SecretKey = "test",
                ReturnUrl = "https://example.test"
            }),
            context,
            coordinator);

        var buyer = new PaymentBuyerDto { TypeCode = "individual" };
        var idempotenceKey = Guid.NewGuid().ToString();
        var timestamp = DateTime.UtcNow;
        var method = isAnnualExtensionAfterSevenMonths
            ? $"yookassa_workspace_extension:{organization.Id:D}"
            : $"yookassa_workspace_purchase:{organization.Id:D}";

        var first = await service.CreatePaymentAsync(
            organization.OwnerUserId,
            plan.Id,
            quoteFinal,
            "Оплата тарифа рабочего пространства",
            method,
            buyer,
            "https://example.test/return",
            promotionDto,
            idempotenceKey,
            timestamp);
        Assert.True(first.IsSuccess, first.ErrorMessage);
        Assert.False(string.IsNullOrWhiteSpace(first.PaymentId));
        Assert.False(string.IsNullOrWhiteSpace(first.PaymentLink));

        // Повтор теми же аргументами не должен порождать новый внешний платеж.
        var second = await service.CreatePaymentAsync(
            organization.OwnerUserId,
            plan.Id,
            quoteFinal,
            "Оплата тарифа рабочего пространства",
            method,
            buyer,
            "https://example.test/return",
            promotionDto,
            idempotenceKey,
            timestamp);
        Assert.True(second.IsSuccess, second.ErrorMessage);
        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(first.PaymentLink, second.PaymentLink);
        Assert.Equal(1, handler.Requests);

        // Шаг 3: проверяем записи операции, заявки и платежа из чистого контекста.
        await using (var check = fixture.CreateContext())
        {
            var operation = await check.PaymentCreationOperations.AsNoTracking()
                .SingleAsync(item => item.IdempotenceKey == idempotenceKey);
            Assert.Equal(PaymentCreationOperationStatuses.Created, operation.Status);
            Assert.Equal(method, operation.OperationType);
            Assert.Equal(expectedFinal, operation.Amount);
            Assert.Equal(originalPrice, operation.OriginalAmount);
            Assert.Equal(expectedDiscount, operation.PromoDiscountAmount);
            Assert.Equal(promo.Id, operation.PromoCodeId);
            Assert.Equal(promotionDto.PromoCodeApplicationId, operation.PromoCodeApplicationId);
            Assert.Equal(organization.Id, operation.OrganizationId);

            var application = await check.PromoCodeApplications.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id);
            Assert.Equal(PromoCodeApplicationStatuses.Claimed, application.Status);
            Assert.Equal(operation.Id, application.PaymentCreationOperationId);

            var payment = await check.SubscriptionPayments.AsNoTracking()
                .SingleAsync(item => item.ExternalPaymentId == first.PaymentId);
            Assert.Equal(operation.SubscriptionPaymentId, payment.Id);
            Assert.Equal(payment.Id, application.SubscriptionPaymentId);
            Assert.Equal(promotionDto.PromoCodeApplicationId, payment.PromoCodeApplicationId);
            Assert.Equal(promo.Id, payment.PromoCodeId);
            Assert.Equal(expectedFinal, payment.Amount);
            Assert.Equal(originalPrice, payment.OriginalAmount);
            Assert.Equal(expectedDiscount, payment.PromoDiscountAmount);
            Assert.Equal(1, await check.SubscriptionPayments.AsNoTracking()
                .CountAsync(item => item.ExternalPaymentId == first.PaymentId));
        }

        // Шаг 4: провайдер подтверждает оплату; промокод и срок закреплённой скидки
        // к этому моменту истекли — уже выставленный счёт должен быть принят.
        await using (var paidContext = fixture.CreateContext())
        {
            var stored = await paidContext.SubscriptionPayments
                .SingleAsync(item => item.ExternalPaymentId == first.PaymentId);
            stored.PaidAt = DateTime.UtcNow;
            stored.PaymentStatus = "paid_waiting_confirmation";
            await paidContext.SaveChangesAsync();

            var expired = await paidContext.PromoCodes.SingleAsync(item => item.Id == promo.Id);
            expired.ValidUntil = DateTime.UtcNow.AddDays(-1);
            await paidContext.SaveChangesAsync();

            var application = await paidContext.PromoCodeApplications
                .SingleAsync(item => item.OrganizationId == organization.Id);
            application.DiscountValidUntil = DateTime.UtcNow.AddDays(-1);
            await paidContext.SaveChangesAsync();
        }

        await using (var activationContext = fixture.CreateContext())
        {
            using var activationProvider = fixture.CreateServices(activationContext);
            var result = await ActivateAgainAsync(
                activationProvider, organization.OwnerUserId, organization.Id, plan.Id,
                first.PaymentId!, isAnnualExtensionAfterSevenMonths);
            Assert.True(result.Success, result.Error);
            var repeat = await ActivateAgainAsync(
                activationProvider, organization.OwnerUserId, organization.Id, plan.Id,
                first.PaymentId!, isAnnualExtensionAfterSevenMonths);
            Assert.True(repeat.Success, repeat.Error);
        }

        await using (var final = fixture.CreateContext())
        {
            var paid = await final.OrganizationSubscriptions.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id);
            Assert.Equal("paid", paid.SourceCode);
            Assert.Equal(plan.Id, paid.SubscriptionPlanId);

            if (isAnnualExtensionAfterSevenMonths)
            {
                // Продление наращивает ту же подписку от конца оплаченного периода.
                Assert.Equal(seeded.Id, paid.Id);
                Assert.InRange((paid.EndDate - endBeforePayment).Duration(),
                    TimeSpan.FromDays(365) - s_annualTolerance,
                    TimeSpan.FromDays(365) + s_annualTolerance);
            }
            else
            {
                // Ровно 30 оплаченных дней, повторная активация не добавляет времени.
                Assert.InRange((paid.EndDate - paid.StartDate).Duration(),
                    TimeSpan.FromDays(30) - s_exactTolerance,
                    TimeSpan.FromDays(30) + s_exactTolerance);
            }

            var payment = await final.SubscriptionPayments.AsNoTracking()
                .SingleAsync(item => item.ExternalPaymentId == first.PaymentId);
            Assert.Equal("completed", payment.PaymentStatus);

            var application = await final.PromoCodeApplications.AsNoTracking()
                .SingleAsync(item => item.OrganizationId == organization.Id);
            Assert.Equal(PromoCodeApplicationStatuses.Consumed, application.Status);

            // Подарочной подписки не должно быть: скидка денежная, а не бонусные дни.
            Assert.Equal(1, await final.OrganizationSubscriptions.AsNoTracking()
                .CountAsync(item => item.OrganizationId == organization.Id));
        }
    }

    // Повторная активация в отдельном scope, как это делал бы повторный вебхук.
    private static async Task<SubscriptionResult> ActivateAgainAsync(
        IServiceProvider provider, string userId, Guid organizationId, int planId,
        string paymentId, bool extended)
    {
        using var scope = provider.CreateScope();
        var activation = scope.ServiceProvider.GetRequiredService<IOrganizationSubscriptionActivationService>();

        return extended
            ? await activation.ActivateExtendedSubscriptionAsync(userId, organizationId, planId, paymentId)
            : await activation.ActivateNewSubscriptionAsync(userId, organizationId, planId, paymentId);
    }
}
