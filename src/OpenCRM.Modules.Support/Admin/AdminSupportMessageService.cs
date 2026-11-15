using OpenCRM.Services.Interfaces.Home.Dtos;
using Microsoft.EntityFrameworkCore;
using System.Text.Encodings.Web;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;
using OpenCRM.Services.Interfaces.Emails;

namespace OpenCRM.Modules.Support.Admin;

/// <summary> Сервис обработки обращений поддержки для администраторов. </summary>
public class AdminSupportMessageService(
    OpenCRMContext opencrmContext,
    IEmailNotificationService emailNotifications,
    Helpers.ProjectBrand? brand = null) : IAdminSupportMessageService
{
    /// <summary> Возвращает страницу обращений поддержки с фильтрами. </summary>
    public async Task<AdminSupportMessagesPageDto> GetPageAsync(string? status, string? search, string? source, int page)
    {
        var query = opencrmContext.SupportMessages.AsNoTracking();
        if (source == "users")
        {
            query = query.Where(x => x.UserId != null);
        }
        else if (source == "guests")
        {
            query = query.Where(x => x.UserId == null);
        }

        if (!string.IsNullOrWhiteSpace(status) && SupportMessageStatusCodes.All.Contains(status))
        {
            query = query.Where(x => x.StatusCode == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x =>
                x.Topic.ToLower().Contains(normalizedSearch) ||
                x.Text.ToLower().Contains(normalizedSearch) ||
                x.Entries.Any(entry => entry.Text.ToLower().Contains(normalizedSearch)) ||
                (x.Email != null && x.Email.ToLower().Contains(normalizedSearch)) ||
                (x.Name != null && x.Name.ToLower().Contains(normalizedSearch)) ||
                (x.Organization != null && x.Organization.Name.ToLower().Contains(normalizedSearch)));
        }

        const int pageSize = 50;
        page = Math.Max(1, page);
        var totalCount = await query.CountAsync();
        var messages = await query
            .OrderBy(x => x.StatusCode == SupportMessageStatusCodes.New ? 0 : 1)
            .ThenByDescending(x => x.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new AdminSupportMessageDto
            {
                Id = x.Id,
                TypeCode = x.TypeCode,
                TypeName = GetSupportTypeName(x.TypeCode),
                StatusCode = x.StatusCode,
                StatusName = GetSupportStatusName(x.StatusCode),
                UserId = x.UserId,
                OrganizationId = x.OrganizationId,
                OrganizationName = x.Organization == null ? null : x.Organization.Name,
                UserEmail = x.UserId == null
                    ? null
                    : opencrmContext.ApplicationUsers
                        .Where(user => user.Id == x.UserId)
                        .Select(user => user.Email)
                        .FirstOrDefault(),
                Name = x.Name,
                Email = x.Email,
                Phone = x.Phone,
                Topic = x.Topic,
                Text = x.Text,
                SourcePage = x.SourcePage,
                AdminComment = x.AdminComment,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .ToListAsync();

        var messageIds = messages.Select(x => x.Id).ToArray();
        var entries = await opencrmContext.SupportMessageEntries.AsNoTracking()
            .Where(x => messageIds.Contains(x.SupportMessageId))
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.SupportMessageId,
                Entry = new AdminSupportMessageEntryDto
                {
                    Id = x.Id,
                    IsStaff = x.IsStaff,
                    Kind = x.Kind,
                    Text = x.Text,
                    CreatedAt = x.CreatedAt,
                    Attachments = x.Attachments.OrderBy(a => a.SortOrder)
                        .Select(a => new SupportAttachmentDto { Id = a.Id, FileName = a.FileName, SizeBytes = a.SizeBytes }).ToList()
                }
            })
            .ToListAsync();

        await PopulateDeliveryStatusAsync(entries.Select(item => item.Entry));

        foreach (var message in messages)
        {
            message.Entries =
            [
                new AdminSupportMessageEntryDto
                {
                    Kind = "message",
                    Text = message.Text,
                    CreatedAt = message.CreatedAt
                },
                .. entries.Where(x => x.SupportMessageId == message.Id).Select(x => x.Entry)
            ];
        }

        await this.PopulateAttachmentsAsync(messages);

        var counters = await opencrmContext.SupportMessages
            .AsNoTracking()
            .GroupBy(x => x.StatusCode)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync();

        return new AdminSupportMessagesPageDto
        {
            Status = status,
            Search = search,
            Items = messages,
            TotalCount = totalCount,
            PageSize = pageSize,
            NewCount = counters.FirstOrDefault(x => x.Status == SupportMessageStatusCodes.New)?.Count ?? 0,
            InProgressCount = counters.FirstOrDefault(x => x.Status == SupportMessageStatusCodes.InProgress)?.Count ?? 0,
            ClosedCount = counters.FirstOrDefault(x => x.Status == SupportMessageStatusCodes.Closed)?.Count ?? 0,
            SpamCount = counters.FirstOrDefault(x => x.Status == SupportMessageStatusCodes.Spam)?.Count ?? 0
        };
    }

    /// <summary> Возвращает обращение поддержки по идентификатору. </summary>
    public async Task<AdminSupportMessageDto?> GetAsync(int id)
    {
        var message = await opencrmContext.SupportMessages.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AdminSupportMessageDto
            {
                Id = x.Id,
                TypeCode = x.TypeCode,
                TypeName = GetSupportTypeName(x.TypeCode),
                StatusCode = x.StatusCode,
                StatusName = GetSupportStatusName(x.StatusCode),
                UserId = x.UserId,
                UserEmail = x.UserId == null ? null : opencrmContext.ApplicationUsers
                    .Where(user => user.Id == x.UserId)
                    .Select(user => user.Email)
                    .FirstOrDefault(),
                OrganizationId = x.OrganizationId,
                OrganizationName = x.Organization == null ? null : x.Organization.Name,
                Name = x.Name,
                Email = x.Email,
                Phone = x.Phone,
                Topic = x.Topic,
                Text = x.Text,
                SourcePage = x.SourcePage,
                AdminComment = x.AdminComment,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt
            })
            .SingleOrDefaultAsync();

        if (message == null)
        {
            return null;
        }

        var entries = await opencrmContext.SupportMessageEntries.AsNoTracking()
            .Where(x => x.SupportMessageId == id)
            .OrderBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(x => new AdminSupportMessageEntryDto
            {
                Id = x.Id,
                IsStaff = x.IsStaff,
                Kind = x.Kind,
                Text = x.Text,
                CreatedAt = x.CreatedAt,
                Attachments = x.Attachments.OrderBy(a => a.SortOrder)
                    .Select(a => new SupportAttachmentDto { Id = a.Id, FileName = a.FileName, SizeBytes = a.SizeBytes }).ToList()
            })
            .ToListAsync();

        await PopulateDeliveryStatusAsync(entries);

        message.Entries =
        [
            new AdminSupportMessageEntryDto
            {
                Kind = "message",
                Text = message.Text,
                CreatedAt = message.CreatedAt
            },
            .. entries
        ];

        await this.PopulateAttachmentsAsync([message]);

        return message;
    }

    /// <summary> Обновляет статус и комментарий администратора обращения. </summary>
    public async Task<AdminSupportMessageUpdateResultDto> UpdateAsync(int id, string? statusCode, string? adminComment)
    {
        var message = await opencrmContext.SupportMessages.FirstOrDefaultAsync(x => x.Id == id);
        if (message == null)
        {
            return new AdminSupportMessageUpdateResultDto { NotFound = true };
        }

        if (string.IsNullOrWhiteSpace(statusCode) || !SupportMessageStatusCodes.All.Contains(statusCode))
        {
            return new AdminSupportMessageUpdateResultDto
            {
                ErrorMessage = "Некорректный статус обращения."
            };
        }

        var statusChanged = message.StatusCode != statusCode;
        message.StatusCode = statusCode;
        message.AdminComment = string.IsNullOrWhiteSpace(adminComment) ? null : adminComment.Trim();
        message.UpdatedAt = DateTime.UtcNow;
        message.ClosedAt = statusCode is SupportMessageStatusCodes.Closed or SupportMessageStatusCodes.Spam
            ? DateTime.UtcNow
            : null;

        if (statusChanged)
        {
            opencrmContext.SupportMessageEntries.Add(new SupportMessageEntry
            {
                SupportMessageId = message.Id,
                IsStaff = true,
                Kind = "status",
                Text = GetSupportStatusName(statusCode),
                CreatedAt = message.UpdatedAt
            });
        }

        await opencrmContext.SaveChangesAsync();

        return new AdminSupportMessageUpdateResultDto
        {
            Succeeded = true,
            SuccessMessage = $"Обращение #{message.Id} обновлено."
        };
    }

    /// <summary> Добавляет ответ администратора на обращение поддержки. </summary>
    public Task<AdminSupportMessageUpdateResultDto> ReplyAsync(int id, string authorUserId, string? text)
    {
        var executionStrategy = opencrmContext.Database.CreateExecutionStrategy();

        return executionStrategy.ExecuteAsync(async () =>
        {
            opencrmContext.ChangeTracker.Clear();
            await using var transaction = await opencrmContext.Database.BeginTransactionAsync();
            var message = await opencrmContext.SupportMessages.FirstOrDefaultAsync(item => item.Id == id);
            if (message == null)
            {
                return new AdminSupportMessageUpdateResultDto { NotFound = true };
            }

            if (message.StatusCode == SupportMessageStatusCodes.Spam)
            {
                return new AdminSupportMessageUpdateResultDto
                {
                    ErrorMessage = "Сначала измените статус обращения со спама."
                };
            }

            if (string.IsNullOrWhiteSpace(text) || text.Trim().Length > 4000)
            {
                return new AdminSupportMessageUpdateResultDto
                {
                    ErrorMessage = "Ответ должен содержать от 1 до 4000 символов."
                };
            }

            if (message.UserId == null && string.IsNullOrWhiteSpace(message.Email))
            {
                return new AdminSupportMessageUpdateResultDto
                {
                    ErrorMessage = "У гостя нет email для ответа."
                };
            }

            var now = DateTime.UtcNow;
            var reply = new SupportMessageEntry
            {
                SupportMessageId = id,
                AuthorUserId = authorUserId,
                IsStaff = true,
                Kind = message.UserId == null ? "guest_email_reply" : "message",
                Text = text.Trim(),
                CreatedAt = now
            };
            opencrmContext.SupportMessageEntries.Add(reply);

            if (message.StatusCode is SupportMessageStatusCodes.New or SupportMessageStatusCodes.Closed)
            {
                message.StatusCode = SupportMessageStatusCodes.InProgress;
                message.ClosedAt = null;
                opencrmContext.SupportMessageEntries.Add(new SupportMessageEntry
                {
                    SupportMessageId = id,
                    IsStaff = true,
                    Kind = "status",
                    Text = GetSupportStatusName(message.StatusCode),
                    CreatedAt = now
                });
            }

            message.UpdatedAt = now;
            await opencrmContext.SaveChangesAsync();

            if (message.UserId == null)
            {
                var subject = $"Ответ на обращение #{id} — {brand?.DisplayName ?? Helpers.ProjectBrand.DefaultName}";
                var body = $"<p>Здравствуйте!</p><p>Ответ на обращение «{HtmlEncoder.Default.Encode(message.Topic)}»:</p>"
                    + $"<p style=\"white-space:pre-wrap\">{HtmlEncoder.Default.Encode(reply.Text)}</p>";

                try
                {
                    await emailNotifications.QueueAsync(message.Email!, subject, body, $"support-reply:{reply.Id}");
                }
                catch (ArgumentException)
                {
                    return new AdminSupportMessageUpdateResultDto
                    {
                        ErrorMessage = "У гостя указан некорректный email для ответа."
                    };
                }
            }

            await transaction.CommitAsync();

            return new AdminSupportMessageUpdateResultDto
            {
                Succeeded = true,
                SuccessMessage = message.UserId == null ? "Ответ поставлен в очередь отправки." : "Ответ сохранён."
            };
        });
    }

    private async Task PopulateAttachmentsAsync(IReadOnlyList<AdminSupportMessageDto> messages)
    {
        var ids = messages.Select(x => x.Id).ToArray();
        var attachments = await opencrmContext.SupportAttachments.AsNoTracking()
            .Where(x => ids.Contains(x.SupportMessageId) && x.EntryId == null)
            .OrderBy(x => x.SortOrder)
            .Select(x => new
            {
                x.SupportMessageId,
                Attachment = new SupportAttachmentDto { Id = x.Id, FileName = x.FileName, SizeBytes = x.SizeBytes }
            })
            .ToListAsync();

        foreach (var message in messages)
        {
            message.Entries[0].Attachments = attachments
                .Where(x => x.SupportMessageId == message.Id)
                .Select(x => x.Attachment)
                .ToList();
        }
    }

    /// <summary> Заполняет статусы доставки уведомлений обращений. </summary>
    private async Task PopulateDeliveryStatusAsync(IEnumerable<AdminSupportMessageEntryDto> entries)
    {
        var guestReplies = entries.Where(entry => entry.Kind == "guest_email_reply").ToArray();
        if (guestReplies.Length == 0)
        {
            return;
        }

        var keys = guestReplies.Select(entry => $"support-reply:{entry.Id}").ToArray();
        var deliveries = await opencrmContext.EmailMessages.AsNoTracking()
            .Where(message => message.NotificationKey != null && keys.Contains(message.NotificationKey))
            .Select(message => new { message.NotificationKey, message.StatusCode, message.ErrorMessage })
            .ToDictionaryAsync(message => message.NotificationKey!);

        foreach (var reply in guestReplies)
        {
            if (!deliveries.TryGetValue($"support-reply:{reply.Id}", out var delivery))
            {
                continue;
            }

            reply.DeliveryStatus = delivery.StatusCode;
            reply.DeliveryError = delivery.ErrorMessage;
        }
    }

    /// <summary> Возвращает название типа обращения по коду. </summary>
    private static string GetSupportTypeName(string typeCode) => typeCode switch
    {
        SupportMessageTypeCodes.Suggestion => "Предложение",
        SupportMessageTypeCodes.Bug => "Ошибка",
        SupportMessageTypeCodes.Billing => "Тарифы и оплата",
        SupportMessageTypeCodes.Cooperation => "Сотрудничество",
        _ => "Вопрос"
    };

    /// <summary> Возвращает название статуса обращения по коду. </summary>
    private static string GetSupportStatusName(string statusCode) => statusCode switch
    {
        SupportMessageStatusCodes.InProgress => "В работе",
        SupportMessageStatusCodes.Closed => "Закрыто",
        SupportMessageStatusCodes.Spam => "Спам",
        _ => "Новое"
    };
}
