using Microsoft.Extensions.Options;
using OpenCRM.Modules.Payments.Contracts;

namespace OpenCRM.Infrastructure.Payments.ServiceIntegration;

/// <summary> Сохраняет маршрут уже принятой покупки при переключении конфигурации. </summary>
internal sealed class RoutedPaymentCreationGateway(
    MicroservicePaymentCreationGateway payments,
    YooKassaPaymentCreationGateway legacy,
    IOptions<PaymentsServiceOptions> options) : IPaymentCreationGateway
{
    public async Task<PaymentCreationResult?> ResumeAsync(PaymentCreationIntent intent) =>
        await payments.ResumeAsync(intent) ?? await legacy.ResumeAsync(intent);

    public async Task<PaymentCreationResult> CreateAsync(PaymentCreationDraft draft)
    {
        var existing = await this.ResumeAsync(draft.Intent);
        if (existing != null)
        {
            return existing;
        }

        return options.Value.Enabled ? await payments.CreateAsync(draft) : await legacy.CreateAsync(draft);
    }
}
