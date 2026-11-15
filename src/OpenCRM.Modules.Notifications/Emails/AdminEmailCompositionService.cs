using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Modules.Notifications.Emails;

/// <summary> Сервис подготовки данных и кампаний административных email-рассылок. </summary>
public sealed class AdminEmailCompositionService(
    OpenCRMContext context,
    IEmailCampaignService emailCampaignService) : IAdminEmailCompositionService
{
    /// <summary> Возвращает данные для формы составления письма и поиск пользователей. </summary>
    public async Task<EmailComposeDataResponse> GetComposeDataAsync(
        string? userSearch,
        CancellationToken cancellationToken = default)
    {
        var organizationTypes = await context.OrganizationTypeDefinitions
            .AsNoTracking()
            .Where(x => x.IsSupported && x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new EmailOrganizationTypeOption
            {
                Code = x.Code,
                Name = x.Name
            })
            .ToListAsync(cancellationToken);

        var usersQuery = context.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Email != null && x.Email != "");

        if (!string.IsNullOrWhiteSpace(userSearch))
        {
            var search = userSearch.Trim();
            usersQuery = usersQuery.Where(x =>
                (x.Email != null && EF.Functions.ILike(x.Email, $"%{search}%")) ||
                (x.UserName != null && EF.Functions.ILike(x.UserName, $"%{search}%")) ||
                (x.PhoneNumber != null && EF.Functions.ILike(x.PhoneNumber, $"%{search}%")));
        }

        var users = await usersQuery
            .OrderByDescending(x => x.DatetimeCreation)
            .Take(30)
            .Select(x => new EmailUserOption
            {
                Id = x.Id,
                Email = x.Email!,
                Phone = x.PhoneNumber,
                DisplayName = x.Email ?? x.UserName ?? x.PhoneNumber ?? x.Id,
                EmailConfirmed = x.EmailConfirmed,
                AdvertisingConsentAccepted = x.AdvertisingConsentAccepted
            })
            .ToListAsync(cancellationToken);

        return new EmailComposeDataResponse
        {
            OrganizationTypes = organizationTypes,
            Users = users
        };
    }

    /// <summary> Возвращает предварительный просмотр получателей рассылки. </summary>
    public async Task<EmailAdminOperationResult<EmailRecipientsPreviewResponse>> PreviewRecipientsAsync(
        EmailCampaignRecipientsRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = AdminEmailCompositionService.ValidateRecipientRequest(request);
        if (validationError != null)
        {
            return EmailAdminOperationResult<EmailRecipientsPreviewResponse>.Failure(validationError);
        }

        var recipients = await this.BuildRecipientsAsync(request, cancellationToken);

        return EmailAdminOperationResult<EmailRecipientsPreviewResponse>.Success(new EmailRecipientsPreviewResponse
        {
            TotalCount = recipients.Count,
            Items = recipients.Take(20).ToList()
        });
    }

    /// <summary> Планирует email-кампанию с проверкой запроса. </summary>
    public async Task<EmailAdminOperationResult<EmailCampaignScheduleResponse>> ScheduleCampaignAsync(
        EmailCampaignScheduleRequest request,
        string? createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var validationError = AdminEmailCompositionService.ValidateRecipientRequest(request)
            ?? AdminEmailCompositionService.ValidateCampaignRequest(request);
        if (validationError != null)
        {
            return EmailAdminOperationResult<EmailCampaignScheduleResponse>.Failure(validationError);
        }

        var recipients = await this.BuildRecipientsAsync(request, cancellationToken);
        if (recipients.Count == 0)
        {
            return EmailAdminOperationResult<EmailCampaignScheduleResponse>.Failure(
                "Не найдено получателей с email по выбранным условиям.");
        }

        var result = await emailCampaignService.CreateCampaignAsync(
            new EmailCampaignCreateDto
            {
                Subject = request.Subject,
                HtmlBody = request.HtmlBody,
                TargetMode = request.TargetMode,
                TargetUserId = request.UserId,
                TargetOrganizationTypeCode = request.OrganizationTypeCode,
                OnlyConfirmedEmail = request.OnlyConfirmedEmail,
                OnlyAdvertisingConsent = request.OnlyAdvertisingConsent,
                IntervalSeconds = request.IntervalSeconds,
                StartDelayMinutes = request.StartDelayMinutes,
                Recipients = recipients
                    .Select(x => new EmailCampaignRecipientCreateDto
                    {
                        UserId = x.UserId,
                        Email = x.Email,
                        DisplayName = x.DisplayName
                    })
                    .ToList()
            },
            createdByUserId);

        return EmailAdminOperationResult<EmailCampaignScheduleResponse>.Success(new EmailCampaignScheduleResponse
        {
            CampaignId = result.CampaignId,
            ScheduledCount = result.ScheduledCount,
            IntervalSeconds = result.IntervalSeconds,
            FirstSendAt = result.FirstSendAt,
            LastSendAt = result.LastSendAt
        });
    }

    /// <summary> Проверяет корректность запроса получателей и возвращает ошибку. </summary>
    private static string? ValidateRecipientRequest(EmailCampaignRecipientsRequest request)
    {
        if (!string.Equals(request.TargetMode?.Trim(), EmailCampaignTargetModeCodes.Email, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!EmailAddressValidation.TryValidate(request.Email, out var error))
        {
            return error;
        }

        if (request.OnlyConfirmedEmail || request.OnlyAdvertisingConsent)
        {
            return "Для произвольного адреса недоступны фильтры зарегистрированных пользователей.";
        }

        return null;
    }

    /// <summary> Проверяет корректность запроса планирования кампании и возвращает ошибку. </summary>
    private static string? ValidateCampaignRequest(EmailCampaignScheduleRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Trim().Length > 300)
        {
            return "Тема письма должна содержать от 1 до 300 символов.";
        }

        if (string.IsNullOrWhiteSpace(request.HtmlBody))
        {
            return "Укажите текст письма.";
        }

        if (request.IntervalSeconds < 5)
        {
            return "Интервал между письмами должен быть не меньше 5 секунд.";
        }

        return null;
    }

    /// <summary> Формирует список получателей рассылки по запросу. </summary>
    private async Task<List<EmailRecipientPreviewItem>> BuildRecipientsAsync(
        EmailCampaignRecipientsRequest request,
        CancellationToken cancellationToken)
    {
        var mode = (request.TargetMode ?? string.Empty).Trim().ToLowerInvariant();
        if (mode == EmailCampaignTargetModeCodes.Email)
        {
            var email = request.Email!.Trim();
            var normalizedEmail = email.ToUpperInvariant();
            var userId = await context.ApplicationUsers
                .AsNoTracking()
                .Where(user => !user.IsDeleted && user.NormalizedEmail == normalizedEmail)
                .Select(user => user.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return [new EmailRecipientPreviewItem { Email = email, DisplayName = email, UserId = userId }];
        }

        IQueryable<ApplicationUser> usersQuery = context.ApplicationUsers
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.Email != null && x.Email != "");

        if (request.OnlyConfirmedEmail)
        {
            usersQuery = usersQuery.Where(x => x.EmailConfirmed);
        }

        if (request.OnlyAdvertisingConsent)
        {
            usersQuery = usersQuery.Where(x => x.AdvertisingConsentAccepted);
        }

        if (mode == EmailCampaignTargetModeCodes.User.ToLowerInvariant())
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return [];
            }

            usersQuery = usersQuery.Where(x => x.Id == request.UserId);
        }
        else if (mode == EmailCampaignTargetModeCodes.OrganizationType.ToLowerInvariant())
        {
            if (string.IsNullOrWhiteSpace(request.OrganizationTypeCode))
            {
                return [];
            }

            var organizationTypeCode = request.OrganizationTypeCode.Trim();
            var ownerUserIds = await context.Organizations
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.PrimaryScenarioCode == organizationTypeCode)
                .Select(x => x.OwnerUserId)
                .ToListAsync(cancellationToken);

            var memberUserIds = await context.OrganizationMembers
                .AsNoTracking()
                .Where(x => !x.Organization.IsDeleted && x.Organization.PrimaryScenarioCode == organizationTypeCode)
                .Select(x => x.UserId)
                .ToListAsync(cancellationToken);

            var organizationUserIds = ownerUserIds
                .Concat(memberUserIds)
                .Distinct()
                .ToList();

            usersQuery = usersQuery.Where(x => organizationUserIds.Contains(x.Id));
        }
        else if (mode != EmailCampaignTargetModeCodes.All)
        {
            return [];
        }

        var recipients = await usersQuery
            .OrderBy(x => x.Email)
            .Select(x => new EmailRecipientPreviewItem
            {
                UserId = x.Id,
                Email = x.Email!,
                DisplayName = x.Email ?? x.UserName ?? x.PhoneNumber ?? x.Id,
                Phone = x.PhoneNumber,
                EmailConfirmed = x.EmailConfirmed,
                AdvertisingConsentAccepted = x.AdvertisingConsentAccepted
            })
            .ToListAsync(cancellationToken);

        return recipients
            .GroupBy(x => x.Email.Trim().ToUpperInvariant())
            .Select(x => x.First())
            .ToList();
    }
}
