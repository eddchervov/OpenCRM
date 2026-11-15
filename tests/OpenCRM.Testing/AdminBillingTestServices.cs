using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;

namespace OpenCRM.Testing;

public static class AdminBillingTestServices
{
    public static IAdminBillingQueryService CreateQuery(OpenCRMContext context) => Create<IAdminBillingQueryService>(context);

    public static TService Create<TService>(
        OpenCRMContext context,
        OpenCRM.Services.Interfaces.Payments.IYooKassaService? paymentProvider = null) where TService : notnull
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        if (paymentProvider is not null)
        {
            services.AddSingleton(paymentProvider);
        }

        services.AddOpenCRMBilling();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        return scope.ServiceProvider.GetRequiredService<TService>();
    }
}
