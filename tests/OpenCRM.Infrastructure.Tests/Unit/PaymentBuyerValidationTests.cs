using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Payments;
using OpenCRM.Services.Interfaces.Payments.Dto;

namespace OpenCRM.Infrastructure.Tests.Unit;

public sealed class PaymentBuyerValidationTests
{
    [Fact]
    public async Task PaymentCannotStartWithoutBuyerSelection()
    {
        using var httpClient = new HttpClient();
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var service = new YooKassaService(
            httpClient,
            NullLogger<YooKassaService>.Instance,
            Options.Create(new YooKassaConfig()),
            context);

        var result = await service.CreatePaymentAsync("user", 1, 100, "Тариф", "purchase", null!);

        Assert.False(result.IsSuccess);
        Assert.Contains("Укажите, кто покупает", result.ErrorMessage);
    }

    [Fact]
    public async Task PaymentCannotStartWithInvalidBusinessInn()
    {
        using var httpClient = new HttpClient();
        await using var context = new OpenCRMContext(new DbContextOptionsBuilder<OpenCRMContext>().Options);
        var service = new YooKassaService(
            httpClient,
            NullLogger<YooKassaService>.Instance,
            Options.Create(new YooKassaConfig()),
            context);
        var buyer = new PaymentBuyerDto
        {
            TypeCode = "business",
            Name = "ООО Тест",
            Inn = "1234567890"
        };

        var result = await service.CreatePaymentAsync("user", 1, 100, "Тариф", "purchase", buyer);

        Assert.False(result.IsSuccess);
        Assert.Contains("контрольные цифры", result.ErrorMessage);
    }
}
