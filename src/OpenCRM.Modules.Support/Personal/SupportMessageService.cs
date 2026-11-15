using OpenCRM.Services.Interfaces.Emails;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Home;
using OpenCRM.Services.Interfaces.Home.Dtos;
using System.Security.Cryptography;
using System.Text;

namespace OpenCRM.Modules.Support.Personal;

/// <summary> Сервис работы с обращениями пользователей в поддержку. </summary>
public class SupportMessageService(
    OpenCRMContext opencrmContext,
    IEmailNotificationService emailSender,
    IConfiguration configuration,
    SupportAttachmentService attachmentService,
    ILogger<SupportMessageService>? logger = null) : ISupportMessageService
{
    /// <summary> Создаёт новое обращение в поддержку. </summary>
    public async Task<SupportMessageCreateResultDto> CreateAsync(SupportMessageCreateDto dto)
    {
        var notificationKey = $"support:{Guid.NewGuid():N}";
        var attachments = await attachmentService.SaveAsync(dto.Attachments);
        int? createdMessageId = null;
        var isCompleted = false;
        try
        {
            var result = await opencrmContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                opencrmContext.ChangeTracker.Clear();
                await using var transaction = await opencrmContext.Database.BeginTransactionAsync();
                if ((createdMessageId.HasValue && await opencrmContext.SupportMessages.AsNoTracking()
                        .AnyAsync(message => message.Id == createdMessageId.Value))
                    || await opencrmContext.EmailMessages.AnyAsync(x => x.NotificationKey == notificationKey))
                {
                    return new SupportMessageCreateResultDto
                    {
                        Succeeded = true,
                        MessageId = createdMessageId,
                        SuccessMessage = "Сообщение принято."
                    };
                }

                var result = await this.CreateCoreAsync(dto, notificationKey, attachments);
                createdMessageId = result.MessageId;
                await transaction.CommitAsync();

                return result;
            });

            isCompleted = result.Succeeded;

            return result;
        }
        finally
        {
            if (!isCompleted)
            {
                await attachmentService.CleanupAsync(attachments);
            }
        }
    }

    /// <summary> Возвращает входящие обращения пользователя поддержки. </summary>
    public async Task<SupportInboxDto> GetInboxAsync(
        string userId,
        int? selectedId,
        CancellationToken cancellationToken = default)
    {
        var organizations = await this.GetAvailableOrganizations(userId)
            .OrderBy(x => x.Name)
            .Select(x => new SupportOrganizationOptionDto { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);

        var conversations = await opencrmContext.SupportMessages.AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.UpdatedAt)
            .Select(x => new SupportConversationDto
            {
                Id = x.Id,
                Topic = x.Topic,
                TypeCode = x.TypeCode,
                StatusCode = x.StatusCode,
                OrganizationName = x.Organization == null ? null : x.Organization.Name,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var selected = selectedId == null
            ? conversations.FirstOrDefault()
            : conversations.FirstOrDefault(x => x.Id == selectedId);

        if (selected != null)
        {
            var message = await opencrmContext.SupportMessages.AsNoTracking()
                .Where(x => x.Id == selected.Id && x.UserId == userId)
                .Select(x => new
                {
                    x.Text,
                    x.CreatedAt,
                    Attachments = x.Attachments.Where(a => a.EntryId == null).OrderBy(a => a.SortOrder)
                        .Select(a => new SupportAttachmentDto { Id = a.Id, FileName = a.FileName, SizeBytes = a.SizeBytes }).ToList()
                })
                .SingleAsync(cancellationToken);

            var entries = await opencrmContext.SupportMessageEntries.AsNoTracking()
                .Where(x => x.SupportMessageId == selected.Id)
                .OrderBy(x => x.CreatedAt)
                .ThenBy(x => x.Id)
                .Select(x => new SupportConversationEntryDto
                {
                    Attachments = x.Attachments.OrderBy(a => a.SortOrder)
                        .Select(a => new SupportAttachmentDto { Id = a.Id, FileName = a.FileName, SizeBytes = a.SizeBytes }).ToList(),
                    IsStaff = x.IsStaff,
                    Kind = x.Kind,
                    Text = x.Text,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            entries.Insert(0, new SupportConversationEntryDto
            {
                Text = message.Text,
                CreatedAt = message.CreatedAt,
                Attachments = message.Attachments
            });
            selected.Entries = entries;
        }

        return new SupportInboxDto
        {
            Organizations = organizations,
            Conversations = conversations,
            Selected = selected
        };
    }

    /// <summary> Отправляет текстовый ответ пользователя. </summary>
    public Task<SupportMessageReplyResult> ReplyAsync(
        string userId, int messageId, string text, CancellationToken cancellationToken = default) =>
        this.ReplyAsync(userId, messageId, text, cancellationToken, []);

    /// <summary> Отправляет ответ пользователя с вложениями на обращение поддержки. </summary>
    public async Task<SupportMessageReplyResult> ReplyAsync(
        string userId,
        int messageId,
        string text,
        CancellationToken cancellationToken,
        IReadOnlyList<SupportAttachmentUploadDto> attachments)
    {
        var message = await opencrmContext.SupportMessages
            .FirstOrDefaultAsync(x => x.Id == messageId && x.UserId == userId, cancellationToken);
        if (message == null)
        {
            return SupportMessageReplyResult.NotFound;
        }

        if (message.StatusCode == SupportMessageStatusCodes.Spam)
        {
            return SupportMessageReplyResult.Forbidden;
        }

        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 4000)
        {
            return SupportMessageReplyResult.InvalidText;
        }

        var savedAttachments = await attachmentService.SaveAsync(attachments);
        foreach (var attachment in savedAttachments)
        {
            attachment.SupportMessageId = message.Id;
        }

        var now = DateTime.UtcNow;
        opencrmContext.SupportMessageEntries.Add(new SupportMessageEntry
        {
            SupportMessageId = message.Id,
            AuthorUserId = userId,
            Attachments = savedAttachments,
            Text = text.Trim(),
            CreatedAt = now
        });

        if (message.StatusCode == SupportMessageStatusCodes.Closed)
        {
            message.StatusCode = SupportMessageStatusCodes.InProgress;
            message.ClosedAt = null;
            opencrmContext.SupportMessageEntries.Add(new SupportMessageEntry
            {
                SupportMessageId = message.Id,
                Kind = "status",
                Text = "Обращение снова в работе",
                CreatedAt = now
            });
        }

        message.UpdatedAt = now;
        try
        {
            await opencrmContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await attachmentService.CleanupAsync(savedAttachments);

            throw;
        }

        return SupportMessageReplyResult.Succeeded;
    }

    /// <summary> Проверяет доступ пользователя к организации. </summary>
    public Task<bool> HasOrganizationAccessAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        return this.GetAvailableOrganizations(userId)
            .AnyAsync(organization => organization.Id == organizationId, cancellationToken);
    }

    /// <summary> Возвращает запрос доступных пользователю организаций. </summary>
    private IQueryable<Organization> GetAvailableOrganizations(string userId)
    {
        return opencrmContext.Organizations.AsNoTracking()
            .Where(organization => !organization.IsDeleted &&
                (organization.OwnerUserId == userId || opencrmContext.OrganizationMembers.Any(member =>
                    member.OrganizationId == organization.Id && member.UserId == userId)));
    }

    /// <summary> Выполняет создание обращения с отправкой уведомления. </summary>
    private async Task<SupportMessageCreateResultDto> CreateCoreAsync(
        SupportMessageCreateDto dto, string notificationKey, List<SupportAttachment> attachments)
    {
        var now = DateTime.UtcNow;
        var ipHash = HashValue(dto.ClientIp);
        var email = NormalizeOptional(dto.Email);
        var normalizedEmail = email?.ToUpperInvariant();
        var topic = NormalizeRequired(dto.Topic);
        var text = NormalizeRequired(dto.Text);

        if (dto.OrganizationId is Guid organizationId &&
            (dto.UserId == null || !await this.HasOrganizationAccessAsync(dto.UserId, organizationId)))
        {
            return new SupportMessageCreateResultDto { ErrorMessage = "Выбранная организация недоступна." };
        }

        var recentFromIp = await opencrmContext.SupportMessages
            .AsNoTracking()
            .CountAsync(x => x.IpHash == ipHash && x.CreatedAt >= now.AddMinutes(-15));

        var recentFromEmail = normalizedEmail == null
            ? 0
            : await opencrmContext.SupportMessages
                .AsNoTracking()
                .CountAsync(x => x.Email != null
                    && x.Email.ToUpper() == normalizedEmail
                    && x.CreatedAt >= now.AddMinutes(-15));

        if (recentFromIp >= 3 || recentFromEmail >= 3)
        {
            logger?.LogWarning("Contact form rejected: sender volume limit reached.");

            return new SupportMessageCreateResultDto
            {
                ErrorMessage = "Слишком много сообщений за короткое время. Попробуйте позже."
            };
        }

        var message = new SupportMessage
        {
            Attachments = attachments,
            TypeCode = dto.TypeCode,
            StatusCode = SupportMessageStatusCodes.New,
            UserId = dto.UserId,
            OrganizationId = dto.OrganizationId,
            Name = NormalizeOptional(dto.Name),
            Email = email,
            Phone = SupportContactRules.NormalizePhone(dto.Phone),
            Topic = topic,
            Text = text,
            SourcePage = NormalizeOptional(dto.SourcePage) ?? "/personal/support",
            IpHash = ipHash,
            UserAgent = NormalizeOptional(dto.UserAgent) is { } userAgent
                ? Truncate(userAgent, 512)
                : null,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var attachment in attachments)
        {
            attachment.SupportMessage = message;
            attachment.SupportMessageId = 0;
            attachment.Entry = null;
            attachment.EntryId = null;
        }

        opencrmContext.SupportMessages.Add(message);
        await opencrmContext.SaveChangesAsync();
        await this.NotifySupportAsync(message, notificationKey);

        return new SupportMessageCreateResultDto
        {
            Succeeded = true,
            MessageId = message.Id,
            SuccessMessage = "Обращение сохранено. Ответ появится в разделе поддержки."
        };
    }

    /// <summary> Отправляет уведомление о новом обращении в поддержку. </summary>
    private Task NotifySupportAsync(SupportMessage message, string notificationKey)
    {
        var notificationEmail = configuration["Support:NotificationEmail"];
        var transport = configuration["Notifications:Transport"];
        if (string.IsNullOrWhiteSpace(notificationEmail)
            || string.Equals(transport, "Disabled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transport, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        return emailSender.QueueTemplateAsync(
            notificationEmail, $"Новое обращение #{message.Id}: {message.Topic}", "system/support-message",
            new
            {
                messageId = message.Id.ToString(),
                typeName = GetSupportTypeName(message.TypeCode),
                name = message.Name,
                email = message.Email,
                phone = message.Phone,
                topic = message.Topic,
                text = message.Text
            },
            idempotencyKey: notificationKey);
    }

    /// <summary> Вычисляет хеш значения для обезличивания данных. </summary>
    private static string HashValue(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim().ToLowerInvariant()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary> Нормализует обязательную строку. </summary>
    private static string NormalizeRequired(string value) => value.Trim();

    /// <summary> Нормализует необязательную строку. </summary>
    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary> Обрезает строку до максимальной длины. </summary>
    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary> Возвращает название типа обращения по коду. </summary>
    private static string GetSupportTypeName(string typeCode) => typeCode switch
    {
        SupportMessageTypeCodes.Suggestion => "Предложение",
        SupportMessageTypeCodes.Bug => "Ошибка",
        SupportMessageTypeCodes.Billing => "Тарифы и оплата",
        SupportMessageTypeCodes.Cooperation => "Сотрудничество",
        _ => "Вопрос"
    };
}
