using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Communications.Contracts;
using OpenCRM.Modules.Communications.Contracts.Dtos;

namespace OpenCRM.Modules.Communications;

internal sealed partial class OrganizationMessageTemplateService(
    OpenCRMContext context,
    ConversationAccess access) : IOrganizationMessageTemplateService
{
    private static readonly Regex s_placeholderRegex = PlaceholderRegex();
    private static readonly HashSet<string> s_allowedPlaceholders =
    [
        "organization.name",
        "client.name",
        "maintenance.date",
        "maintenance.time",
        "maintenance.vehicle"
    ];

    public async Task<IReadOnlyList<MessageTemplateGroupDto>> GetGroupsAsync(
        string userId,
        Guid organizationId,
        bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureTemplatesViewAsync(userId, organizationId);
        if (includeArchived)
        {
            await access.EnsurePermissionAsync(userId, organizationId, "messages.templates.manage");
        }

        var groups = await context.MessageTemplateGroups.AsNoTracking()
            .Where(group => group.OrganizationId == organizationId && (includeArchived || !group.IsArchived))
            .OrderBy(group => group.SortOrder)
            .ThenBy(group => group.Name)
            .Select(group => new MessageTemplateGroupDto
            {
                Id = group.Id,
                Name = group.Name,
                SortOrder = group.SortOrder,
                Version = group.Version,
                IsArchived = group.IsArchived,
                Templates = context.MessageTemplates.AsNoTracking()
                    .Where(template => template.OrganizationId == organizationId
                        && template.GroupId == group.Id
                        && (includeArchived || !template.IsArchived))
                    .OrderBy(template => template.SortOrder)
                    .ThenBy(template => template.Name)
                    .Select(template => new MessageTemplateDto
                    {
                        Id = template.Id,
                        GroupId = template.GroupId,
                        Name = template.Name,
                        Body = template.Body,
                        SortOrder = template.SortOrder,
                        Version = template.Version,
                        IsArchived = template.IsArchived
                    })
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        return groups;
    }

    public async Task<MessageTemplateGroupDto> SaveGroupAsync(
        string userId,
        Guid organizationId,
        SaveMessageTemplateGroupDto request,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureTemplatesManageAsync(userId, organizationId);
        var name = request.Name.Trim();
        if (name.Length is < 1 or > 100)
        {
            throw new ArgumentException("Название группы должно содержать от 1 до 100 символов.", nameof(request));
        }

        var now = DateTime.UtcNow;
        MessageTemplateGroup group;
        if (!request.Id.HasValue)
        {
            group = new MessageTemplateGroup
            {
                OrganizationId = organizationId,
                Name = name,
                SortOrder = request.SortOrder,
                IsArchived = request.IsArchived,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.MessageTemplateGroups.Add(group);
        }
        else
        {
            var expectedVersion = request.ExpectedVersion
                ?? throw new ArgumentException("Для изменения группы требуется её версия.", nameof(request));
            var rowsChanged = await context.MessageTemplateGroups
                .Where(item => item.Id == request.Id.Value
                    && item.OrganizationId == organizationId
                    && item.Version == expectedVersion)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.Name, name)
                    .SetProperty(item => item.SortOrder, request.SortOrder)
                    .SetProperty(item => item.IsArchived, request.IsArchived)
                    .SetProperty(item => item.Version, item => item.Version + 1)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);
            if (rowsChanged != 1)
            {
                throw new InvalidOperationException("Группа была изменена или удалена другим пользователем. Обновите список.");
            }

            group = await context.MessageTemplateGroups.AsNoTracking()
                .SingleAsync(item => item.Id == request.Id.Value && item.OrganizationId == organizationId, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        return new MessageTemplateGroupDto
        {
            Id = group.Id,
            Name = group.Name,
            SortOrder = group.SortOrder,
            Version = group.Version,
            IsArchived = group.IsArchived
        };
    }

    public async Task<bool> ArchiveGroupAsync(
        string userId,
        Guid organizationId,
        Guid groupId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureTemplatesManageAsync(userId, organizationId);
        var now = DateTime.UtcNow;
        var rowsChanged = await context.MessageTemplateGroups
            .Where(item => item.Id == groupId && item.OrganizationId == organizationId
                && item.Version == expectedVersion && !item.IsArchived)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.IsArchived, true)
                .SetProperty(item => item.Version, item => item.Version + 1)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (rowsChanged != 1)
        {
            throw new InvalidOperationException("Группа уже изменена. Обновите список перед архивированием.");
        }

        return true;
    }

    public async Task<MessageTemplateDto> SaveTemplateAsync(
        string userId,
        Guid organizationId,
        SaveMessageTemplateDto request,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureTemplatesManageAsync(userId, organizationId);
        var name = request.Name.Trim();
        var body = request.Body.Trim();
        if (name.Length is < 1 or > 100 || body.Length is < 1 or > 4096)
        {
            throw new ArgumentException("Шаблон должен иметь название и текст не длиннее 4096 символов.", nameof(request));
        }

        ValidatePlaceholders(body);
        if (!await context.MessageTemplateGroups.AsNoTracking()
            .AnyAsync(group => group.Id == request.GroupId && group.OrganizationId == organizationId && !group.IsArchived, cancellationToken))
        {
            throw new ArgumentException("Активная группа шаблонов не найдена.", nameof(request));
        }

        var now = DateTime.UtcNow;
        MessageTemplate template;
        if (!request.Id.HasValue)
        {
            template = new MessageTemplate
            {
                OrganizationId = organizationId,
                GroupId = request.GroupId,
                Name = name,
                Body = body,
                SortOrder = request.SortOrder,
                IsArchived = request.IsArchived,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.MessageTemplates.Add(template);
        }
        else
        {
            var expectedVersion = request.ExpectedVersion
                ?? throw new ArgumentException("Для изменения шаблона требуется его версия.", nameof(request));
            var rowsChanged = await context.MessageTemplates
                .Where(item => item.Id == request.Id.Value
                    && item.OrganizationId == organizationId
                    && item.Version == expectedVersion)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(item => item.GroupId, request.GroupId)
                    .SetProperty(item => item.Name, name)
                    .SetProperty(item => item.Body, body)
                    .SetProperty(item => item.SortOrder, request.SortOrder)
                    .SetProperty(item => item.IsArchived, request.IsArchived)
                    .SetProperty(item => item.Version, item => item.Version + 1)
                    .SetProperty(item => item.UpdatedAt, now), cancellationToken);
            if (rowsChanged != 1)
            {
                throw new InvalidOperationException("Шаблон был изменён, архивирован или удалён другим пользователем. Обновите список.");
            }

            template = await context.MessageTemplates.AsNoTracking()
                .SingleAsync(item => item.Id == request.Id.Value && item.OrganizationId == organizationId, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);

        return ToDto(template);
    }

    public async Task<bool> ArchiveTemplateAsync(
        string userId,
        Guid organizationId,
        Guid templateId,
        int expectedVersion,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureTemplatesManageAsync(userId, organizationId);
        var now = DateTime.UtcNow;
        var rowsChanged = await context.MessageTemplates
            .Where(item => item.Id == templateId && item.OrganizationId == organizationId
                && item.Version == expectedVersion && !item.IsArchived)
            .ExecuteUpdateAsync(update => update
                .SetProperty(item => item.IsArchived, true)
                .SetProperty(item => item.Version, item => item.Version + 1)
                .SetProperty(item => item.UpdatedAt, now), cancellationToken);

        if (rowsChanged != 1)
        {
            throw new InvalidOperationException("Шаблон уже изменён. Обновите список перед архивированием.");
        }

        return true;
    }

    public async Task<MessageTemplatePreviewDto> PreviewAsync(
        string userId,
        Guid organizationId,
        PreviewMessageTemplateDto request,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureSendAsync(userId, organizationId);
        var template = await context.MessageTemplates.AsNoTracking()
            .Where(item => item.Id == request.TemplateId
                && item.OrganizationId == organizationId
                && item.Version == request.TemplateVersion
                && !item.IsArchived
                && !item.Group.IsArchived)
            .Select(item => new { item.Id, item.Version, item.Body })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Шаблон не найден или его версия изменилась.");
        var conversation = await context.OrganizationConversations.AsNoTracking()
            .Where(item => item.Id == request.ConversationId && item.OrganizationId == organizationId)
            .Select(item => new
            {
                item.OrganizationClientId,
                item.IsReadOnly,
                item.IsChatActive,
                IsCurrentConnection = item.ConnectionId.HasValue && item.Connection!.IsCurrent
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Диалог не найден.");
        if (conversation.IsReadOnly || !conversation.IsChatActive || !conversation.IsCurrentConnection)
        {
            throw new InvalidOperationException("Нельзя подготовить отправку для закрытого канала.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["organization.name"] = await context.Organizations.AsNoTracking()
                .Where(item => item.Id == organizationId)
                .Select(item => item.Name)
                .SingleAsync(cancellationToken)
        };
        var placeholders = GetPlaceholders(template.Body);
        if (placeholders.Contains("client.name"))
        {
            await access.EnsurePermissionAsync(userId, organizationId, "clients.view");
            if (!conversation.OrganizationClientId.HasValue)
            {
                throw new ArgumentException("Шаблон требует связанного клиента.", nameof(request));
            }

            values["client.name"] = await context.OrganizationClients.AsNoTracking()
                .Where(item => item.Id == conversation.OrganizationClientId.Value && item.OrganizationId == organizationId)
                .Select(item => item.Name)
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new ArgumentException("Клиент не найден.", nameof(request));
        }

        if (placeholders.Any(item => item.StartsWith("maintenance.", StringComparison.Ordinal)))
        {
            await access.EnsurePermissionAsync(userId, organizationId, "maintenance.view");
            if (!request.MaintenanceId.HasValue || !conversation.OrganizationClientId.HasValue)
            {
                throw new ArgumentException("Для полей обслуживания требуется выбрать запись клиента.", nameof(request));
            }

            var maintenance = await context.Maintenance.AsNoTracking()
                .Where(item => item.Id == request.MaintenanceId.Value
                    && (item.BuyerClientId.HasValue
                        ? item.BuyerClientId == conversation.OrganizationClientId
                        : item.Vehicle.OrganizationClientId == conversation.OrganizationClientId)
                    && item.Vehicle.OrganizationId == organizationId)
                .Select(item => new
                {
                    item.PlannedDate,
                    item.Date,
                    item.PlannedStartTime,
                    Vehicle = item.Vehicle.RegistrationPlate ?? item.Vehicle.Name
                })
                .FirstOrDefaultAsync(cancellationToken)
                ?? throw new ArgumentException("Запись обслуживания не принадлежит клиенту этого диалога.", nameof(request));
            values["maintenance.date"] = (maintenance.PlannedDate ?? maintenance.Date).ToString("dd.MM.yyyy");
            values["maintenance.time"] = maintenance.PlannedStartTime?.ToString(@"hh\:mm") ?? string.Empty;
            values["maintenance.vehicle"] = maintenance.Vehicle ?? string.Empty;
        }

        var rendered = s_placeholderRegex.Replace(template.Body, match =>
        {
            var key = match.Groups[1].Value;
            return values.TryGetValue(key, out var value)
                ? value
                : throw new ArgumentException($"Для поля «{key}» не удалось получить значение.", nameof(request));
        });
        if (rendered.Length > 4096)
        {
            throw new ArgumentException("После подстановки текст превышает 4096 символов.", nameof(request));
        }

        return new MessageTemplatePreviewDto
        {
            TemplateId = template.Id,
            TemplateVersion = template.Version,
            Text = rendered
        };
    }

    private static MessageTemplateDto ToDto(MessageTemplate template) => new()
    {
        Id = template.Id,
        GroupId = template.GroupId,
        Name = template.Name,
        Body = template.Body,
        SortOrder = template.SortOrder,
        Version = template.Version,
        IsArchived = template.IsArchived
    };

    private static HashSet<string> GetPlaceholders(string text) => s_placeholderRegex.Matches(text)
        .Select(match => match.Groups[1].Value)
        .ToHashSet(StringComparer.Ordinal);

    private static void ValidatePlaceholders(string text)
    {
        var matches = s_placeholderRegex.Matches(text);
        foreach (Match match in matches)
        {
            if (!s_allowedPlaceholders.Contains(match.Groups[1].Value))
            {
                throw new ArgumentException($"Неизвестное поле шаблона: {match.Groups[1].Value}.", nameof(text));
            }
        }

        var withoutPlaceholders = s_placeholderRegex.Replace(text, string.Empty);
        if (withoutPlaceholders.Contains("{{", StringComparison.Ordinal)
            || withoutPlaceholders.Contains("}}", StringComparison.Ordinal))
        {
            throw new ArgumentException("В шаблоне есть незакрытая подстановка.", nameof(text));
        }
    }

    [GeneratedRegex("\\{\\{([^{}]+)\\}\\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
