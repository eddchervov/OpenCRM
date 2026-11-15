using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using MaintenanceEntity = OpenCRM.Domain.Maintenance;

namespace OpenCRM.Modules.Clients.RepeatVisits;

internal sealed class OrganizationRepeatVisitService(
    OpenCRMContext context,
    RepeatVisitAccess access) : IOrganizationRepeatVisitService, IOrganizationRepeatVisitBookingLinkService
{
    public async Task<OrganizationRepeatVisitOptionsDto> GetOptionsAsync(string userId, Guid organizationId, CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var responsibles = await context.OrganizationMembers.AsNoTracking()
            .Where(member => member.OrganizationId == organizationId)
            .OrderBy(member => member.User.UserName)
            .Select(member => new OrganizationRepeatVisitResponsibleDto(member.UserId, member.User.UserName ?? member.User.Email ?? member.UserId))
            .ToListAsync(cancellationToken);
        return new OrganizationRepeatVisitOptionsDto
        {
            Responsibles = responsibles,
            TimeZoneId = await GetTimeZoneIdAsync(organizationId, cancellationToken),
            CanCreate = await access.CanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit)
        };
    }

    public async Task<OrganizationRepeatVisitPageDto> GetPageAsync(
        string userId,
        Guid organizationId,
        OrganizationRepeatVisitQueryDto query,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var timeZoneId = await GetTimeZoneIdAsync(organizationId, cancellationToken);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var items = Query(organizationId);
        if (query.ClientId.HasValue)
        {
            items = items.Where(item => item.OrganizationClientId == query.ClientId.Value);
        }
        if (query.SourceMaintenanceId.HasValue)
        {
            items = items.Where(item => item.SourceMaintenanceId == query.SourceMaintenanceId.Value);
        }
        if (query.ActionsOnly)
        {
            var actionDate = query.Date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, GetTimeZone(timeZoneId)));
            var cutoff = GetEndOfLocalDateUtc(actionDate, timeZoneId);
            items = items.Where(item => item.IsActive
                && item.BookedMaintenanceId == null
                && item.ContactCompletedAtUtc == null
                && (item.ResponsibleUserId == null || item.ResponsibleUserId == userId)
                && item.ContactByUtc.HasValue
                && item.ContactByUtc <= cutoff);
        }

        var totalCount = await items.CountAsync(cancellationToken);
        items = query.ActionsOnly
            ? items.OrderBy(item => item.ContactByUtc).ThenBy(item => item.SuggestedVisitDate)
            : items.OrderBy(item => item.SuggestedVisitDate).ThenByDescending(item => item.UpdatedAt);
        var rows = await items
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var capabilities = await GetCapabilitiesAsync(userId, organizationId, cancellationToken);
        return new OrganizationRepeatVisitPageDto
        {
            Items = MapRows(rows, capabilities),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TimeZoneId = timeZoneId
        };
    }

    public async Task<OrganizationRepeatVisitDto?> GetAsync(string userId, Guid organizationId, Guid repeatVisitId, CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var item = await Query(organizationId).FirstOrDefaultAsync(row => row.Id == repeatVisitId, cancellationToken);
        if (item == null)
        {
            return null;
        }

        return Map(item, await GetCapabilitiesAsync(userId, organizationId, cancellationToken));
    }

    public async Task<OrganizationRepeatVisitDto> CreateAsync(string userId, Guid organizationId, OrganizationRepeatVisitSaveDto dto, CancellationToken cancellationToken = default)
    {
        await access.EnsureWriteAsync(userId, organizationId);
        var organizationTimeZoneId = await GetConfiguredTimeZoneIdAsync(organizationId, cancellationToken);
        ValidateFields(dto, isUpdate: false, organizationTimeZoneId);
        var resultId = await ExecuteLockedAsync(async () =>
        {
            await LockClientGeneratedIdAsync(dto.Id, cancellationToken);
            var existing = await context.OrganizationRepeatVisits.AsNoTracking()
                .FirstOrDefaultAsync(item => item.Id == dto.Id, cancellationToken);
            if (existing != null)
            {
                if (existing.OrganizationId != organizationId)
                {
                    throw new RepeatVisitConflictException("Идентификатор рекомендации уже занят.");
                }
                return existing.Id;
            }

            var source = await ValidateSourceAsync(organizationId, dto.SourceMaintenanceId, cancellationToken);
            var responsibleUserId = await ValidateResponsibleAsync(organizationId, dto.ResponsibleUserId, cancellationToken);
            var now = DateTime.UtcNow;
            var item = new OrganizationRepeatVisit
            {
                Id = dto.Id,
                OrganizationId = organizationId,
                OrganizationClientId = source.BuyerClientId ?? source.Vehicle.OrganizationClientId!.Value,
                VehicleId = source.VehicleId,
                SourceMaintenanceId = source.Id,
                ServiceName = dto.ServiceName.Trim(),
                Reason = Normalize(dto.Reason),
                SuggestedVisitDate = dto.SuggestedVisitDate,
                ResponsibleUserId = responsibleUserId,
                ContactByUtc = dto.ContactByUtc,
                CreatedByUserId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };
            context.OrganizationRepeatVisits.Add(item);
            AddAudit(organizationId, userId, "repeat_visit.created", "Рекомендация повторного визита создана", item.Id, now);
            await context.SaveChangesAsync(cancellationToken);
            return item.Id;
        });

        return await GetAsync(userId, organizationId, resultId, cancellationToken) ?? throw new KeyNotFoundException("Рекомендация повторного визита не найдена.");
    }

    public async Task<OrganizationRepeatVisitDto> UpdateAsync(string userId, Guid organizationId, Guid repeatVisitId, OrganizationRepeatVisitSaveDto dto, CancellationToken cancellationToken = default)
    {
        await access.EnsureWriteAsync(userId, organizationId);
        var organizationTimeZoneId = await GetConfiguredTimeZoneIdAsync(organizationId, cancellationToken);
        ValidateFields(dto, isUpdate: true, organizationTimeZoneId);
        var result = await ExecuteLockedAsync(async () =>
        {
            await LockAsync(organizationId, repeatVisitId, cancellationToken);
            var item = await context.OrganizationRepeatVisits.FirstOrDefaultAsync(row => row.OrganizationId == organizationId && row.Id == repeatVisitId, cancellationToken)
                ?? throw new KeyNotFoundException("Рекомендация повторного визита не найдена.");
            await context.Entry(item).ReloadAsync(cancellationToken);
            if (dto.Id != Guid.Empty && dto.Id != item.Id
                || dto.SourceMaintenanceId != Guid.Empty && dto.SourceMaintenanceId != item.SourceMaintenanceId)
            {
                throw new ArgumentException("Клиента, автомобиль и исходный заказ нельзя изменить.");
            }
            EnsureVersion(item, dto.Version);
            EnsureCanChange(item);
            var contactByChanged = item.ContactByUtc != dto.ContactByUtc;
            var suggestedDateChanged = item.SuggestedVisitDate != dto.SuggestedVisitDate;
            if (dto.ContactByUtc.HasValue && (contactByChanged || suggestedDateChanged))
            {
                ValidateScheduledAt(
                    dto.ContactByUtc.Value,
                    dto.SuggestedVisitDate,
                    organizationTimeZoneId,
                    "Срок связи",
                    requireFuture: contactByChanged);
            }

            var responsibleUserId = await ValidateResponsibleAsync(organizationId, dto.ResponsibleUserId, cancellationToken);
            var now = DateTime.UtcNow;
            item.ServiceName = dto.ServiceName.Trim();
            item.Reason = Normalize(dto.Reason);
            item.SuggestedVisitDate = dto.SuggestedVisitDate;
            item.ResponsibleUserId = responsibleUserId;
            if (item.ContactByUtc != dto.ContactByUtc)
            {
                item.ContactCompletedAtUtc = null;
            }
            item.ContactByUtc = dto.ContactByUtc;
            item.UpdatedAt = now;
            item.Version++;
            context.Entry(item).Property(row => row.Version).OriginalValue = dto.Version;
            AddAudit(organizationId, userId, "repeat_visit.updated", "Рекомендация повторного визита изменена", item.Id, now);
            await context.SaveChangesAsync(cancellationToken);
            return item.Id;
        });

        return await GetAsync(userId, organizationId, result, cancellationToken) ?? throw new KeyNotFoundException("Рекомендация повторного визита не найдена.");
    }

    public Task<OrganizationRepeatVisitDto> CompleteActionAsync(string userId, Guid organizationId, Guid repeatVisitId, long version, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, organizationId, repeatVisitId, version, "repeat_visit.contact_completed", "Связь с клиентом отмечена выполненной", item =>
        {
            if (!item.ContactByUtc.HasValue || item.ContactCompletedAtUtc.HasValue)
            {
                throw new RepeatVisitConflictException("Для рекомендации нет незавершённой задачи связи с клиентом.");
            }
            item.ContactCompletedAtUtc = DateTime.UtcNow;
        }, cancellationToken, contactTask: true);

    public Task<OrganizationRepeatVisitDto> RescheduleActionAsync(string userId, Guid organizationId, Guid repeatVisitId, DateTime contactByUtc, long version, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, organizationId, repeatVisitId, version, "repeat_visit.contact_rescheduled", "Срок связи с клиентом перенесён", item =>
        {
            if (!item.ContactByUtc.HasValue || item.ContactCompletedAtUtc.HasValue)
            {
                throw new RepeatVisitConflictException("Для рекомендации нет незавершённой задачи связи с клиентом.");
            }
            ValidateScheduledAt(contactByUtc, item.SuggestedVisitDate, item.Organization.ShiftTimeZoneId, "Срок связи");
            item.ContactByUtc = contactByUtc;
        }, cancellationToken, contactTask: true);

    public Task<OrganizationRepeatVisitDto> ChangeStateAsync(string userId, Guid organizationId, Guid repeatVisitId, bool isActive, long version, CancellationToken cancellationToken = default) =>
        ChangeAsync(userId, organizationId, repeatVisitId, version, "repeat_visit.state_changed", isActive ? "Рекомендация повторного визита включена" : "Рекомендация повторного визита отключена", item => item.IsActive = isActive, cancellationToken);

    public async Task<OrganizationRepeatVisitBookingContext?> LockAndGetAsync(string userId, Guid organizationId, Guid repeatVisitId, CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        EnsureTransaction();
        return await ExecuteLockedAsync(async () =>
        {
            await LockAsync(organizationId, repeatVisitId, cancellationToken);
            return await context.OrganizationRepeatVisits.AsNoTracking()
                .Where(item => item.OrganizationId == organizationId && item.Id == repeatVisitId)
                .Select(item => new OrganizationRepeatVisitBookingContext(item.Id, item.OrganizationClientId, item.VehicleId,
                    item.ServiceName, item.SuggestedVisitDate, item.Version, item.IsActive, item.BookedMaintenanceId))
                .FirstOrDefaultAsync(cancellationToken);
        });
    }

    public async Task MarkBookedAsync(string userId, Guid organizationId, Guid repeatVisitId, Guid maintenanceId, long expectedVersion, CancellationToken cancellationToken = default)
    {
        await access.EnsureWriteAsync(userId, organizationId);
        EnsureTransaction();
        await ExecuteLockedAsync(async () =>
        {
            await LockAsync(organizationId, repeatVisitId, cancellationToken);
            var item = await context.OrganizationRepeatVisits.FirstOrDefaultAsync(row => row.OrganizationId == organizationId && row.Id == repeatVisitId, cancellationToken)
                ?? throw new KeyNotFoundException("Рекомендация повторного визита не найдена.");
            if (item.BookedMaintenanceId.HasValue)
            {
                if (item.BookedMaintenanceId == maintenanceId)
                {
                    return true;
                }
                throw new RepeatVisitConflictException("По этой рекомендации уже создана запись.");
            }
            EnsureVersion(item, expectedVersion);
            if (!item.IsActive)
            {
                throw new RepeatVisitConflictException("Отключённую рекомендацию нельзя записать.");
            }
            item.BookedMaintenanceId = maintenanceId;
            item.Version++;
            item.UpdatedAt = DateTime.UtcNow;
            AddAudit(organizationId, userId, "repeat_visit.booked", "По рекомендации создана запись", item.Id, item.UpdatedAt);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        });
    }

    private async Task<OrganizationRepeatVisitDto> ChangeAsync(
        string userId,
        Guid organizationId,
        Guid repeatVisitId,
        long version,
        string auditCode,
        string auditTitle,
        Action<OrganizationRepeatVisit> change,
        CancellationToken cancellationToken,
        bool contactTask = false)
    {
        if (contactTask)
        {
            await access.EnsureContactActionWriteAsync(userId, organizationId);
        }
        else
        {
            await access.EnsureWriteAsync(userId, organizationId);
        }
        await ExecuteLockedAsync(async () =>
        {
            await LockAsync(organizationId, repeatVisitId, cancellationToken);
            var item = await context.OrganizationRepeatVisits.Include(row => row.Organization)
                .FirstOrDefaultAsync(row => row.OrganizationId == organizationId && row.Id == repeatVisitId, cancellationToken)
                ?? throw new KeyNotFoundException("Рекомендация повторного визита не найдена.");
            await context.Entry(item).ReloadAsync(cancellationToken);
            EnsureVersion(item, version);
            EnsureCanChange(item);
            if (contactTask
                && item.ResponsibleUserId != null
                && item.ResponsibleUserId != userId
                && !await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit))
            {
                throw new UnauthorizedAccessException("Выполнить задачу может назначенный сотрудник или пользователь с правом редактирования клиентов.");
            }
            if (auditCode.StartsWith("repeat_visit.contact_", StringComparison.Ordinal) && !item.IsActive)
            {
                throw new RepeatVisitConflictException("Задача выключена вместе с рекомендацией.");
            }
            change(item);
            item.Version++;
            item.UpdatedAt = DateTime.UtcNow;
            context.Entry(item).Property(row => row.Version).OriginalValue = version;
            AddAudit(organizationId, userId, auditCode, auditTitle, item.Id, item.UpdatedAt);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        });
        return await GetAsync(userId, organizationId, repeatVisitId, cancellationToken) ?? throw new KeyNotFoundException("Рекомендация повторного визита не найдена.");
    }

    private IQueryable<OrganizationRepeatVisit> Query(Guid organizationId) => context.OrganizationRepeatVisits.AsNoTracking()
        .Where(item => item.OrganizationId == organizationId)
        .Where(item => !item.OrganizationClient.IsDeleted
            && item.OrganizationClient.OrganizationId == organizationId
            && !item.Vehicle.IsDeleted
            && item.Vehicle.OrganizationId == organizationId)
        .Include(item => item.OrganizationClient)
        .Include(item => item.Vehicle).ThenInclude(vehicle => vehicle.Brand)
        .Include(item => item.Vehicle).ThenInclude(vehicle => vehicle.Model)
        .Include(item => item.ResponsibleUser)
        .Include(item => item.Organization);

    private IReadOnlyList<OrganizationRepeatVisitDto> MapRows(List<OrganizationRepeatVisit> rows, Capabilities capabilities)
    {
        if (rows.Count == 0)
        {
            return [];
        }
        return rows.Select(item => Map(item, capabilities)).ToArray();
    }

    private OrganizationRepeatVisitDto Map(OrganizationRepeatVisit item, Capabilities capabilities) => new()
    {
        Id = item.Id,
        OrganizationClientId = item.OrganizationClientId,
        ClientName = item.OrganizationClient.Name,
        VehicleId = item.VehicleId,
        VehicleName = string.Join(" ", new[] { item.Vehicle.Brand?.Name ?? item.Vehicle.ManualBrandName, item.Vehicle.Model?.Name ?? item.Vehicle.ManualModelName, item.Vehicle.RegistrationPlate }.Where(value => !string.IsNullOrWhiteSpace(value))),
        SourceMaintenanceId = item.SourceMaintenanceId,
        ServiceName = item.ServiceName,
        Reason = item.Reason,
        SuggestedVisitDate = item.SuggestedVisitDate,
        ResponsibleUserId = item.ResponsibleUserId,
        ResponsibleName = item.ResponsibleUser?.UserName,
        ContactByUtc = item.ContactByUtc,
        ContactCompletedAtUtc = item.ContactCompletedAtUtc,
        IsActive = item.IsActive,
        BookedMaintenanceId = item.BookedMaintenanceId,
        Version = item.Version,
        CanEdit = capabilities.CanEdit && !item.BookedMaintenanceId.HasValue,
        CanManageContact = capabilities.CanManageContact
            && (capabilities.CanEdit || item.ResponsibleUserId == null || item.ResponsibleUserId == capabilities.UserId)
            && item.IsActive
            && !item.BookedMaintenanceId.HasValue
            && item.ContactByUtc.HasValue
            && !item.ContactCompletedAtUtc.HasValue,
        CanBook = capabilities.CanEdit && capabilities.CanBook && item.IsActive && !item.BookedMaintenanceId.HasValue,
        TimeZoneId = capabilities.TimeZoneId,
    };

    private async Task<Capabilities> GetCapabilitiesAsync(string userId, Guid organizationId, CancellationToken cancellationToken) => new(
        await access.CanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsEdit),
        await access.CanWriteAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView),
        await access.CanWriteAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate),
        await GetTimeZoneIdAsync(organizationId, cancellationToken),
        userId);

    private async Task<string> GetTimeZoneIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId)
            .Select(item => item.ShiftTimeZoneId)
            .FirstOrDefaultAsync(cancellationToken) ?? "UTC";

    private async Task<string?> GetConfiguredTimeZoneIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var timeZoneId = await context.Organizations.AsNoTracking()
            .Where(item => item.Id == organizationId)
            .Select(item => item.ShiftTimeZoneId)
            .FirstOrDefaultAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return null;
        }

        GetTimeZone(timeZoneId);
        return timeZoneId;
    }

    private async Task<MaintenanceEntity> ValidateSourceAsync(Guid organizationId, Guid sourceMaintenanceId, CancellationToken cancellationToken)
    {
        var source = await context.Maintenance.AsNoTracking()
            .Include(item => item.Vehicle)
            .Where(item => item.Id == sourceMaintenanceId
                && !item.IsDeleted
                && item.HandedOverAt.HasValue
                && item.Vehicle.OrganizationId == organizationId
                && !item.Vehicle.IsDeleted
                && item.Vehicle.OrganizationClientId.HasValue
                && (!item.BuyerClientId.HasValue || item.BuyerClientId == item.Vehicle.OrganizationClientId)
                && !item.Vehicle.OrganizationClient!.IsDeleted
                && item.Vehicle.OrganizationClient.OrganizationId == organizationId)
            .Select(item => item)
            .FirstOrDefaultAsync(cancellationToken);
        if (source == null)
        {
            throw new ArgumentException("Повторный визит можно рекомендовать только по выданному заказу с клиентом и автомобилем этой организации.");
        }
        return source;
    }

    private async Task<string?> ValidateResponsibleAsync(Guid organizationId, string? responsibleUserId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(responsibleUserId))
        {
            return null;
        }
        var isMember = await context.OrganizationMembers.AsNoTracking().AnyAsync(member =>
            member.OrganizationId == organizationId && member.UserId == responsibleUserId, cancellationToken);
        if (!isMember)
        {
            throw new ArgumentException("Ответственный должен состоять в этой организации.");
        }
        return responsibleUserId;
    }

    private void ValidateFields(OrganizationRepeatVisitSaveDto dto, bool isUpdate, string? organizationTimeZoneId)
    {
        if ((!isUpdate && (dto.Id == Guid.Empty || dto.SourceMaintenanceId == Guid.Empty))
            || string.IsNullOrWhiteSpace(dto.ServiceName)
            || dto.ServiceName.Trim().Length > 240)
        {
            throw new ArgumentException("Укажите услугу и исходный выданный заказ.");
        }
        if (dto.Reason?.Length > 1000)
        {
            throw new ArgumentException("Причина не должна превышать 1000 символов.");
        }
        if (isUpdate && dto.Version <= 0)
        {
            throw new ArgumentException("Укажите актуальную версию рекомендации.");
        }
        if (!isUpdate && dto.Version != 0)
        {
            throw new ArgumentException("Новая рекомендация не должна содержать версию.");
        }
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            string.IsNullOrWhiteSpace(organizationTimeZoneId) ? TimeZoneInfo.Utc : GetTimeZone(organizationTimeZoneId)));
        if (dto.SuggestedVisitDate <= today)
        {
            throw new ArgumentException("Рекомендуемая дата визита должна быть в будущем.");
        }
        if (!isUpdate && dto.ContactByUtc.HasValue)
        {
            ValidateScheduledAt(dto.ContactByUtc.Value, dto.SuggestedVisitDate, organizationTimeZoneId, "Срок связи");
        }
    }

    private static void ValidateScheduledAt(
        DateTime value,
        DateOnly suggestedDate,
        string? timeZoneId,
        string fieldName,
        bool requireFuture = true)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException($"{fieldName}: не удалось определить время. Выберите дату и время заново.");
        }
        if (requireFuture && value <= DateTime.UtcNow)
        {
            throw new ArgumentException($"{fieldName}: выберите дату и время в будущем.");
        }
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("Укажите часовой пояс организации перед настройкой напоминаний.");
        }
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(value, GetTimeZone(timeZoneId)));
        if (localDate > suggestedDate)
        {
            throw new ArgumentException($"{fieldName} не должен быть позже рекомендуемой даты визита.");
        }
    }

    private static DateTime GetEndOfLocalDateUtc(DateOnly date, string timeZoneId)
    {
        var zone = GetTimeZone(timeZoneId);
        var nextDate = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(nextDate, zone).AddTicks(-1);
    }

    private void AddAudit(Guid organizationId, string userId, string code, string title, Guid targetId, DateTime now) => context.OrganizationAuditEvents.Add(new OrganizationAuditEvent
    {
        Id = Guid.NewGuid(), OrganizationId = organizationId, ActorUserId = userId, EventType = code,
        Title = title, TargetType = "organization_repeat_visit", TargetId = targetId.ToString(), CreatedAt = now
    });

    private async Task LockAsync(Guid organizationId, Guid repeatVisitId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"OrganizationRepeatVisits\" WHERE \"Id\" = {repeatVisitId} AND \"OrganizationId\" = {organizationId} FOR UPDATE", cancellationToken);

    private async Task LockClientGeneratedIdAsync(Guid repeatVisitId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({repeatVisitId.ToString()}, 0))", cancellationToken);

    private static TimeZoneInfo GetTimeZone(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new ArgumentException("В настройках организации указан неизвестный часовой пояс.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new ArgumentException("Часовой пояс организации повреждён. Проверьте настройки.", exception);
        }
    }

    private void EnsureTransaction()
    {
        if (context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Связь рекомендации с записью нужно выполнять внутри общей транзакции.");
        }
    }

    private async Task<T> ExecuteLockedAsync<T>(Func<Task<T>> action)
    {
        if (context.Database.CurrentTransaction != null)
        {
            return await action();
        }
        var isRetry = false;
        T result = default!;
        await context.Database.CreateExecutionStrategy().ExecuteInTransactionAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }
            isRetry = true;
            result = await action();
        }, () => Task.FromResult(true));
        return result;
    }

    private static void EnsureCanChange(OrganizationRepeatVisit item)
    {
        if (item.BookedMaintenanceId.HasValue)
        {
            throw new RepeatVisitConflictException("Уже созданную запись нельзя изменить.");
        }
    }

    private static void EnsureVersion(OrganizationRepeatVisit item, long version)
    {
        if (item.Version != version)
        {
            throw new RepeatVisitConflictException("Рекомендация изменилась. Обновите данные и повторите действие.");
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record Capabilities(bool CanEdit, bool CanManageContact, bool CanBook, string TimeZoneId, string UserId);
}

