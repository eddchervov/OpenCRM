using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Modules.Clients.Contracts;
using OpenCRM.Modules.Clients.Contracts.Dtos;
using OpenCRM.Modules.Clients.Commands;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Clients.Inquiries;

internal sealed class OrganizationInquiryService(
    OpenCRMContext context,
    InquiryAccess access,
    OrganizationClientTransaction transaction) : IOrganizationInquiryService, IOrganizationInquiryBookingLinkService
{
    private Guid? _lockedInquiryId;
    private long? _lockedInquiryVersion;
    private static readonly string[] s_openStages = [
        OrganizationInquiryStageCodes.New,
        OrganizationInquiryStageCodes.Clarifying,
        OrganizationInquiryStageCodes.Proposal,
        OrganizationInquiryStageCodes.Ready
    ];

    public async Task<OrganizationInquiryDto?> GetAsync(string userId, Guid organizationId, Guid inquiryId, CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var inquiry = await Query(organizationId).FirstOrDefaultAsync(item => item.Id == inquiryId, cancellationToken);
        if (inquiry == null)
        {
            return null;
        }
        return Map(inquiry, await GetCapabilitiesAsync(userId, organizationId, cancellationToken));
    }

    public async Task<OrganizationInquiryPageDto> GetPageAsync(string userId, Guid organizationId, OrganizationInquiryQueryDto query, CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var capabilities = await GetCapabilitiesAsync(userId, organizationId, cancellationToken);
        var bounds = GetDateBounds(query.Date, capabilities.TimeZoneId ?? Normalize(query.TimeZoneId) ?? "UTC");
        var filtered = ApplyFilters(Query(organizationId), query, bounds.Start, bounds.End);
        var totalCount = await filtered.CountAsync(cancellationToken);
        var rows = await filtered.OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var items = new List<OrganizationInquiryDto>(rows.Count);
        foreach (var row in rows)
        {
            items.Add(Map(row, capabilities));
        }

        return new OrganizationInquiryPageDto
        {
            Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount,
            CanManage = capabilities.CanManage,
            CanViewClients = capabilities.CanViewClients,
            CanViewVehicles = capabilities.CanViewVehicles,
            TimeZoneId = capabilities.TimeZoneId
        };
    }

    public async Task<IReadOnlyList<OrganizationInquiryDto>> GetActionRemindersAsync(
        string userId,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var capabilities = await GetCapabilitiesAsync(userId, organizationId, cancellationToken);
        var reminderCutoff = DateTime.UtcNow.AddMinutes(15);
        var inquiries = await Query(organizationId)
            .Where(item => s_openStages.Contains(item.StageCode)
                && item.ResponsibleUserId == userId
                && item.NextActionAt.HasValue
                && item.NextActionAt <= reminderCutoff)
            .OrderBy(item => item.NextActionAt)
            .ThenBy(item => item.Id)
            .Take(20)
            .ToListAsync(cancellationToken);

        return inquiries.Select(item => Map(item, capabilities)).ToArray();
    }

    public async Task<OrganizationInquiryBoardDto> GetBoardAsync(string userId, Guid organizationId, OrganizationInquiryQueryDto query, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var capabilities = await GetCapabilitiesAsync(userId, organizationId, cancellationToken);
        var bounds = GetDateBounds(query.Date, capabilities.TimeZoneId ?? Normalize(query.TimeZoneId) ?? "UTC");
        var page = Math.Max(1, query.Page);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var columns = new List<OrganizationInquiryBoardColumnDto>(s_openStages.Length);
        foreach (var stage in s_openStages)
        {
            var filtered = ApplyFilters(Query(organizationId).Where(item => item.StageCode == stage), query with { StageCode = null }, bounds.Start, bounds.End);
            var count = await filtered.CountAsync(cancellationToken);
            var rows = await filtered.OrderByDescending(item => item.UpdatedAt).ThenBy(item => item.Id)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            var items = new List<OrganizationInquiryDto>(rows.Count);
            foreach (var row in rows)
            {
                items.Add(Map(row, capabilities));
            }

            columns.Add(new OrganizationInquiryBoardColumnDto { StageCode = stage, Items = items, Count = count, Page = page, HasMore = count > page * pageSize });
        }

        return new OrganizationInquiryBoardDto
        {
            Columns = columns,
            CanManage = capabilities.CanManage,
            CanViewClients = capabilities.CanViewClients,
            CanViewVehicles = capabilities.CanViewVehicles,
            TimeZoneId = capabilities.TimeZoneId
        };
    }

    public async Task<OrganizationInquiryHistoryPageDto> GetHistoryAsync(string userId, Guid organizationId, Guid inquiryId, int page = 1, int pageSize = 30, CancellationToken cancellationToken = default)
    {
        await access.EnsureReadAsync(userId, organizationId);
        var exists = await context.OrganizationInquiries.AsNoTracking().AnyAsync(item => item.OrganizationId == organizationId && item.Id == inquiryId, cancellationToken);
        if (!exists)
        {
            throw new KeyNotFoundException("Обращение не найдено.");
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var capabilities = await GetCapabilitiesAsync(userId, organizationId, cancellationToken);
        var events = context.OrganizationInquiryEvents.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.OrganizationInquiryId == inquiryId)
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id);
        var totalCount = await events.CountAsync(cancellationToken);
        var items = await events.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new OrganizationInquiryEventDto
            {
                Id = item.Id,
                EventType = item.EventType,
                Title = item.Title,
                Description = item.Description,
                PreviousValue = item.EventType == "client" && !capabilities.CanViewClients
                    || item.EventType == "vehicle" && !capabilities.CanViewVehicles
                    || item.EventType == "converted" && !capabilities.CanViewMaintenance
                    || item.EventType == "responsible" && !capabilities.CanViewMembers
                    ? null : item.PreviousValue,
                NewValue = item.EventType == "client" && !capabilities.CanViewClients
                    || item.EventType == "vehicle" && !capabilities.CanViewVehicles
                    || item.EventType == "converted" && !capabilities.CanViewMaintenance
                    || item.EventType == "responsible" && !capabilities.CanViewMembers
                    ? null : item.NewValue,
                ActorUserId = item.ActorUserId,
                ActorName = item.ActorUser.UserName,
                CreatedAt = item.CreatedAt
            }).ToListAsync(cancellationToken);

        return new OrganizationInquiryHistoryPageDto { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<OrganizationInquiryDto> CreateAsync(string userId, Guid organizationId, OrganizationInquirySaveDto dto, CancellationToken cancellationToken = default)
    {
        await access.EnsureManageAsync(userId, organizationId);
        ValidateFields(dto);
        var capabilities = await GetCapabilitiesAsync(userId, organizationId, cancellationToken);
        EnsureCanSetLinks(dto, capabilities);
        await ValidateLinksAsync(organizationId, dto, cancellationToken);
        var now = DateTime.UtcNow;
        var inquiry = new OrganizationInquiry
        {
            Id = Guid.NewGuid(), OrganizationId = organizationId, ContactName = dto.ContactName.Trim(), Phone = Normalize(dto.ContactPhone), NormalizedPhone = NormalizePhone(dto.ContactPhone),
            SourceCode = Normalize(dto.SourceCode), Interest = dto.InterestText.Trim(), OrganizationClientId = dto.OrganizationClientId,
            VehicleId = dto.VehicleId, VehicleDescription = Normalize(dto.VehicleDescription), ResponsibleUserId = await ValidateResponsibleAsync(organizationId, dto.ResponsibleUserId),
            NextActionText = Normalize(dto.NextActionText), NextActionAt = dto.NextActionAt?.ToUniversalTime(), CreatedByUserId = userId,
            CreatedAt = now, UpdatedAt = now
        };

        context.OrganizationInquiries.Add(inquiry);
        AddEvent(inquiry, userId, "created", "Обращение создано", null, null, now);
        await context.SaveChangesAsync(cancellationToken);
        return await GetAsync(userId, organizationId, inquiry.Id, cancellationToken) ?? throw new KeyNotFoundException("Обращение не найдено.");
    }

    public async Task<OrganizationInquiryDto> UpdateAsync(string userId, Guid organizationId, Guid inquiryId, OrganizationInquirySaveDto dto, CancellationToken cancellationToken = default)
    {
        await access.EnsureManageAsync(userId, organizationId);
        ValidateFields(dto);
        var capabilities = await GetCapabilitiesAsync(userId, organizationId, cancellationToken);
        EnsureCanSetLinks(dto, capabilities);
        var responsibleUserId = await ValidateResponsibleAsync(organizationId, dto.ResponsibleUserId);
        var saved = await transaction.ExecuteAsync(async () =>
        {
            await LockInquiryAsync(organizationId, inquiryId, cancellationToken);
            var inquiry = await context.OrganizationInquiries.FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == inquiryId, cancellationToken)
                ?? throw new KeyNotFoundException("Обращение не найдено.");
            await context.Entry(inquiry).ReloadAsync(cancellationToken);
            EnsureVersion(inquiry, dto.Version);
            context.Entry(inquiry).Property(item => item.Version).OriginalValue = dto.Version;
            if (!OrganizationInquiryStageCodes.Open.Contains(inquiry.StageCode))
            {
                throw new InquiryConflictException("Завершенное обращение нельзя изменить.");
            }

            var organizationClientId = capabilities.CanViewClients ? dto.OrganizationClientId : inquiry.OrganizationClientId;
            var vehicleId = capabilities.CanViewVehicles ? dto.VehicleId : inquiry.VehicleId;
            await ValidateChangedLinksAsync(
                organizationId,
                inquiry.OrganizationClientId,
                inquiry.VehicleId,
                organizationClientId,
                vehicleId,
                cancellationToken);

            var now = DateTime.UtcNow;
            UpdateField(inquiry, userId, "contact", "Контакт изменён", inquiry.ContactName, dto.ContactName.Trim(), value => inquiry.ContactName = value ?? string.Empty, now);
            UpdateField(inquiry, userId, "phone", "Телефон изменён", inquiry.Phone, Normalize(dto.ContactPhone), value => inquiry.Phone = value, now);
            inquiry.NormalizedPhone = NormalizePhone(dto.ContactPhone);
            UpdateField(inquiry, userId, "source", "Источник изменён", inquiry.SourceCode, Normalize(dto.SourceCode), value => inquiry.SourceCode = value, now);
            UpdateField(inquiry, userId, "interest", "Интерес изменён", inquiry.Interest, dto.InterestText.Trim(), value => inquiry.Interest = value ?? string.Empty, now);
            UpdateField(inquiry, userId, "vehicle_description", "Описание автомобиля изменено", inquiry.VehicleDescription, Normalize(dto.VehicleDescription), value => inquiry.VehicleDescription = value, now);
            if (inquiry.OrganizationClientId != organizationClientId)
            {
                UpdateField(inquiry, userId, "client", "Клиент изменён", inquiry.OrganizationClientId?.ToString(), organizationClientId?.ToString(), _ => inquiry.OrganizationClientId = organizationClientId, now);
            }

            if (inquiry.VehicleId != vehicleId)
            {
                UpdateField(inquiry, userId, "vehicle", "Автомобиль изменён", inquiry.VehicleId?.ToString(), vehicleId?.ToString(), _ => inquiry.VehicleId = vehicleId, now);
            }

            if (inquiry.ResponsibleUserId != responsibleUserId)
            {
                UpdateField(inquiry, userId, "responsible", "Ответственный изменён", inquiry.ResponsibleUserId, responsibleUserId, _ => inquiry.ResponsibleUserId = responsibleUserId, now);
            }

            if (inquiry.NextActionText != Normalize(dto.NextActionText) || inquiry.NextActionAt != dto.NextActionAt?.ToUniversalTime())
            {
                var previous = $"{inquiry.NextActionText} ({inquiry.NextActionAt:O})";
                var next = $"{Normalize(dto.NextActionText)} ({dto.NextActionAt?.ToUniversalTime():O})";
                UpdateField(inquiry, userId, "action", "Следующее действие изменено", previous, next, _ =>
                {
                    inquiry.NextActionText = Normalize(dto.NextActionText);
                    inquiry.NextActionAt = dto.NextActionAt?.ToUniversalTime();
                }, now);
            }

            inquiry.Version++;
            inquiry.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return inquiry.Id;
        }, () => this.VerifyVersionAsync(organizationId, inquiryId, dto.Version + 1, cancellationToken));
        return await GetAsync(userId, organizationId, saved, cancellationToken) ?? throw new KeyNotFoundException("Обращение не найдено.");
    }

    public Task<OrganizationInquiryDto> ChangeStageAsync(string userId, Guid organizationId, Guid inquiryId, string stageCode, long version, CancellationToken cancellationToken = default) =>
        this.ChangeStageAsync(userId, organizationId, inquiryId, stageCode, version, null, cancellationToken);

    public Task<OrganizationInquiryDto> ChangeStageAsync(
        string userId,
        Guid organizationId,
        Guid inquiryId,
        string stageCode,
        long version,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var normalizedStageCode = stageCode?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!OrganizationInquiryStageCodes.Open.Contains(normalizedStageCode))
        {
            throw new ArgumentException("Выберите открытый этап. Для закрытия обращения укажите причину закрытия.", nameof(stageCode));
        }

        var normalizedComment = Normalize(comment);
        if (normalizedComment?.Length > 1000)
        {
            throw new ArgumentException("Комментарий должен содержать не более 1000 символов.", nameof(comment));
        }

        return this.ChangeStateAsync(userId, organizationId, inquiryId, normalizedStageCode, version, null, normalizedComment, cancellationToken);
    }

    public Task<OrganizationInquiryDto> CompleteActionAsync(string userId, Guid organizationId, Guid inquiryId, long version, CancellationToken cancellationToken = default) =>
        CompleteActionCoreAsync(userId, organizationId, inquiryId, version, cancellationToken);

    public async Task<OrganizationInquiryDto> RescheduleActionAsync(
        string userId,
        Guid organizationId,
        Guid inquiryId,
        DateTime nextActionAt,
        long version,
        CancellationToken cancellationToken = default)
    {
        await access.EnsureManageAsync(userId, organizationId);
        if (nextActionAt <= DateTime.UtcNow || nextActionAt > DateTime.UtcNow.AddYears(1))
        {
            throw new ArgumentException("Срок действия должен быть в будущем и не превышать один год.", nameof(nextActionAt));
        }

        await transaction.ExecuteAsync(async () =>
        {
            await LockInquiryAsync(organizationId, inquiryId, cancellationToken);
            var inquiry = await context.OrganizationInquiries.FirstOrDefaultAsync(
                item => item.OrganizationId == organizationId && item.Id == inquiryId,
                cancellationToken)
                ?? throw new KeyNotFoundException("Обращение не найдено.");
            await context.Entry(inquiry).ReloadAsync(cancellationToken);
            EnsureVersion(inquiry, version);
            context.Entry(inquiry).Property(item => item.Version).OriginalValue = version;
            if (!OrganizationInquiryStageCodes.Open.Contains(inquiry.StageCode) || inquiry.NextActionText == null)
            {
                throw new InquiryConflictException("У обращения нет активного действия.");
            }

            var previous = inquiry.NextActionAt;
            var now = DateTime.UtcNow;
            inquiry.NextActionAt = nextActionAt.ToUniversalTime();
            inquiry.Version++;
            inquiry.UpdatedAt = now;
            AddEvent(inquiry, userId, "action_rescheduled", "Срок следующего действия перенесён", previous?.ToString("O"), inquiry.NextActionAt.Value.ToString("O"), now);
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }, () => this.VerifyVersionAsync(organizationId, inquiryId, version + 1, cancellationToken));

        return await GetAsync(userId, organizationId, inquiryId, cancellationToken)
            ?? throw new KeyNotFoundException("Обращение не найдено.");
    }

    public Task<OrganizationInquiryDto> CloseAsync(string userId, Guid organizationId, Guid inquiryId, string reason, long version, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Укажите причину закрытия.", nameof(reason));
        }

        return ChangeStateAsync(userId, organizationId, inquiryId, OrganizationInquiryStageCodes.Closed, version, reason.Trim(), null, cancellationToken);
    }

    public async Task<OrganizationInquiryDto> AddNoteAsync(string userId, Guid organizationId, Guid inquiryId, string note, long version, CancellationToken cancellationToken = default)
    {
        await access.EnsureManageAsync(userId, organizationId);
        if (string.IsNullOrWhiteSpace(note) || note.Trim().Length > 1000)
        {
            throw new ArgumentException("Заметка должна содержать от 1 до 1000 символов.", nameof(note));
        }

        await transaction.ExecuteAsync(async () =>
        {
            await LockInquiryAsync(organizationId, inquiryId, cancellationToken);
            var inquiry = await context.OrganizationInquiries.FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == inquiryId, cancellationToken)
                ?? throw new KeyNotFoundException("Обращение не найдено.");
            await context.Entry(inquiry).ReloadAsync(cancellationToken);
            EnsureVersion(inquiry, version);
            context.Entry(inquiry).Property(item => item.Version).OriginalValue = version;
            var now = DateTime.UtcNow;
            AddEvent(inquiry, userId, "note", "Добавлена заметка", null, note.Trim(), now);
            inquiry.Version++;
            inquiry.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }, () => this.VerifyVersionAsync(organizationId, inquiryId, version + 1, cancellationToken));

        return await GetAsync(userId, organizationId, inquiryId, cancellationToken) ?? throw new KeyNotFoundException("Обращение не найдено.");
    }

    public async Task<OrganizationInquiryBookingLinkDto?> LockAndGetAsync(string userId, Guid organizationId, Guid inquiryId, CancellationToken cancellationToken = default)
    {
        await access.EnsureManageAsync(userId, organizationId);
        if (context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Для блокировки обращения нужна открытая транзакция.");
        }

        await LockInquiryAsync(organizationId, inquiryId, cancellationToken);
        var inquiry = await context.OrganizationInquiries.AsNoTracking()
            .Where(item => item.OrganizationId == organizationId && item.Id == inquiryId)
            .Select(item => new OrganizationInquiryBookingLinkDto
            {
                InquiryId = item.Id, OrganizationId = item.OrganizationId, Version = item.Version,
                StageCode = item.StageCode, MaintenanceId = item.MaintenanceId
            }).FirstOrDefaultAsync(cancellationToken);
        _lockedInquiryId = inquiry?.InquiryId;
        _lockedInquiryVersion = inquiry?.Version;
        return inquiry;
    }

    public async Task MarkBookedAsync(string userId, Guid organizationId, Guid inquiryId, long expectedVersion, Guid maintenanceId, CancellationToken cancellationToken = default)
    {
        await access.EnsureManageAsync(userId, organizationId);
        if (context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Для фиксации записи нужна открытая транзакция.");
        }

        if (_lockedInquiryId != inquiryId || _lockedInquiryVersion != expectedVersion)
        {
            throw new InvalidOperationException("Сначала заблокируйте обращение через LockAndGetAsync в этой транзакции.");
        }

        var inquiry = await context.OrganizationInquiries.FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == inquiryId, cancellationToken)
            ?? throw new KeyNotFoundException("Обращение не найдено.");
        await context.Entry(inquiry).ReloadAsync(cancellationToken);
        EnsureVersion(inquiry, expectedVersion);
        context.Entry(inquiry).Property(item => item.Version).OriginalValue = expectedVersion;
        var maintenanceBelongsToOrganization = await context.Maintenance.AsNoTracking()
            .AnyAsync(item => item.Id == maintenanceId && item.Vehicle.OrganizationId == organizationId, cancellationToken);
        if (!maintenanceBelongsToOrganization)
        {
            throw new InvalidOperationException("Запись не найдена в организации.");
        }
        if (inquiry.MaintenanceId.HasValue || !OrganizationInquiryStageCodes.Open.Contains(inquiry.StageCode))
        {
            throw new InquiryConflictException("Обращение уже завершено или записано.");
        }

        var now = DateTime.UtcNow;
        inquiry.MaintenanceId = maintenanceId;
        inquiry.StageCode = OrganizationInquiryStageCodes.Booked;
        inquiry.NextActionAt = null;
        inquiry.NextActionText = null;
        inquiry.Version++;
        inquiry.UpdatedAt = now;
        AddEvent(inquiry, userId, "converted", "Обращение записано", null, maintenanceId.ToString(), now);
        await context.SaveChangesAsync(cancellationToken);
        _lockedInquiryId = null;
        _lockedInquiryVersion = null;
    }

    private async Task<OrganizationInquiryDto> ChangeStateAsync(string userId, Guid organizationId, Guid inquiryId, string stageCode, long version, string? closeReason, string? comment, CancellationToken cancellationToken)
    {
        await access.EnsureManageAsync(userId, organizationId);
        stageCode = stageCode.Trim().ToLowerInvariant();
        if (stageCode != OrganizationInquiryStageCodes.Closed && !OrganizationInquiryStageCodes.Open.Contains(stageCode))
        {
            throw new ArgumentException("Неизвестный этап обращения.", nameof(stageCode));
        }

        await transaction.ExecuteAsync(async () =>
        {
            await LockInquiryAsync(organizationId, inquiryId, cancellationToken);
            var inquiry = await context.OrganizationInquiries.FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == inquiryId, cancellationToken)
                ?? throw new KeyNotFoundException("Обращение не найдено.");
            await context.Entry(inquiry).ReloadAsync(cancellationToken);
            EnsureVersion(inquiry, version);
            context.Entry(inquiry).Property(item => item.Version).OriginalValue = version;
            if (!OrganizationInquiryStageCodes.Open.Contains(inquiry.StageCode))
            {
                throw new InquiryConflictException("Обращение уже завершено.");
            }

            if (inquiry.StageCode == stageCode)
            {
                throw new InquiryConflictException("Обращение уже находится на выбранном этапе.");
            }

            var now = DateTime.UtcNow;
            AddEvent(inquiry, userId, stageCode == OrganizationInquiryStageCodes.Closed ? "closed" : "stage_changed",
                stageCode == OrganizationInquiryStageCodes.Closed ? "Обращение закрыто" : "Этап изменён",
                inquiry.StageCode, stageCode == OrganizationInquiryStageCodes.Closed ? closeReason : stageCode, now, comment);
            inquiry.StageCode = stageCode;
            inquiry.CloseReason = closeReason;
            if (stageCode == OrganizationInquiryStageCodes.Closed)
            {
                inquiry.NextActionText = null;
                inquiry.NextActionAt = null;
            }

            inquiry.Version++;
            inquiry.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }, () => this.VerifyVersionAsync(organizationId, inquiryId, version + 1, cancellationToken));

        return await GetAsync(userId, organizationId, inquiryId, cancellationToken) ?? throw new KeyNotFoundException("Обращение не найдено.");
    }

    private async Task<OrganizationInquiryDto> CompleteActionCoreAsync(string userId, Guid organizationId, Guid inquiryId, long version, CancellationToken cancellationToken)
    {
        await access.EnsureManageAsync(userId, organizationId);
        await transaction.ExecuteAsync(async () =>
        {
            await LockInquiryAsync(organizationId, inquiryId, cancellationToken);
            var inquiry = await context.OrganizationInquiries.FirstOrDefaultAsync(item => item.OrganizationId == organizationId && item.Id == inquiryId, cancellationToken)
                ?? throw new KeyNotFoundException("Обращение не найдено.");
            await context.Entry(inquiry).ReloadAsync(cancellationToken);
            EnsureVersion(inquiry, version);
            context.Entry(inquiry).Property(item => item.Version).OriginalValue = version;
            if (!OrganizationInquiryStageCodes.Open.Contains(inquiry.StageCode) || inquiry.NextActionText == null)
            {
                throw new InquiryConflictException("У обращения нет активного действия.");
            }

            var now = DateTime.UtcNow;
            AddEvent(inquiry, userId, "action_completed", "Следующее действие выполнено", inquiry.NextActionText, null, now);
            inquiry.NextActionText = null;
            inquiry.NextActionAt = null;
            inquiry.Version++;
            inquiry.UpdatedAt = now;
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }, () => this.VerifyVersionAsync(organizationId, inquiryId, version + 1, cancellationToken));

        return await GetAsync(userId, organizationId, inquiryId, cancellationToken) ?? throw new KeyNotFoundException("Обращение не найдено.");
    }

    private IQueryable<OrganizationInquiry> Query(Guid organizationId) => context.OrganizationInquiries.AsNoTracking()
        .Where(item => item.OrganizationId == organizationId)
        .Include(item => item.OrganizationClient)
        .Include(item => item.Vehicle).ThenInclude(vehicle => vehicle!.Brand)
        .Include(item => item.Vehicle).ThenInclude(vehicle => vehicle!.Model)
        .Include(item => item.ResponsibleUser);

    private static IQueryable<OrganizationInquiry> ApplyFilters(IQueryable<OrganizationInquiry> source, OrganizationInquiryQueryDto query, DateTime start, DateTime end)
    {
        if (!string.IsNullOrWhiteSpace(query.SearchQuery))
        {
            var pattern = $"%{query.SearchQuery.Trim()}%";
            var normalizedPhone = NormalizePhone(query.SearchQuery);
            source = source.Where(item => EF.Functions.ILike(item.ContactName, pattern)
                || (normalizedPhone != null && item.NormalizedPhone != null && EF.Functions.ILike(item.NormalizedPhone, $"%{normalizedPhone}%"))
                || EF.Functions.ILike(item.Interest, pattern));
        }

        if (!string.IsNullOrWhiteSpace(query.SourceCode))
        {
            source = source.Where(item => item.SourceCode == query.SourceCode.Trim());
        }

        if (!string.IsNullOrWhiteSpace(query.ResponsibleUserId))
        {
            source = source.Where(item => item.ResponsibleUserId == query.ResponsibleUserId);
        }

        if (!string.Equals(query.StageCode, "all", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(query.StageCode))
        {
            source = source.Where(item => s_openStages.Contains(item.StageCode));
        }
        else if (!string.Equals(query.StageCode, "all", StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(item => item.StageCode == query.StageCode!.Trim().ToLowerInvariant());
        }

        if (string.Equals(query.ActionDue, "overdue", StringComparison.OrdinalIgnoreCase))
        {
            var now = DateTime.UtcNow;
            source = source.Where(item => item.NextActionAt < now && item.NextActionAt != null);
        }
        if (string.Equals(query.ActionDue, "today", StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(item => item.NextActionAt >= start && item.NextActionAt < end);
        }
        if (string.Equals(query.ActionDue, "through-day", StringComparison.OrdinalIgnoreCase))
        {
            source = source.Where(item => s_openStages.Contains(item.StageCode)
                && item.NextActionAt < end && item.NextActionAt != null);
        }

        return source;
    }

    private OrganizationInquiryDto Map(OrganizationInquiry inquiry, InquiryCapabilities capabilities)
    {
        var canViewClients = capabilities.CanViewClients;
        var canViewVehicles = capabilities.CanViewVehicles;
        var canViewMaintenance = capabilities.CanViewMaintenance;
        return new OrganizationInquiryDto
        {
            Id = inquiry.Id, ContactName = inquiry.ContactName, ContactPhone = inquiry.Phone, SourceCode = inquiry.SourceCode, InterestText = inquiry.Interest,
            OrganizationClientId = canViewClients ? inquiry.OrganizationClientId : null, OrganizationClientName = canViewClients ? inquiry.OrganizationClient?.Name : null,
            VehicleId = canViewVehicles ? inquiry.VehicleId : null, VehicleName = !canViewVehicles || inquiry.Vehicle == null ? null : string.Join(" ", new[] { inquiry.Vehicle.Name, inquiry.Vehicle.Brand?.Name, inquiry.Vehicle.Model?.Name, inquiry.Vehicle.RegistrationPlate }.Where(value => !string.IsNullOrWhiteSpace(value))),
            VehicleDescription = inquiry.VehicleDescription, StageCode = inquiry.StageCode, ResponsibleUserId = inquiry.ResponsibleUserId,
            ResponsibleUserName = inquiry.ResponsibleUser?.UserName, NextActionText = inquiry.NextActionText, NextActionAt = inquiry.NextActionAt,
            CloseReason = inquiry.CloseReason, MaintenanceId = canViewMaintenance ? inquiry.MaintenanceId : null, Version = inquiry.Version, CreatedAt = inquiry.CreatedAt, UpdatedAt = inquiry.UpdatedAt,
            CanManage = capabilities.CanManage,
            CanCreateClient = capabilities.CanCreateClient,
            CanCreateBooking = capabilities.CanManage && capabilities.CanCreateBooking,
            CanViewLinkedMaintenance = inquiry.MaintenanceId.HasValue && canViewMaintenance,
            CanViewLinkedClient = inquiry.OrganizationClientId.HasValue && canViewClients,
            CanViewLinkedVehicle = inquiry.VehicleId.HasValue && canViewVehicles,
            CanViewClients = canViewClients,
            CanViewVehicles = canViewVehicles,
            TimeZoneId = capabilities.TimeZoneId
        };
    }

    private async Task ValidateLinksAsync(Guid organizationId, OrganizationInquirySaveDto dto, CancellationToken cancellationToken)
    {
        if (dto.OrganizationClientId.HasValue && !await context.OrganizationClients.AnyAsync(
            item => item.Id == dto.OrganizationClientId && item.OrganizationId == organizationId && !item.IsDeleted, cancellationToken))
        {
            throw new InvalidOperationException("Клиент не найден в организации.");
        }

        if (dto.VehicleId.HasValue && !await context.Vehicles.AnyAsync(
            item => item.Id == dto.VehicleId && item.OrganizationId == organizationId && !item.IsDeleted, cancellationToken))
        {
            throw new InvalidOperationException("Автомобиль не найден в организации.");
        }

        await ValidateVehicleClientLinkAsync(organizationId, dto.OrganizationClientId, dto.VehicleId, cancellationToken);
    }

    private async Task ValidateVehicleClientLinkAsync(
        Guid organizationId,
        Guid? organizationClientId,
        Guid? vehicleId,
        CancellationToken cancellationToken)
    {
        if (!organizationClientId.HasValue || !vehicleId.HasValue)
        {
            return;
        }

        if (!await context.Vehicles.AnyAsync(item => item.Id == vehicleId
            && item.OrganizationId == organizationId
            && item.OrganizationClientId == organizationClientId
            && !item.IsDeleted, cancellationToken))
        {
            throw new InvalidOperationException("Автомобиль не принадлежит выбранному клиенту.");
        }
    }

    private async Task ValidateChangedLinksAsync(
        Guid organizationId,
        Guid? previousClientId,
        Guid? previousVehicleId,
        Guid? nextClientId,
        Guid? nextVehicleId,
        CancellationToken cancellationToken)
    {
        if (previousClientId == nextClientId && previousVehicleId == nextVehicleId)
        {
            return;
        }

        if (nextClientId.HasValue && nextClientId != previousClientId
            && !await context.OrganizationClients.AnyAsync(item => item.Id == nextClientId
                && item.OrganizationId == organizationId && !item.IsDeleted, cancellationToken))
        {
            throw new InvalidOperationException("Клиент не найден в организации.");
        }

        if (nextVehicleId.HasValue && nextVehicleId != previousVehicleId
            && !await context.Vehicles.AnyAsync(item => item.Id == nextVehicleId
                && item.OrganizationId == organizationId && !item.IsDeleted, cancellationToken))
        {
            throw new InvalidOperationException("Автомобиль не найден в организации.");
        }

        if (nextClientId.HasValue && nextVehicleId.HasValue
            && !await context.Vehicles.AnyAsync(item => item.Id == nextVehicleId
                && item.OrganizationId == organizationId
                && item.OrganizationClientId == nextClientId, cancellationToken))
        {
            throw new InvalidOperationException("Автомобиль не принадлежит выбранному клиенту.");
        }
    }

    private static void EnsureCanSetLinks(OrganizationInquirySaveDto dto, InquiryCapabilities capabilities)
    {
        if (!capabilities.CanViewClients && dto.OrganizationClientId.HasValue
            || !capabilities.CanViewVehicles && dto.VehicleId.HasValue)
        {
            throw new UnauthorizedAccessException("Для изменения связанных клиента и автомобиля нужны права просмотра.");
        }
    }

    private async Task<string?> ValidateResponsibleAsync(Guid organizationId, string? userId)
    {
        var normalized = Normalize(userId);
        if (normalized == null)
        {
            return null;
        }
        return await context.OrganizationMembers.AsNoTracking().AnyAsync(item => item.OrganizationId == organizationId && item.UserId == normalized)
            ? normalized
            : throw new InvalidOperationException("Ответственный должен быть действующим участником организации.");
    }

    private static void ValidateFields(OrganizationInquirySaveDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ContactName) || dto.ContactName.Trim().Length > 160)
        {
            throw new ArgumentException("Укажите контакт длиной не более 160 символов.");
        }
        if (string.IsNullOrWhiteSpace(dto.InterestText) || dto.InterestText.Trim().Length > 4000)
        {
            throw new ArgumentException("Опишите интерес клиента длиной не более 4000 символов.");
        }
        var nextActionText = Normalize(dto.NextActionText);
        if (Normalize(dto.ContactPhone)?.Length > 40 || Normalize(dto.SourceCode)?.Length > 40 || Normalize(dto.VehicleDescription)?.Length > 500 || nextActionText?.Length > 500)
        {
            throw new ArgumentException("Одно из полей превышает допустимую длину.");
        }
        if ((nextActionText == null) != (dto.NextActionAt == null))
        {
            throw new ArgumentException("Для следующего действия укажите и описание, и срок.");
        }
    }

    private static void EnsureVersion(OrganizationInquiry inquiry, long version)
    {
        if (version != inquiry.Version)
        {
            throw new InquiryConflictException("Обращение изменилось. Обновите данные и повторите действие.");
        }
    }

    private async Task LockInquiryAsync(Guid organizationId, Guid inquiryId, CancellationToken cancellationToken) =>
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"OrganizationInquiries\" WHERE \"Id\" = {inquiryId} AND \"OrganizationId\" = {organizationId} FOR UPDATE", cancellationToken);

    private void AddEvent(OrganizationInquiry inquiry, string actorId, string type, string title, string? previous, string? next, DateTime now, string? description = null)
    {
        var eventItem = new OrganizationInquiryEvent
        {
            Id = Guid.NewGuid(), OrganizationInquiryId = inquiry.Id, OrganizationId = inquiry.OrganizationId, ActorUserId = actorId,
            EventType = type, Title = title, Description = description, PreviousValue = Truncate(previous), NewValue = Truncate(next), CreatedAt = now
        };
        inquiry.Events.Add(eventItem);
        context.OrganizationInquiryEvents.Add(eventItem);
    }

    private void UpdateField(OrganizationInquiry inquiry, string userId, string type, string title, string? previous, string? next, Action<string?> update, DateTime now)
    {
        if (previous == next)
        {
            return;
        }
        update(next);
        AddEvent(inquiry, userId, type, title, previous, next, now);
    }

    private async Task<InquiryCapabilities> GetCapabilitiesAsync(string userId, Guid organizationId, CancellationToken cancellationToken)
    {
        var timeZoneId = await GetOrganizationTimeZoneIdAsync(organizationId, cancellationToken);
        return new InquiryCapabilities(
            await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.InquiriesManage),
            await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsCreate),
            await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceCreate),
            await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MaintenanceView),
            await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.ClientsView),
            await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.VehiclesView),
            await access.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.MembersView),
            timeZoneId);
    }

    private static (DateTime Start, DateTime End) GetDateBounds(DateOnly? requestedDate, string timeZoneId)
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var localDate = requestedDate ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone));
            var localStart = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
            var localEnd = localStart.AddDays(1);
            return (TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone), TimeZoneInfo.ConvertTimeToUtc(localEnd, timeZone));
        }
        catch (TimeZoneNotFoundException)
        {
            throw new ArgumentException("Укажите существующий часовой пояс.", nameof(timeZoneId));
        }
        catch (InvalidTimeZoneException)
        {
            throw new ArgumentException("Укажите корректный часовой пояс.", nameof(timeZoneId));
        }
    }

    private async Task<string?> GetOrganizationTimeZoneIdAsync(Guid organizationId, CancellationToken cancellationToken) =>
        await context.Organizations.AsNoTracking().Where(item => item.Id == organizationId)
            .Select(item => item.ShiftTimeZoneId).FirstOrDefaultAsync(cancellationToken);

    private Task<bool> VerifyVersionAsync(Guid organizationId, Guid inquiryId, long version, CancellationToken cancellationToken) =>
        context.OrganizationInquiries.AsNoTracking().AnyAsync(
            item => item.Id == inquiryId && item.OrganizationId == organizationId && item.Version == version,
            cancellationToken);

    private sealed record InquiryCapabilities(
        bool CanManage,
        bool CanCreateClient,
        bool CanCreateBooking,
        bool CanViewMaintenance,
        bool CanViewClients,
        bool CanViewVehicles,
        bool CanViewMembers,
        string? TimeZoneId);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizePhone(string? value)
    {
        var digits = string.IsNullOrWhiteSpace(value) ? null : new string(value.Where(char.IsDigit).ToArray());
        return string.IsNullOrEmpty(digits) ? null : digits;
    }

    private static string? Truncate(string? value) => value?.Length > 1000 ? value[..1000] : value;
}
