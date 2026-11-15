using Microsoft.EntityFrameworkCore;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Сервис журнала email-сообщений для администратора. </summary>
public sealed class AdminEmailJournalService(OpenCRMContext context) : IAdminEmailJournalService
{
    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Возвращает страницу журнала email-сообщений с фильтрами. </summary>
    public async Task<EmailMessagesPageResponse> GetPageAsync(
        string? search = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var query = this._context.EmailMessages
            .AsNoTracking()
            .Include(x => x.User)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.StatusCode == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x =>
                x.ToEmail.ToLower().Contains(normalizedSearch) ||
                x.Subject.ToLower().Contains(normalizedSearch) ||
                (x.ProviderMessageId != null && x.ProviderMessageId.ToLower().Contains(normalizedSearch)) ||
                (x.LastEventCode != null && x.LastEventCode.ToLower().Contains(normalizedSearch)) ||
                (x.User != null && x.User.Email != null && x.User.Email.ToLower().Contains(normalizedSearch)) ||
                (x.User != null && x.User.UserName != null && x.User.UserName.ToLower().Contains(normalizedSearch)) ||
                (x.User != null && x.User.PhoneNumber != null && x.User.PhoneNumber.ToLower().Contains(normalizedSearch)) ||
                (x.User != null && x.User.OriginalEmail != null && x.User.OriginalEmail.ToLower().Contains(normalizedSearch)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new EmailMessageListItem
            {
                Id = x.Id,
                ToEmail = x.ToEmail,
                Subject = x.Subject,
                StatusCode = x.StatusCode,
                UserId = x.UserId,
                UserDisplayName = x.User == null
                    ? null
                    : x.User.Email ?? x.User.UserName ?? x.User.PhoneNumber ?? x.User.Id,
                CreatedAt = x.CreatedAt,
                SentAt = x.SentAt,
                ErrorMessage = x.ErrorMessage,
                Provider = x.Provider,
                ProviderMessageId = x.ProviderMessageId,
                LastEventCode = x.LastEventCode,
                LastEventAt = x.LastEventAt,
                OpenCount = x.OpenCount,
                ClickCount = x.ClickCount,
                EventCount = x.Events.Count
            })
            .ToListAsync(cancellationToken);

        return new EmailMessagesPageResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary> Возвращает детали email-сообщения по идентификатору. </summary>
    public async Task<EmailMessageDetails?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var message = await this._context.EmailMessages
            .AsNoTracking()
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (message == null)
        {
            return null;
        }

        return new EmailMessageDetails
        {
            Id = message.Id,
            ToEmail = message.ToEmail,
            Subject = message.Subject,
            StatusCode = message.StatusCode,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            UserId = message.UserId,
            UserDisplayName = message.User == null
                ? null
                : message.User.Email ?? message.User.UserName ?? message.User.PhoneNumber ?? message.User.Id,
            CreatedAt = message.CreatedAt,
            SentAt = message.SentAt,
            ErrorMessage = message.ErrorMessage,
            Provider = message.Provider,
            ProviderMessageId = message.ProviderMessageId,
            ProviderResponse = message.ProviderResponse,
            LastEventCode = message.LastEventCode,
            LastEventAt = message.LastEventAt,
            OpenCount = message.OpenCount,
            ClickCount = message.ClickCount,
            EventCount = await this._context.EmailMessageEvents.CountAsync(x => x.EmailMessageId == message.Id, cancellationToken)
        };
    }

    /// <summary> Возвращает события доставки email-сообщения. </summary>
    public async Task<EmailMessageEventsResponse> GetEventsAsync(int id, CancellationToken cancellationToken)
    {
        var items = await this._context.EmailMessageEvents
            .AsNoTracking()
            .Where(x => x.EmailMessageId == id)
            .OrderByDescending(x => x.EventAt)
            .ThenByDescending(x => x.ReceivedAt)
            .Take(200)
            .Select(x => new EmailMessageEventItem
            {
                Id = x.Id,
                Provider = x.Provider,
                EventName = x.EventName,
                Status = x.Status,
                Email = x.Email,
                ProviderMessageId = x.ProviderMessageId,
                CampaignId = x.CampaignId,
                Url = x.Url,
                IpAddress = x.IpAddress,
                UserAgent = x.UserAgent,
                DeliveryInfo = x.DeliveryInfo,
                RawPayload = x.RawPayload,
                SignatureVerified = x.SignatureVerified,
                EventAt = x.EventAt,
                ReceivedAt = x.ReceivedAt
            })
            .ToListAsync(cancellationToken);

        return new EmailMessageEventsResponse { Items = items };
    }

    /// <summary> Возвращает страницу событий webhook email-провайдера. </summary>
    public async Task<EmailWebhookEventsPageResponse> GetWebhookEventsAsync(
        string? search = null,
        string? status = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var query = this._context.EmailMessageEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x =>
                (x.Email != null && x.Email.ToLower().Contains(normalizedSearch)) ||
                (x.ProviderMessageId != null && x.ProviderMessageId.ToLower().Contains(normalizedSearch)) ||
                (x.EventName != null && x.EventName.ToLower().Contains(normalizedSearch)) ||
                (x.Status != null && x.Status.ToLower().Contains(normalizedSearch)) ||
                (x.Url != null && x.Url.ToLower().Contains(normalizedSearch)));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.ReceivedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new EmailMessageEventItem
            {
                Id = x.Id,
                EmailMessageId = x.EmailMessageId,
                Provider = x.Provider,
                EventName = x.EventName,
                Status = x.Status,
                Email = x.Email,
                ProviderMessageId = x.ProviderMessageId,
                CampaignId = x.CampaignId,
                Url = x.Url,
                IpAddress = x.IpAddress,
                UserAgent = x.UserAgent,
                DeliveryInfo = x.DeliveryInfo,
                RawPayload = x.RawPayload,
                SignatureVerified = x.SignatureVerified,
                EventAt = x.EventAt,
                ReceivedAt = x.ReceivedAt
            })
            .ToListAsync(cancellationToken);

        return new EmailWebhookEventsPageResponse
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary> Возвращает сводные показатели email-рассылок. </summary>
    public async Task<EmailDashboardResponse> GetDashboardAsync(CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-14);

        var messageStatusCounts = await this._context.EmailMessages
            .AsNoTracking()
            .Where(x => x.CreatedAt >= since)
            .GroupBy(x => x.StatusCode)
            .Select(x => new EmailMetricItem { Code = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        var eventStatusCounts = await this._context.EmailMessageEvents
            .AsNoTracking()
            .Where(x => x.ReceivedAt >= since && x.Status != null)
            .GroupBy(x => x.Status!)
            .Select(x => new EmailMetricItem { Code = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);

        return new EmailDashboardResponse
        {
            Since = since,
            MessageStatusCounts = messageStatusCounts,
            EventStatusCounts = eventStatusCounts,
            TotalMessages = await this._context.EmailMessages.CountAsync(cancellationToken),
            TotalWebhookEvents = await this._context.EmailMessageEvents.CountAsync(cancellationToken),
            LastWebhookAt = await this._context.EmailMessageEvents
                .OrderByDescending(x => x.ReceivedAt)
                .Select(x => (DateTime?)x.ReceivedAt)
                .FirstOrDefaultAsync(cancellationToken)
        };
    }
}
