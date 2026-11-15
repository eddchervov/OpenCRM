using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Тестовые операции с email-рассылкой. </summary>
public interface IAdminEmailTestOperationsService
{
    /// <summary> Отправляет тестовое письмо. </summary>
    Task<EmailAdminOperationResult<string>> SendTestEmailAsync(EmailTestSendRequest request);

    /// <summary> Создаёт тестовое webhook-событие. </summary>
    Task<EmailAdminOperationResult<EmailMessageEventItem>> CreateWebhookTestAsync(
        EmailWebhookTestRequest request,
        CancellationToken cancellationToken = default);
}
