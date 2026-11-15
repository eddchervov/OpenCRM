using System.Text.Json;
using Payments.Service.Domain;
using Payments.Service.Providers;

namespace Payments.Tests.Unit;

public sealed class YooKassaPaymentRequestTests
{
    [Fact]
    public void DisabledProvider_BuildsRequestForTestingWithoutCredentials()
    {
        var provider = new DisabledPaymentProvider();
        var order = new PaymentOrder
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000011"),
            PurchaseId = Guid.Parse("00000000-0000-0000-0000-000000000022"),
            ApplicationId = "opencrm",
            AmountMinor = 1_025,
            Currency = "RUB",
            Description = "Подписка opencrm.example.invalid",
            ReturnUrl = "https://opencrm.example.invalid/payment/complete"
        };

        using var document = JsonDocument.Parse(provider.BuildPaymentRequest(order));
        var root = document.RootElement;

        Assert.Equal("10.25", root.GetProperty("amount").GetProperty("value").GetString());
        Assert.Equal("00000000-0000-0000-0000-000000000011",
            root.GetProperty("metadata").GetProperty("paymentOrderId").GetString());
    }

    [Fact]
    public async Task DisabledProvider_RejectsMoneyOperations()
    {
        var provider = new DisabledPaymentProvider();

        var exception = await Assert.ThrowsAsync<PaymentProviderException>(
            () => provider.CreatePaymentAsync("{}", "idempotence-key", CancellationToken.None));

        Assert.Equal("provider_disabled", exception.Code);
        Assert.True(exception.RequiresManualReview);
    }
}
