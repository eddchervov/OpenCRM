using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Admin;
using OpenCRM.Services.Interfaces.Admin.Dtos;

namespace OpenCRM.Modules.Notifications;

/// <summary> Сервис администрирования site-уведомлений. </summary>
public sealed class AdminSiteNotificationService(OpenCRMContext context) : IAdminSiteNotificationService
{
    /// <summary> Зависимость доступа к данным приложения. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Возвращает список site-уведомлений для администратора. </summary>
    public async Task<IReadOnlyList<AdminSiteNotificationDto>> GetAsync(CancellationToken cancellationToken)
    {
        var notifications = await this._context.SiteNotifications.AsNoTracking()
            .Where(notification => !notification.IsDeleted)
            .OrderByDescending(notification => notification.CreatedAt)
            .Select(notification => new AdminSiteNotificationDto(
                notification.Id,
                notification.Title,
                notification.Body,
                notification.TypeCode,
                notification.IsTemplate,
                SiteNotificationAudiences.ToCodes(notification.AudienceMask),
                notification.CreatedAt,
                this._context.ApplicationUsers
                    .Where(user => user.Id == notification.RecipientUserId)
                    .Select(user => user.Email)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        var templates = notifications.Where(notification => notification.IsTemplate)
            .Select(notification => notification.TypeCode)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var typeCode in SiteNotificationTypes.Automatic)
        {
            if (templates.Contains(typeCode))
            {
                continue;
            }

            var (title, body) = SiteNotificationPublisher.GetDefaultText(typeCode);
            notifications.Add(new AdminSiteNotificationDto(
                AdminSiteNotificationService.GetDefaultTemplateId(typeCode),
                title,
                body,
                typeCode,
                true,
                Array.Empty<string>(),
                DateTime.MinValue,
                null));
        }

        return notifications;
    }

    /// <summary> Возвращает страницу истории уведомлений сайта. </summary>
    public async Task<AdminSiteNotificationHistoryPageDto> GetHistoryPageAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var query = this._context.SiteNotifications.AsNoTracking()
            .Where(notification => !notification.IsDeleted && !notification.IsTemplate);
        var totalCount = await query.CountAsync(cancellationToken);
        var pageCount = Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize));
        page = Math.Min(page, pageCount);

        var items = await query
            .OrderByDescending(notification => notification.CreatedAt)
            .ThenByDescending(notification => notification.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(notification => new AdminSiteNotificationDto(
                notification.Id,
                notification.Title,
                notification.Body,
                notification.TypeCode,
                notification.IsTemplate,
                SiteNotificationAudiences.ToCodes(notification.AudienceMask),
                notification.CreatedAt,
                this._context.ApplicationUsers
                    .Where(user => user.Id == notification.RecipientUserId)
                    .Select(user => user.Email)
                    .FirstOrDefault()))
            .ToListAsync(cancellationToken);

        return new AdminSiteNotificationHistoryPageDto
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary> Возвращает шаблоны уведомлений и стандартные значения по умолчанию. </summary>
    public async Task<IReadOnlyList<AdminSiteNotificationDto>> GetTemplatesAsync(CancellationToken cancellationToken)
    {
        var templates = await this._context.SiteNotifications.AsNoTracking()
            .Where(notification => !notification.IsDeleted && notification.IsTemplate)
            .OrderBy(notification => notification.TypeCode)
            .Select(notification => new AdminSiteNotificationDto(
                notification.Id,
                notification.Title,
                notification.Body,
                notification.TypeCode,
                notification.IsTemplate,
                SiteNotificationAudiences.ToCodes(notification.AudienceMask),
                notification.CreatedAt,
                null))
            .ToListAsync(cancellationToken);

        var existingTypeCodes = templates.Select(notification => notification.TypeCode)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var typeCode in SiteNotificationTypes.Automatic)
        {
            if (existingTypeCodes.Contains(typeCode))
            {
                continue;
            }

            var (title, body) = SiteNotificationPublisher.GetDefaultText(typeCode);
            templates.Add(new AdminSiteNotificationDto(
                AdminSiteNotificationService.GetDefaultTemplateId(typeCode),
                title,
                body,
                typeCode,
                true,
                Array.Empty<string>(),
                DateTime.MinValue,
                null));
        }

        return templates;
    }

    /// <summary> Ищет пользователей для адресной рассылки уведомлений. </summary>
    public async Task<IReadOnlyList<AdminSiteNotificationUserDto>> SearchUsersAsync(
        string? search,
        CancellationToken cancellationToken)
    {
        var normalizedSearch = search?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedSearch))
        {
            return Array.Empty<AdminSiteNotificationUserDto>();
        }

        normalizedSearch = normalizedSearch.ToUpperInvariant();
        var users = await this._context.ApplicationUsers.AsNoTracking()
            .Where(user => !user.IsDeleted && user.NormalizedEmail != null &&
                EF.Functions.ILike(user.NormalizedEmail, $"%{normalizedSearch}%"))
            .OrderBy(user => user.Email)
            .Take(25)
            .Select(user => new AdminSiteNotificationUserDto(user.Id, user.Email))
            .ToListAsync(cancellationToken);

        return users;
    }

    /// <summary> Удаляет site-уведомление, если оно существует. </summary>
    public async Task<bool> TryDeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var notification = await this._context.SiteNotifications
            .FirstOrDefaultAsync(item => item.Id == id && !item.IsDeleted, cancellationToken);
        if (notification == null)
        {
            return false;
        }

        notification.IsDeleted = true;
        await this._context.SaveChangesAsync(cancellationToken);

        return true;
    }

    /// <summary> Обновляет заголовок и текст шаблона уведомления. </summary>
    public async Task<AdminSiteNotificationDto?> UpdateTemplateAsync(
        Guid id,
        string title,
        string body,
        CancellationToken cancellationToken)
    {
        var template = await this._context.SiteNotifications
            .FirstOrDefaultAsync(item => item.Id == id && item.IsTemplate && !item.IsDeleted, cancellationToken);

        var typeCode = template?.TypeCode ?? SiteNotificationTypes.Automatic
            .FirstOrDefault(type => AdminSiteNotificationService.GetDefaultTemplateId(type) == id);

        if (typeCode == null)
        {
            return null;
        }

        if (template == null)
        {
            template = new SiteNotification
            {
                Id = id,
                TypeCode = typeCode,
                IsTemplate = true
            };
            this._context.SiteNotifications.Add(template);
        }

        template.Title = title;
        template.Body = body;
        await this._context.SaveChangesAsync(cancellationToken);

        return new AdminSiteNotificationDto(
            template.Id,
            template.Title,
            template.Body,
            template.TypeCode,
            template.IsTemplate,
            Array.Empty<string>(),
            template.CreatedAt,
            null);
    }

    /// <summary> Отправляет site-уведомление выбранным получателям. </summary>
    public async Task<string?> SendAsync(AdminSiteNotificationSendDto dto, CancellationToken cancellationToken)
    {
        if (dto.RecipientScope is not ("all" or "selected"))
        {
            return "Выберите допустимый способ выбора пользователей.";
        }

        var audiences = dto.Audiences ?? [];
        if (!AdminSiteNotificationService.HasValidAudiences(audiences))
        {
            return "Выберите хотя бы одну допустимую аудиторию.";
        }

        var userIds = dto.UserIds?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (dto.RecipientScope == "selected" && (userIds.Length == 0 || userIds.Length > 500))
        {
            return "Выберите от одного до 500 пользователей.";
        }

        if (dto.RecipientScope == "all" && userIds.Length != 0)
        {
            return "Для рассылки всем пользователям не передавайте список пользователей.";
        }

        var audienceMask = 0;
        foreach (var audience in audiences)
        {
            audienceMask |= audience switch
            {
                "all" => SiteNotificationAudiences.All,
                "admins" => SiteNotificationAudiences.Admins,
                _ => SiteNotificationAudiences.OrganizationOwners
            };
        }

        if (dto.RecipientScope == "all")
        {
            this._context.SiteNotifications.Add(new SiteNotification
            {
                Title = dto.Title,
                Body = dto.Body,
                AudienceMask = audienceMask
            });
            await this._context.SaveChangesAsync(cancellationToken);

            return null;
        }

        var users = this.GetSelectedRecipients(userIds, audienceMask);
        var recipients = await users.Select(user => user.Id).ToListAsync(cancellationToken);
        var existingUserCount = await this._context.ApplicationUsers.AsNoTracking()
            .CountAsync(user => !user.IsDeleted && userIds.Contains(user.Id), cancellationToken);

        if (existingUserCount != userIds.Length)
        {
            return "Список содержит отсутствующих или удалённых пользователей.";
        }

        if (recipients.Count != userIds.Length)
        {
            return "Выбранные пользователи не входят в указанную аудиторию.";
        }

        this._context.SiteNotifications.AddRange(recipients.Select(recipientUserId => new SiteNotification
        {
            Title = dto.Title,
            Body = dto.Body,
            AudienceMask = audienceMask,
            RecipientUserId = recipientUserId
        }));

        await this._context.SaveChangesAsync(cancellationToken);

        return null;
    }

    /// <summary> Возвращает запрос выбранных получателей по идентификаторам и аудитории. </summary>
    private IQueryable<ApplicationUser> GetSelectedRecipients(string[] userIds, int audienceMask)
    {
        var users = this._context.ApplicationUsers.AsNoTracking()
            .Where(user => !user.IsDeleted && userIds.Contains(user.Id));

        if ((audienceMask & SiteNotificationAudiences.All) == 0)
        {
            var audienceUserIds = this._context.ApplicationUsers.AsNoTracking()
                .Where(user => !user.IsDeleted &&
                    (((audienceMask & SiteNotificationAudiences.Admins) != 0 &&
                        this._context.UserRoles.Any(userRole => userRole.UserId == user.Id &&
                            (this._context.Roles.Any(role => role.Id == userRole.RoleId && role.Name == "System") ||
                                this._context.AdminRolePermissions.Any(permission =>
                                    permission.RoleId == userRole.RoleId &&
                                    permission.PermissionCode == AdminAuthorizationPolicies.Access)))) ||
                    ((audienceMask & SiteNotificationAudiences.OrganizationOwners) != 0 &&
                        this._context.Organizations.Any(organization =>
                            organization.OwnerUserId == user.Id && !organization.IsDeleted))))
                .Select(user => user.Id);

            users = users.Where(user => audienceUserIds.Contains(user.Id));
        }

        return users;
    }

    /// <summary> Проверяет корректность набора аудиторий получателей. </summary>
    private static bool HasValidAudiences(IReadOnlyCollection<string> audiences)
    {
        return audiences.Count != 0 &&
            audiences.All(audience => audience is "all" or "admins" or "organization-owners") &&
            (!audiences.Contains("all") || audiences.Count == 1);
    }

    /// <summary> Возвращает идентификатор шаблона по умолчанию для типа уведомления. </summary>
    private static Guid GetDefaultTemplateId(string typeCode)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"opencrm-site-notification-template:{typeCode}"));

        return new Guid(hash);
    }
}
