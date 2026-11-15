using System.Net.Http.Json;
using System.Text.Json;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments.ServiceIntegration;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;
using Payments.Contracts;
using Payments.Service.Domain;
using Payments.Service.Processing;

namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentsServiceTransportAcceptanceTests(
    PaymentsServiceTransportAcceptanceFixture fixture) : IClassFixture<PaymentsServiceTransportAcceptanceFixture>
{
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task RealRabbitAndHttpRoundTripCreatesOneTestPaymentAndGrantsOneSubscriptionAfterRestart()
    {
        Assert.NotSame(fixture.OpenCRMServices.GetRequiredService<IBus>(),
            fixture.OpenCRMServices.GetRequiredService<IPaymentsServiceBus>());
        Assert.Equal("loopback", fixture.OpenCRMServices.GetRequiredService<IBus>().Address.Scheme);
        Assert.Equal("rabbitmq", fixture.OpenCRMServices.GetRequiredService<IPaymentsServiceBus>().Address.Scheme);
        var (user, plan) = await this.SeedAsync();
        var operationId = Guid.NewGuid();
        var intent = new PaymentCreationIntent(operationId, user.Id, "yookassa_purchase", plan.Id,
            new PaymentBuyerDto { TypeCode = "individual" });
        PaymentCreationResult created;
        await using (var scope = fixture.OpenCRMServices.CreateAsyncScope())
        {
            var gateway = scope.ServiceProvider.GetRequiredService<IPaymentCreationGateway>();
            created = await gateway.CreateAsync(new(intent, plan.Id, plan.Price, "Тестовая подписка"));
        }

        Assert.Equal(PaymentCreationStatus.InProgress, created.Status);
        var purchaseId = Assert.IsType<Guid>(created.PurchaseId);
        await WaitUntilAsync(async () =>
        {
            await using var scope = fixture.OpenCRMServices.CreateAsyncScope();
            var query = scope.ServiceProvider.GetRequiredService<IPaymentPurchaseQueryService>();
            return (await query.GetAsync(user.Id, purchaseId))?.Result.Status == PaymentCreationStatus.PaymentRequired;
        }, "Payments consumer не вернул ссылку на тестовую оплату через RabbitMQ.");

        PaymentPurchaseState pendingState;
        await using (var scope = fixture.OpenCRMServices.CreateAsyncScope())
        {
            var query = scope.ServiceProvider.GetRequiredService<IPaymentPurchaseQueryService>();
            pendingState = (await query.GetAsync(user.Id, purchaseId))!;
            var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenCRM.PaymentsService");
            using var response = await client.GetAsync($"internal/payments/opencrm/{purchaseId:D}");
            response.EnsureSuccessStatusCode();
            using var snapshot = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(purchaseId, snapshot.RootElement.GetProperty("purchaseId").GetGuid());
            Assert.Equal("Pending", snapshot.RootElement.GetProperty("paymentStatus").GetString());
            Assert.True(snapshot.RootElement.GetProperty("isTest").GetBoolean());
        }

        Assert.Equal(PaymentCreationStatus.PaymentRequired, pendingState.Result.Status);
        Assert.StartsWith("https://provider.example.test/payment/", pendingState.Result.PaymentUrl);
        Assert.False(pendingState.IsPaid);

        var savedCreateCommand = await this.GetSavedCreateCommandAsync(purchaseId);
        var paymentsBus = fixture.PaymentsServices.GetRequiredService<IBus>();
        var commandEndpoint = await paymentsBus.GetSendEndpoint(new Uri("queue:payments-opencrm-commands"));
        var eventEndpoint = await paymentsBus.GetSendEndpoint(new Uri("queue:payments-opencrm-events"));
        await commandEndpoint.Send(savedCreateCommand, send =>
        {
            send.MessageId = savedCreateCommand.CommandId;
            send.CorrelationId = purchaseId;
        });
        await WaitUntilAsync(async () =>
        {
            await using var context = fixture.CreatePaymentsContext();
            return await context.CommandReceipts.CountAsync(receipt => receipt.CommandId == savedCreateCommand.CommandId) == 1;
        }, "Дубликат команды не был обработан idempotently.");
        Assert.Equal(1, fixture.Provider.CreationCount);

        await this.SetReconciliationDueAsync(purchaseId);
        await WaitUntilAsync(async () =>
        {
            await using var context = fixture.CreateOpenCRMContext();
            var nextReconcileAt = await context.SubscriptionPurchases.Where(item => item.Id == purchaseId)
                .Select(item => item.NextReconcileAt).SingleAsync();
            return nextReconcileAt > DateTime.UtcNow.AddSeconds(30)
                && nextReconcileAt < DateTime.UtcNow.AddMinutes(2);
        }, "OpenCRM не выполнил внутреннюю HTTP-сверку платежного сервиса.");

        await fixture.RestartOpenCRMAsync(acceptNewPurchases: false);
        await using (var pausedScope = fixture.OpenCRMServices.CreateAsyncScope())
        {
            var gateway = pausedScope.ServiceProvider.GetRequiredService<IPaymentCreationGateway>();
            Assert.Equal(PaymentCreationStatus.PaymentRequired, (await gateway.ResumeAsync(intent))!.Status);
            var another = intent with { OperationId = Guid.NewGuid() };
            Assert.Equal(PaymentCreationStatus.Rejected,
                (await gateway.CreateAsync(new(another, plan.Id, plan.Price, "Новая покупка при паузе"))).Status);
        }

        fixture.Provider.MarkPaymentsSucceeded();
        await this.SetProviderReconciliationDueAsync(purchaseId);
        await using (var scope = fixture.PaymentsServices.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<PaymentProcessor>()
                .ProcessDueReconciliationsAsync(CancellationToken.None);
        }

        await WaitUntilAsync(async () =>
        {
            await using var scope = fixture.OpenCRMServices.CreateAsyncScope();
            var query = scope.ServiceProvider.GetRequiredService<IPaymentPurchaseQueryService>();
            return (await query.GetAsync(user.Id, purchaseId))?.Result.Status == PaymentCreationStatus.Completed;
        }, "Событие Succeeded не дошло до OpenCRM или подписка не была применена.");

        PaymentStateChanged succeededEvent;
        long eventCount;
        await using (var context = fixture.CreateOpenCRMContext())
        {
            Assert.Equal(1, await context.UserSubscriptions.CountAsync(item => item.UserId == user.Id));
            Assert.Single(await context.UserSubscriptionHistory.Where(item => item.UserId == user.Id).ToListAsync());
            var receipt = await context.SubscriptionPaymentEvents
                .Where(item => item.PurchaseId == purchaseId && item.Kind == "state" && item.ProcessedAt != null
                    && item.MessageId != null && item.Payload != null)
                .OrderByDescending(item => item.CreatedAt)
                .FirstAsync();
            succeededEvent = JsonSerializer.Deserialize<PaymentStateChanged>(receipt.Payload!)!;
            eventCount = await context.SubscriptionPaymentEvents.CountAsync(item => item.PurchaseId == purchaseId);
        }

        var opencrmBus = (IBusControl)fixture.OpenCRMServices.GetRequiredService<IPaymentsServiceBus>();
        await opencrmBus.StopAsync();
        await eventEndpoint.Send(succeededEvent, send =>
        {
            send.MessageId = succeededEvent.EventId;
            send.CorrelationId = purchaseId;
        });
        var staleEvent = succeededEvent with { EventId = Guid.NewGuid() };
        await eventEndpoint.Send(staleEvent, send =>
        {
            send.MessageId = staleEvent.EventId;
            send.CorrelationId = purchaseId;
        });
        await opencrmBus.StartAsync();

        await WaitUntilAsync(async () =>
        {
            await using var context = fixture.CreateOpenCRMContext();
            return await context.SubscriptionPaymentEvents.AnyAsync(item => item.PurchaseId == purchaseId
                && item.MessageId == staleEvent.EventId && item.Kind == "state")
                && await context.SubscriptionPaymentEvents.AnyAsync(item => item.PurchaseId == purchaseId
                    && item.Kind == "state" && item.Code == "stale_event_ignored")
                && await context.SubscriptionPaymentEvents.CountAsync(item => item.PurchaseId == purchaseId) == eventCount + 2;
        }, "Повтор события после перезапуска изменил inbox вместо дедупликации.");

        await using var verification = fixture.CreateOpenCRMContext();
        Assert.Equal(1, await verification.UserSubscriptions.CountAsync(item => item.UserId == user.Id));
        Assert.Equal(1, await verification.UserSubscriptionHistory.CountAsync(item => item.UserId == user.Id));
        Assert.Equal(1, await verification.PaymentCreationOperations.CountAsync(item => item.Id == operationId));
        Assert.Equal(1, fixture.Provider.CreationCount);
    }

    private async Task<(ApplicationUser User, SubscriptionPlan Plan)> SeedAsync()
    {
        await using var context = fixture.CreateOpenCRMContext();
        var user = new ApplicationUser { Email = "transport-" + Guid.NewGuid().ToString("N") + "@example.test" };
        var plan = new SubscriptionPlan
        {
            Name = "Acceptance тариф",
            Price = 420m,
            DurationDays = 30,
            Order = 1,
            IsActive = true,
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Personal
        };
        context.Users.Add(user);
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();

        return (user, plan);
    }

    private async Task<CreatePayment> GetSavedCreateCommandAsync(Guid purchaseId)
    {
        await using var context = fixture.CreateOpenCRMContext();
        var payload = await context.SubscriptionPaymentCommands.Where(item => item.PurchaseId == purchaseId && item.Kind == "create")
            .Select(item => item.Payload).SingleAsync();

        return JsonSerializer.Deserialize<CreatePayment>(payload)!;
    }

    private async Task SetReconciliationDueAsync(Guid purchaseId)
    {
        await using var context = fixture.CreateOpenCRMContext();
        await context.SubscriptionPurchases.Where(item => item.Id == purchaseId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.NextReconcileAt, DateTime.UtcNow.AddSeconds(-1)));
    }

    private async Task SetProviderReconciliationDueAsync(Guid purchaseId)
    {
        await using var context = fixture.CreatePaymentsContext();
        await context.PaymentOrders.Where(item => item.PurchaseId == purchaseId)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.NextCheckAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, string timeoutMessage)
    {
        var deadline = DateTime.UtcNow.Add(s_timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new TimeoutException(timeoutMessage);
    }
}
