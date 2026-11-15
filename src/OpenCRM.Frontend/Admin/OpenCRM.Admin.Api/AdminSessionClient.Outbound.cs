using System.Net;
using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.Api;

public sealed partial class AdminSessionClient
{
    public Task<AdminEmailComposeData> GetEmailComposeDataAsync(string? userSearch, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<AdminEmailComposeData>(
            $"api/v1/admin/email-messages/compose-data?userSearch={Uri.EscapeDataString(userSearch ?? string.Empty)}",
            "Не удалось загрузить адресатов.", cancellationToken);

    public Task<AdminEmailRecipientsPreview> PreviewEmailRecipientsAsync(
        AdminEmailRecipientRequest request,
        CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminEmailRecipientsPreview>(HttpMethod.Post,
            "api/v1/admin/email-messages/preview-recipients", request,
            "Не удалось проверить адресатов.", cancellationToken);

    public Task<AdminEmailCampaignScheduleResponse> ScheduleEmailCampaignAsync(
        AdminEmailCampaignScheduleRequest request,
        CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminEmailCampaignScheduleResponse>(HttpMethod.Post,
            "api/v1/admin/email-messages/schedule", request,
            "Не удалось запланировать письмо.", cancellationToken);

    public Task<AdminEmailCampaignsPage> GetEmailCampaignsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<AdminEmailCampaignsPage>(
            $"api/v1/admin/email-messages/campaigns?page={page}&pageSize={pageSize}",
            "Не удалось загрузить рассылки.", cancellationToken);

    public Task PauseEmailCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminEmptyResponse>(HttpMethod.Post,
            $"api/v1/admin/email-messages/campaigns/{id}/pause", null,
            "Не удалось приостановить рассылку.", cancellationToken);

    public Task ResumeEmailCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminEmptyResponse>(HttpMethod.Post,
            $"api/v1/admin/email-messages/campaigns/{id}/resume", null,
            "Не удалось возобновить рассылку.", cancellationToken);

    public Task CancelEmailCampaignAsync(Guid id, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminEmptyResponse>(HttpMethod.Post,
            $"api/v1/admin/email-messages/campaigns/{id}/cancel", null,
            "Не удалось отменить рассылку.", cancellationToken);

    public Task<AdminSmsHistoryPage> GetSmsHistoryAsync(CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<AdminSmsHistoryPage>(
            "api/v1/admin/sms-sending-histories", "Не удалось загрузить историю SMS.", cancellationToken);

    public Task<IReadOnlyList<AdminSiteNotification>> GetSiteNotificationsAsync(CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminSiteNotification>>(
            "api/v1/admin/site-notifications", "Не удалось загрузить уведомления сайта.", cancellationToken);

    public async Task<AdminSiteNotificationHistoryPage> GetSiteNotificationHistoryAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await this.GetAuthorizedReportAsync<AdminSiteNotificationHistoryPage>(
                $"api/v1/admin/site-notifications/history?page={page}&pageSize={pageSize}",
                "Не удалось загрузить историю уведомлений сайта.", cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            var allNotifications = await this.GetSiteNotificationsAsync(cancellationToken);
            var history = allNotifications
                .Where(notification => !notification.IsTemplate)
                .OrderByDescending(notification => notification.CreatedAt)
                .ThenByDescending(notification => notification.Id)
                .ToArray();
            pageSize = Math.Clamp(pageSize, 10, 100);
            page = Math.Max(page, 1);
            var pageCount = Math.Max(1, (int)Math.Ceiling(history.Length / (double)pageSize));
            page = Math.Min(page, pageCount);

            return new AdminSiteNotificationHistoryPage
            {
                Items = history.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                TotalCount = history.Length,
                Page = page,
                PageSize = pageSize,
                IsLegacyResponse = true
            };
        }
    }

    public async Task<IReadOnlyList<AdminSiteNotification>> GetSiteNotificationTemplatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await this.GetAuthorizedReportAsync<IReadOnlyList<AdminSiteNotification>>(
                "api/v1/admin/site-notifications/templates", "Не удалось загрузить шаблоны уведомлений.", cancellationToken);
        }
        catch (ApiException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            var notifications = await this.GetSiteNotificationsAsync(cancellationToken);

            return notifications.Where(notification => notification.IsTemplate).ToArray();
        }
    }

    public Task<IReadOnlyList<AdminSiteNotificationUser>> GetSiteNotificationUsersAsync(string? search, CancellationToken cancellationToken = default) =>
        this.GetAuthorizedReportAsync<IReadOnlyList<AdminSiteNotificationUser>>(
            $"api/v1/admin/site-notifications/users?search={Uri.EscapeDataString(search ?? string.Empty)}",
            "Не удалось найти пользователей.", cancellationToken);

    public Task<AdminSiteNotification> UpdateSiteNotificationTemplateAsync(
        Guid id,
        AdminSiteNotificationTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminSiteNotification>(HttpMethod.Put,
            $"api/v1/admin/site-notifications/templates/{id}", request,
            "Не удалось сохранить шаблон.", cancellationToken);

    public Task SendSiteNotificationAsync(AdminSiteNotificationSendRequest request, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminEmptyResponse>(HttpMethod.Post,
            "api/v1/admin/site-notifications/send", request,
            "Не удалось отправить уведомление.", cancellationToken);

    public Task DeleteSiteNotificationAsync(Guid id, CancellationToken cancellationToken = default) =>
        this.SendAuthorizedRequestAsync<AdminEmptyResponse>(HttpMethod.Delete,
            $"api/v1/admin/site-notifications/{id}", null,
            "Не удалось удалить уведомление.", cancellationToken);
}
