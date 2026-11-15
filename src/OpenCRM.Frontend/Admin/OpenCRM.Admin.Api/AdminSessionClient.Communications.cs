using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    public Task<EmailMessagesPageResponse> GetEmailMessagesAsync(
        string search, string status, int page, int pageSize, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<EmailMessagesPageResponse>(
            $"api/v1/admin/email-messages?search={Uri.EscapeDataString(search)}&status={Uri.EscapeDataString(status)}&page={page}&pageSize={pageSize}",
            "Не удалось загрузить историю email.", cancellationToken);

    public Task<EmailMessageDetails> GetEmailMessageAsync(int id, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<EmailMessageDetails>(
            $"api/v1/admin/email-messages/{id}", "Не удалось загрузить письмо.", cancellationToken);

    public Task<EmailMessageEventsResponse> GetEmailMessageEventsAsync(int id, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<EmailMessageEventsResponse>(
            $"api/v1/admin/email-messages/{id}/events", "Не удалось загрузить события письма.", cancellationToken);

    public Task<AdminSupportMessagesPage> GetSupportMessagesAsync(
        string search, string status, string source, int page, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<AdminSupportMessagesPage>(
            $"api/v1/admin/support-messages?search={Uri.EscapeDataString(search)}&status={Uri.EscapeDataString(status)}&source={Uri.EscapeDataString(source)}&page={page}",
            "Не удалось загрузить обращения.", cancellationToken);

    public Task<AdminSupportMessage> GetSupportMessageAsync(int id, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<AdminSupportMessage>(
            $"api/v1/admin/support-messages/{id}", "Не удалось загрузить обращение.", cancellationToken);

    public Task<CommunicationResult> UpdateSupportMessageAsync(
        int id, string statusCode, string adminComment, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<CommunicationResult>(HttpMethod.Put,
            $"api/v1/admin/support-messages/{id}", new { statusCode, adminComment },
            "Не удалось сохранить обращение.", cancellationToken);

    public Task<CommunicationResult> ReplyToSupportMessageAsync(
        int id, string text, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<CommunicationResult>(HttpMethod.Post,
            $"api/v1/admin/support-messages/{id}/replies", new { text },
            "Не удалось отправить ответ.", cancellationToken);
}
