using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Сервис создания и обработки email-кампаний. </summary>
public sealed class EmailCampaignService(
    OpenCRMContext context,
    IEmailNotificationService emailSender,
    IEmailCampaignScheduler emailCampaignScheduler,
    ILogger<EmailCampaignService> logger) : IEmailCampaignService
{
    /// <summary> Создаёт email-кампанию по запросу. </summary>
    public async Task<EmailCampaignCreateResultDto> CreateCampaignAsync(EmailCampaignCreateDto request, string? createdByUserId = null)
    {
        var recipients = request.Recipients
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .GroupBy(x => x.Email.Trim().ToUpperInvariant())
            .Select(x => x.First())
            .ToList();

        if (recipients.Count == 0)
        {
            throw new InvalidOperationException("Не найдено получателей с email по выбранным условиям.");
        }

        var intervalSeconds = Math.Clamp(request.IntervalSeconds, 5, 86400);
        var startDelayMinutes = Math.Clamp(request.StartDelayMinutes, 0, 1440);
        var startAt = DateTime.UtcNow.AddMinutes(startDelayMinutes);

        var campaign = new EmailCampaign
        {
            Subject = request.Subject.Trim(),
            HtmlBody = request.HtmlBody.Trim(),
            StatusCode = EmailCampaignStatusCodes.Scheduled,
            TargetMode = request.TargetMode,
            TargetUserId = request.TargetUserId,
            TargetOrganizationTypeCode = request.TargetOrganizationTypeCode,
            OnlyConfirmedEmail = request.OnlyConfirmedEmail,
            OnlyAdvertisingConsent = request.OnlyAdvertisingConsent,
            IntervalSeconds = intervalSeconds,
            StartAt = startAt,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedByUserId = string.IsNullOrWhiteSpace(createdByUserId) ? null : createdByUserId,
            TotalCount = recipients.Count,
            Recipients = recipients
                .Select(x => new EmailCampaignRecipient
                {
                    UserId = x.UserId,
                    Email = x.Email.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email.Trim() : x.DisplayName.Trim(),
                    StatusCode = EmailCampaignRecipientStatusCodes.Pending,
                    CreatedAt = DateTime.UtcNow
                })
                .ToList()
        };

        context.EmailCampaigns.Add(campaign);
        await context.SaveChangesAsync();

        emailCampaignScheduler.ScheduleNext(campaign.Id, startAt - DateTime.UtcNow);

        return new EmailCampaignCreateResultDto
        {
            CampaignId = campaign.Id,
            ScheduledCount = recipients.Count,
            IntervalSeconds = intervalSeconds,
            FirstSendAt = startAt,
            LastSendAt = startAt.AddSeconds(intervalSeconds * Math.Max(0, recipients.Count - 1))
        };
    }

    /// <summary> Обрабатывает кампанию: формирует и ставит письма в очередь. </summary>
    public async Task ProcessCampaignAsync(Guid campaignId)
    {
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            var transaction = await context.Database.BeginTransactionAsync();
            await using (transaction.ConfigureAwait(true))
            {
                await this.ProcessCampaignCoreAsync(campaignId);
                await transaction.CommitAsync();
            }
        });
    }

    /// <summary> Внутренняя реализация обработки кампании. </summary>
    private async Task ProcessCampaignCoreAsync(Guid campaignId)
    {
        var now = DateTime.UtcNow;
        var campaigns = await context.EmailCampaigns.FromSqlInterpolated($"""
            SELECT * FROM "EmailCampaigns" WHERE "Id" = {campaignId} FOR UPDATE
            """).ToListAsync();
        var campaign = campaigns.SingleOrDefault();
        if (campaign == null ||
            campaign.StatusCode is EmailCampaignStatusCodes.Canceled or EmailCampaignStatusCodes.Paused or EmailCampaignStatusCodes.Completed)
        {
            return;
        }

        if (campaign.StartAt > now)
        {
            emailCampaignScheduler.ScheduleNext(campaign.Id, campaign.StartAt - now);
            return;
        }

        var recipient = await context.EmailCampaignRecipients
            .Where(x => x.EmailCampaignId == campaignId && x.StatusCode == EmailCampaignRecipientStatusCodes.Pending)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .FirstOrDefaultAsync();

        if (recipient == null)
        {
            if (campaign.SentCount + campaign.SimulatedCount + campaign.FailedCount + campaign.SkippedCount >= campaign.TotalCount)
            {
                campaign.StatusCode = EmailCampaignStatusCodes.Completed;
                campaign.CompletedAt = now;
                campaign.UpdatedAt = now;
                await context.SaveChangesAsync();
            }

            return;
        }

        if (campaign.StatusCode == EmailCampaignStatusCodes.Scheduled)
        {
            campaign.StatusCode = EmailCampaignStatusCodes.Running;
            campaign.StartedAt = now;
        }

        recipient.StatusCode = EmailCampaignRecipientStatusCodes.Queued;
        recipient.AttemptCount++;
        recipient.LastAttemptAt = now;
        campaign.UpdatedAt = now;

        try
        {
            recipient.ErrorMessage = null;
            await emailSender.QueueAsync(
                recipient.Email, campaign.Subject, campaign.HtmlBody,
                $"campaign:{campaign.Id:N}:{recipient.Id:N}", campaignRecipientId: recipient.Id);
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Unable to send email campaign {CampaignId} message to {Email}", campaignId, recipient.Email);
            recipient.StatusCode = EmailCampaignRecipientStatusCodes.Failed;
            recipient.ErrorMessage = ex.Message;
            campaign.FailedCount++;
            campaign.LastError = ex.Message;
        }

        campaign.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        var hasMoreRecipients = await context.EmailCampaignRecipients
            .AnyAsync(x => x.EmailCampaignId == campaignId && x.StatusCode == EmailCampaignRecipientStatusCodes.Pending);

        if (hasMoreRecipients)
        {
            emailCampaignScheduler.ScheduleNext(campaign.Id, TimeSpan.FromSeconds(campaign.IntervalSeconds));
            return;
        }

        if (campaign.SentCount + campaign.SimulatedCount + campaign.FailedCount + campaign.SkippedCount >= campaign.TotalCount)
        {
            campaign.StatusCode = EmailCampaignStatusCodes.Completed;
            campaign.CompletedAt = DateTime.UtcNow;
            campaign.UpdatedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
    }

    /// <summary> Возвращает страницу списка email-кампаний. </summary>
    public async Task<EmailCampaignsPageDto> GetCampaignsAsync(int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var query = context.EmailCampaigns.AsNoTracking();
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(x => x.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new EmailCampaignListItemDto
            {
                Id = x.Id,
                Subject = x.Subject,
                StatusCode = x.StatusCode,
                TargetMode = x.TargetMode,
                TargetOrganizationTypeCode = x.TargetOrganizationTypeCode,
                IntervalSeconds = x.IntervalSeconds,
                StartAt = x.StartAt,
                CreatedAt = x.CreatedAt,
                CompletedAt = x.CompletedAt,
                TotalCount = x.TotalCount,
                SentCount = x.SentCount,
                SimulatedCount = x.SimulatedCount,
                FailedCount = x.FailedCount,
                SkippedCount = x.SkippedCount,
                LastError = x.LastError
            })
            .ToListAsync();

        return new EmailCampaignsPageDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary> Устанавливает статус кампании по коду статуса. </summary>
    public async Task SetStatusAsync(Guid campaignId, string statusCode)
    {
        if (statusCode is not (EmailCampaignStatusCodes.Paused or EmailCampaignStatusCodes.Canceled or EmailCampaignStatusCodes.Running))
        {
            throw new InvalidOperationException("Недопустимый статус рассылки.");
        }

        var campaign = await context.EmailCampaigns.FirstOrDefaultAsync(x => x.Id == campaignId)
            ?? throw new InvalidOperationException("Рассылка не найдена.");

        if (campaign.StatusCode == EmailCampaignStatusCodes.Completed)
        {
            return;
        }

        campaign.StatusCode = statusCode;
        campaign.UpdatedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        if (statusCode == EmailCampaignStatusCodes.Running)
        {
            emailCampaignScheduler.ScheduleNext(campaign.Id, TimeSpan.Zero);
        }
    }
}
