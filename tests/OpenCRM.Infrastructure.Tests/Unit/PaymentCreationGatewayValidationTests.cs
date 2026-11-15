using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class PaymentCreationGatewayValidationTests
{
    [Fact]
    public async Task InvalidBuyerIsRejectedBeforeDatabaseAccess()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>().Options;
        using var context = new OpenCRMContext(options);
        using var httpClient = new HttpClient();
        var config = new YooKassaConfig { ShopId = "shop", SecretKey = "secret", ReturnUrl = "https://app.example.test" };
        var processor = new PaymentCreationProcessor(
            httpClient,
            context,
            NullLogger.Instance,
            config);
        var gateway = new YooKassaPaymentCreationGateway(context, processor, Options.Create(config));
        var intent = new PaymentCreationIntent(
            Guid.NewGuid(),
            "user-1",
            "yookassa_purchase",
            1,
            new PaymentBuyerDto { TypeCode = "business", Name = "ООО Проверка", Inn = "некорректный ИНН" });

        var result = await gateway.CreateAsync(new PaymentCreationDraft(intent, 1, 500, "Платная подписка"));

        Assert.Equal(PaymentCreationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task InvalidIntentVariantsAreRejectedWithoutDatabaseAccess()
    {
        using var context = this.CreateContext();
        using var httpClient = new HttpClient();
        var config = this.CreateConfig();
        var gateway = new YooKassaPaymentCreationGateway(
            context,
            new PaymentCreationProcessor(httpClient, context, NullLogger.Instance, config),
            Options.Create(config));
        var validBuyer = new PaymentBuyerDto { TypeCode = "individual" };
        var operationId = Guid.NewGuid();
        var intents = new PaymentCreationIntent?[]
        {
            null,
            new(Guid.Empty, "user-1", "yookassa_purchase", 1, validBuyer),
            new(operationId, " ", "yookassa_purchase", 1, validBuyer),
            new(operationId, "user-1", null!, 1, validBuyer),
            new(operationId, "user-1", " ", 1, validBuyer),
            new(operationId, "user-1", "unsupported", 1, validBuyer),
            new(operationId, "user-1", "yookassa_purchase", 1, new PaymentBuyerDto { TypeCode = "business" }),
            new(operationId, "user-1", $"yookassa_workspace_purchase:{Guid.NewGuid()}", 1, new PaymentBuyerDto { TypeCode = "business" }),
            new(operationId, "user-1", $"yookassa_workspace_extension:{Guid.NewGuid()}", 1, new PaymentBuyerDto { TypeCode = "business" }),
            new(operationId, "user-1", "yookassa_workspace_purchase:not-a-guid", 1, new PaymentBuyerDto { TypeCode = "business" }),
            new(operationId, "user-1", "yookassa_workspace_purchase:00000000-0000-0000-0000-000000000000", 1, new PaymentBuyerDto { TypeCode = "business" })
        };

        foreach (var intent in intents)
        {
            Assert.Equal(PaymentCreationStatus.Invalid, (await gateway.ResumeAsync(intent!))!.Status);
        }
    }

    [Fact]
    public async Task InvalidDraftVariantsAreRejectedWithoutDatabaseAccess()
    {
        using var context = this.CreateContext();
        using var httpClient = new HttpClient();
        var config = this.CreateConfig();
        var gateway = new YooKassaPaymentCreationGateway(
            context,
            new PaymentCreationProcessor(httpClient, context, NullLogger.Instance, config),
            Options.Create(config));
        var buyer = new PaymentBuyerDto { TypeCode = "individual" };
        var operationId = Guid.NewGuid();
        var invalidDrafts = new PaymentCreationDraft?[]
        {
            null,
            new(new PaymentCreationIntent(operationId, "user-1", "yookassa_purchase", 1, buyer), 2, 500, "Оплата"),
            new(new PaymentCreationIntent(operationId, "user-1", "yookassa_purchase", null, buyer), 0, 500, "Оплата"),
            new(new PaymentCreationIntent(operationId, "user-1", "yookassa_purchase", null, buyer), 1, 0, "Оплата"),
            new(new PaymentCreationIntent(operationId, "user-1", "yookassa_purchase", null, buyer), 1, 500, " ")
        };

        foreach (var draft in invalidDrafts)
        {
            Assert.Equal(PaymentCreationStatus.Invalid, (await gateway.CreateAsync(draft!)).Status);
        }
    }

    private OpenCRMContext CreateContext() => new(new DbContextOptionsBuilder<OpenCRMContext>().Options);

    private YooKassaConfig CreateConfig() => new()
    {
        ShopId = "shop",
        SecretKey = "secret",
        ReturnUrl = "https://app.example.test"
    };
}
