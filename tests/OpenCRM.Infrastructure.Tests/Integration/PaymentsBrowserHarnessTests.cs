namespace OpenCRM.Infrastructure.Tests.Integration;

public sealed class PaymentsBrowserHarnessManualFactAttribute : FactAttribute
{
    public PaymentsBrowserHarnessManualFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PAYMENTS_BROWSER_HARNESS"), "1", StringComparison.Ordinal))
        {
            this.Skip = "Установите PAYMENTS_BROWSER_HARNESS=1 для ручного browser harness.";
        }
    }
}

public sealed class PaymentsBrowserHarnessTests
{
    [PaymentsBrowserHarnessManualFact]
    public async Task StartsLocalPaymentsServiceForManualBrowserChecks()
    {
        await using var harness = await PaymentsBrowserHarness.StartAsync();
        await harness.RunUntilStoppedAsync();
    }
}
