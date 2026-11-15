using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Payments.Service.Domain;

namespace Payments.Tests.Integration;

public sealed class AdminReconciliationEndpointTests(PaymentServiceFixture fixture) : IClassFixture<PaymentServiceFixture>
{
    [Fact]
    public async Task RequestIsAuthenticatedAndOnlySchedulesProviderReadWithoutChangingMoneyOrCreatingOperations()
    {
        var purchaseId = Guid.NewGuid();
        await using var database = fixture.CreateDatabase();
        var order = new PaymentOrder
        {
            Id = Guid.NewGuid(), ApplicationId = "opencrm", PurchaseId = purchaseId, RequestHash = new string('A', 64),
            AmountMinor = 42000, Currency = "RUB", MerchantAccountId = "12345", ProviderPaymentId = "provider-admin-check",
            Status = PaymentStatus.Pending, IsTest = true, CreatedAt = DateTimeOffset.UtcNow,
            NextCheckAt = DateTimeOffset.UtcNow.AddHours(1)
        };
        database.PaymentOrders.Add(order);
        await database.SaveChangesAsync();
        var route = $"/internal/payments/opencrm/{purchaseId:D}/reconcile";
        using var unauthorizedRequest = new HttpRequestMessage(HttpMethod.Post, route) { Content = JsonContent.Create(new { }) };
        unauthorizedRequest.Headers.Add("X-Payments-Key", "incorrect-key");
        using var unauthorized = await fixture.Client.SendAsync(unauthorizedRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        using var wrongApplication = await fixture.Client.PostAsJsonAsync($"/internal/payments/other/{purchaseId:D}/reconcile", new { });
        Assert.Equal(HttpStatusCode.NotFound, wrongApplication.StatusCode);
        using var missing = await fixture.Client.PostAsJsonAsync($"/internal/payments/opencrm/{Guid.NewGuid():D}/reconcile", new { });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var accepted = await fixture.Client.PostAsJsonAsync(route, new { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var current = await database.PaymentOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
        Assert.True(current.NextCheckAt < DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Null(current.LastReconciledAt);
        Assert.Equal(PaymentStatus.Pending, current.Status);
        Assert.Equal(0, current.RefundedAmountMinor);
        Assert.Empty(await database.ProviderOperations.Where(item => item.PaymentOrderId == order.Id).ToListAsync());
        using var repeated = await fixture.Client.PostAsJsonAsync(route, new { OperationId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Accepted, repeated.StatusCode);
        await database.PaymentOrders.Where(item => item.Id == order.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(item => item.MerchantAccountId, "another-shop"));
        using var wrongMerchant = await fixture.Client.PostAsJsonAsync(route, new { });
        Assert.Equal(HttpStatusCode.Conflict, wrongMerchant.StatusCode);
    }
}
