using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary> Сервис журнала работ организации. </summary>
public sealed class OrganizationWorkJournalQueryService(OpenCRMContext opencrmContext) : IOrganizationWorkJournalQueryService
{
    /// <summary> Возвращает журнал работ организации за период с фильтрами. </summary>
    public async Task<OrganizationWorkspaceWorkJournalResult> GetWorkJournalAsync(
        Guid organizationId,
        DateTime dateFrom,
        DateTime dateTo,
        Guid? vehicleId,
        string? kind,
        string? tab,
        bool hasDefectsModule,
        TimeZoneInfo? timeZone = null)
    {
        var from = dateFrom.Date;
        var to = dateTo.Date;
        if (to < from)
        {
            (from, to) = (to, from);
        }

        var selectedKind = OrganizationWorkJournalQueryService.NormalizeWorkJournalKind(kind);
        var selectedTab = OrganizationWorkJournalQueryService.NormalizeWorkJournalTab(tab);
        var toExclusive = to.AddDays(1);
        var zone = timeZone ?? TimeZoneInfo.Utc;
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), zone);
        var toUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(toExclusive, DateTimeKind.Unspecified), zone);
        var scenarioCode = await opencrmContext.Organizations
            .AsNoTracking()
            .Where(x => x.Id == organizationId)
            .Select(x => x.PrimaryScenarioCode)
            .FirstOrDefaultAsync();

        var vehicleOptionRows = await opencrmContext.Vehicles
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId && !x.IsDeleted && !x.IsArchived)
            .Select(x => new
            {
                x.Id,
                BrandName = x.BrandId.HasValue
                    ? (x.Brand != null ? x.Brand.Name : string.Empty)
                    : (x.ManualBrandName ?? string.Empty),
                ModelName = x.ModelId.HasValue
                    ? (x.Model != null ? x.Model.Name : string.Empty)
                    : (x.ManualModelName ?? string.Empty),
                x.Name,
                x.YearIssue,
                x.RegistrationPlate,
                HasCatalogIdentity = x.BrandId.HasValue || x.ModelId.HasValue,
                OrganizationClientName = x.OrganizationClient != null ? x.OrganizationClient.Name : null
            })
            .ToListAsync();

        var vehicleOptions = vehicleOptionRows
            .Select(x => new OrganizationVehicleOptionDto
            {
                Id = x.Id,
                DisplayName = Helpers.VehicleDisplayName.Format(x.BrandName, x.ModelName, x.RegistrationPlate),
                Subtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(x.Name, x.YearIssue, x.OrganizationClientName, x.RegistrationPlate, !x.HasCatalogIdentity)
            })
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Subtitle)
            .ToList();

        var workItems = new List<OrganizationWorkJournalWorkItemDto>();

        if (string.IsNullOrWhiteSpace(selectedKind) || selectedKind == "maintenance")
        {
            var completedMaintenances = await opencrmContext.Maintenance
                .AsNoTracking()
                .Where(x => x.Vehicle.OrganizationId == organizationId
                    && !x.IsDeleted
                    && x.Status == MaintenanceStatus.Completed
                    && x.CompletedDate.HasValue
                    && x.CompletedDate.Value >= from
                    && x.CompletedDate.Value < toExclusive
                    && (!vehicleId.HasValue || x.VehicleId == vehicleId.Value))
                .Include(x => x.Vehicle)
                    .ThenInclude(x => x.Brand)
                .Include(x => x.Vehicle)
                    .ThenInclude(x => x.Model)
                .Include(x => x.Vehicle)
                    .ThenInclude(x => x.OrganizationClient)
                .OrderByDescending(x => x.CompletedDate)
                .ToListAsync();

            workItems.AddRange(completedMaintenances.Select(x => new OrganizationWorkJournalWorkItemDto
            {
                OccurredAt = OrganizationWorkJournalQueryService.BuildWorkJournalMaintenanceOccurredAt(x.CompletedDate!.Value, x.PlannedStartTime),
                Kind = "maintenance",
                EventCode = "completed",
                KindName = "Обслуживание выполнено",
                Title = OrganizationWorkspaceMaintenanceMapper.GetMaintenanceTypeName(x.Type, scenarioCode),
                VehicleDisplayName = OrganizationWorkspaceMaintenanceMapper.GetVehicleDisplayName(x.Vehicle),
                VehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(
                    x.Vehicle.Name,
                    x.Vehicle.YearIssue,
                    x.Vehicle.OrganizationClient?.Name,
                    x.Vehicle.RegistrationPlate,
                    !(x.Vehicle.BrandId.HasValue || x.Vehicle.ModelId.HasValue)),
                ClientId = x.BuyerClientId,
                ClientName = x.BuyerClientId.HasValue ? x.BuyerName : null,
                ResultText = x.Details.Count > 0
                    ? string.Join(", ", x.Details.Select(d => d.Name).Where(name => !string.IsNullOrWhiteSpace(name)).Take(3))
                    : x.Comment,
                MetaText = x.Mileage.HasValue
                    ? $"{x.Mileage.Value:N0} {(x.Vehicle.MileageUnit == VehicleMileageUnit.EngineHours ? "м/ч" : "км")}"
                    : null,
                Price = (decimal?)x.Details.Sum(d => d.Price ?? 0),
                DetailUrl = $"#maintenance/{x.Id}",
                VehicleUrl = $"#vehicles/{x.VehicleId}"
            }));
        }

        if (hasDefectsModule && (string.IsNullOrWhiteSpace(selectedKind) || selectedKind == "defect"))
        {
            var closedDefects = await opencrmContext.VehicleDefects
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && !x.IsDeleted
                    && x.ClosedAt.HasValue
                    && x.ClosedAt.Value >= fromUtc
                    && x.ClosedAt.Value < toUtc
                    && (!vehicleId.HasValue || x.VehicleId == vehicleId.Value))
                .Include(x => x.Vehicle)
                    .ThenInclude(x => x.Brand)
                .Include(x => x.Vehicle)
                    .ThenInclude(x => x.Model)
                .Include(x => x.Vehicle)
                    .ThenInclude(x => x.OrganizationClient)
                .OrderByDescending(x => x.ClosedAt)
                .ToListAsync();

            workItems.AddRange(closedDefects.Select(x => new OrganizationWorkJournalWorkItemDto
            {
                OccurredAt = x.ClosedAt!.Value,
                Kind = "defect",
                EventCode = "closed",
                KindName = "Неисправность закрыта",
                Title = x.Title,
                VehicleDisplayName = OrganizationWorkspaceMaintenanceMapper.GetVehicleDisplayName(x.Vehicle),
                VehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(
                    x.Vehicle.Name,
                    x.Vehicle.YearIssue,
                    x.Vehicle.OrganizationClient?.Name,
                    x.Vehicle.RegistrationPlate,
                    !(x.Vehicle.BrandId.HasValue || x.Vehicle.ModelId.HasValue)),
                ClientId = x.Vehicle.OrganizationClientId,
                ClientName = x.Vehicle.OrganizationClient?.Name,
                ResultText = x.Description,
                MetaText = $"{VehicleDefectSeverityCodes.GetName(x.SeverityCode)} • {VehicleDefectStatusCodes.GetName(x.StatusCode)}",
                DetailUrl = $"#defects/{x.Id}",
                VehicleUrl = $"#vehicles/{x.VehicleId}"
            }));
        }

        var orderedWorkItems = workItems
            .OrderByDescending(item => ToOrganizationDateTime(item.OccurredAt, zone))
            .ThenBy(x => x.Title)
            .ToList();

        var items = new List<OrganizationWorkJournalItemDto>();

        if (string.IsNullOrWhiteSpace(selectedKind) || selectedKind == "maintenance")
        {
            var maintenanceEvents = await opencrmContext.MaintenanceHistoryEvents
                .AsNoTracking()
                .Where(x => (x.OrganizationId == organizationId || x.Maintenance.Vehicle.OrganizationId == organizationId)
                    && x.CreatedAt >= fromUtc
                    && x.CreatedAt < toUtc
                    && (!vehicleId.HasValue || x.VehicleId == vehicleId.Value))
                .Select(x => new
                {
                    x.CreatedAt,
                    x.Title,
                    x.Description,
                    x.EventType,
                    x.ActorDisplayName,
                    x.User.Email,
                    x.User.UserName,
                    x.User.OriginalEmail,
                    x.User.IsDeleted,
                    x.MaintenanceId,
                    x.VehicleId,
                    MaintenanceType = x.Maintenance.Type,
                    MaintenanceStatus = x.Maintenance.Status,
                    BrandName = x.Maintenance.Vehicle.BrandId.HasValue
                        ? (x.Maintenance.Vehicle.Brand != null ? x.Maintenance.Vehicle.Brand.Name : string.Empty)
                        : (x.Maintenance.Vehicle.ManualBrandName ?? string.Empty),
                    ModelName = x.Maintenance.Vehicle.ModelId.HasValue
                        ? (x.Maintenance.Vehicle.Model != null ? x.Maintenance.Vehicle.Model.Name : string.Empty)
                        : (x.Maintenance.Vehicle.ManualModelName ?? string.Empty),
                    VehicleName = x.Maintenance.Vehicle.Name,
                    x.Maintenance.Vehicle.YearIssue,
                    x.Maintenance.Vehicle.RegistrationPlate,
                    HasCatalogIdentity = x.Maintenance.Vehicle.BrandId.HasValue || x.Maintenance.Vehicle.ModelId.HasValue,
                    OrganizationClientName = x.Maintenance.Vehicle.OrganizationClient != null
                        ? x.Maintenance.Vehicle.OrganizationClient.Name
                        : null
                })
                .ToListAsync();

            items.AddRange(maintenanceEvents.Select(x => new OrganizationWorkJournalItemDto
            {
                OccurredAt = x.CreatedAt,
                Kind = "maintenance",
                EventCode = x.EventType,
                KindName = OrganizationWorkJournalQueryService.GetWorkJournalMaintenanceKindName(x.EventType),
                Title = x.Title,
                Description = x.Description,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(x.BrandName, x.ModelName, x.RegistrationPlate),
                VehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(x.VehicleName, x.YearIssue, x.OrganizationClientName, x.RegistrationPlate, !x.HasCatalogIdentity),
                ActorDisplayName = OrganizationWorkJournalQueryService.ResolveJournalActorName(x.ActorDisplayName, x.Email, x.UserName, x.OriginalEmail, x.IsDeleted),
                MetaText = $"{OrganizationWorkspaceMaintenanceMapper.GetMaintenanceTypeName(x.MaintenanceType, scenarioCode)} • {OrganizationWorkspaceMaintenanceMapper.GetMaintenanceStatusName(x.MaintenanceStatus)}",
                DetailUrl = $"#maintenance/{x.MaintenanceId}",
                VehicleUrl = $"#vehicles/{x.VehicleId}"
            }));
        }

        if (hasDefectsModule && (string.IsNullOrWhiteSpace(selectedKind) || selectedKind == "defect"))
        {
            var defectHistoryEvents = await opencrmContext.VehicleHistoryEvents
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && x.RelatedEntityType == "defect"
                    && x.RelatedEntityId.HasValue
                    && x.CreatedAt >= fromUtc
                    && x.CreatedAt < toUtc
                    && (!vehicleId.HasValue || x.VehicleId == vehicleId.Value))
                .Select(x => new
                {
                    x.CreatedAt,
                    x.Title,
                    x.Description,
                    x.ActorDisplayName,
                    DefectId = x.RelatedEntityId!.Value,
                    x.VehicleId,
                    x.User.Email,
                    x.User.UserName,
                    x.User.OriginalEmail,
                    x.User.IsDeleted,
                    BrandName = x.Vehicle.BrandId.HasValue
                        ? (x.Vehicle.Brand != null ? x.Vehicle.Brand.Name : string.Empty)
                        : (x.Vehicle.ManualBrandName ?? string.Empty),
                    ModelName = x.Vehicle.ModelId.HasValue
                        ? (x.Vehicle.Model != null ? x.Vehicle.Model.Name : string.Empty)
                        : (x.Vehicle.ManualModelName ?? string.Empty),
                    VehicleName = x.Vehicle.Name,
                    x.Vehicle.YearIssue,
                    x.Vehicle.RegistrationPlate,
                    HasCatalogIdentity = x.Vehicle.BrandId.HasValue || x.Vehicle.ModelId.HasValue,
                    OrganizationClientName = x.Vehicle.OrganizationClient != null ? x.Vehicle.OrganizationClient.Name : null
                })
                .ToListAsync();

            items.AddRange(defectHistoryEvents.Select(x => new OrganizationWorkJournalItemDto
            {
                OccurredAt = x.CreatedAt,
                Kind = "defect",
                EventCode = "history",
                KindName = "Неисправность",
                Title = x.Title,
                Description = x.Description,
                VehicleDisplayName = Helpers.VehicleDisplayName.Format(x.BrandName, x.ModelName, x.RegistrationPlate),
                VehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(x.VehicleName, x.YearIssue, x.OrganizationClientName, x.RegistrationPlate, !x.HasCatalogIdentity),
                ActorDisplayName = OrganizationWorkJournalQueryService.ResolveJournalActorName(x.ActorDisplayName, x.Email, x.UserName, x.OriginalEmail, x.IsDeleted),
                DetailUrl = $"#defects/{x.DefectId}",
                VehicleUrl = $"#vehicles/{x.VehicleId}"
            }));

            var defectRows = await opencrmContext.VehicleDefects
                .AsNoTracking()
                .Where(x => x.OrganizationId == organizationId
                    && (!vehicleId.HasValue || x.VehicleId == vehicleId.Value)
                    && ((x.CreatedAt >= fromUtc && x.CreatedAt < toUtc)
                        || (x.ClosedAt.HasValue && x.ClosedAt.Value >= fromUtc && x.ClosedAt.Value < toUtc)))
                .Select(x => new
                {
                    x.Id,
                    x.VehicleId,
                    x.Title,
                    x.Description,
                    x.SeverityCode,
                    x.StatusCode,
                    x.CreatedAt,
                    x.ClosedAt,
                    x.User.Email,
                    x.User.UserName,
                    x.User.OriginalEmail,
                    x.User.IsDeleted,
                    BrandName = x.Vehicle.BrandId.HasValue
                        ? (x.Vehicle.Brand != null ? x.Vehicle.Brand.Name : string.Empty)
                        : (x.Vehicle.ManualBrandName ?? string.Empty),
                    ModelName = x.Vehicle.ModelId.HasValue
                        ? (x.Vehicle.Model != null ? x.Vehicle.Model.Name : string.Empty)
                        : (x.Vehicle.ManualModelName ?? string.Empty),
                    VehicleName = x.Vehicle.Name,
                    x.Vehicle.YearIssue,
                    x.Vehicle.RegistrationPlate,
                    HasCatalogIdentity = x.Vehicle.BrandId.HasValue || x.Vehicle.ModelId.HasValue,
                    OrganizationClientName = x.Vehicle.OrganizationClient != null ? x.Vehicle.OrganizationClient.Name : null
                })
                .ToListAsync();

            foreach (var defect in defectRows)
            {
                var vehicleDisplayName = Helpers.VehicleDisplayName.Format(defect.BrandName, defect.ModelName, defect.RegistrationPlate);
                var vehicleSubtitle = OrganizationWorkspaceMaintenanceMapper.BuildVehicleSubtitle(defect.VehicleName, defect.YearIssue, defect.OrganizationClientName, defect.RegistrationPlate, !defect.HasCatalogIdentity);
                var actorDisplayName = OrganizationWorkJournalQueryService.ResolveJournalActorName(null, defect.Email, defect.UserName, defect.OriginalEmail, defect.IsDeleted);
                var meta = $"{VehicleDefectSeverityCodes.GetName(defect.SeverityCode)} • {VehicleDefectStatusCodes.GetName(defect.StatusCode)}";

                if (defect.CreatedAt >= fromUtc && defect.CreatedAt < toUtc)
                {
                    items.Add(new OrganizationWorkJournalItemDto
                    {
                        OccurredAt = defect.CreatedAt,
                        Kind = "defect",
                        EventCode = "created",
                        KindName = "Неисправность создана",
                        Title = defect.Title,
                        Description = defect.Description,
                        VehicleDisplayName = vehicleDisplayName,
                        VehicleSubtitle = vehicleSubtitle,
                        ActorDisplayName = actorDisplayName,
                        MetaText = meta,
                        DetailUrl = $"#defects/{defect.Id}",
                        VehicleUrl = $"#vehicles/{defect.VehicleId}"
                    });
                }

                if (defect.ClosedAt.HasValue && defect.ClosedAt.Value >= fromUtc && defect.ClosedAt.Value < toUtc)
                {
                    items.Add(new OrganizationWorkJournalItemDto
                    {
                        OccurredAt = defect.ClosedAt.Value,
                        Kind = "defect",
                        EventCode = "closed",
                        KindName = "Неисправность закрыта",
                        Title = defect.Title,
                        Description = defect.Description,
                        VehicleDisplayName = vehicleDisplayName,
                        VehicleSubtitle = vehicleSubtitle,
                        ActorDisplayName = actorDisplayName,
                        MetaText = meta,
                        DetailUrl = $"#defects/{defect.Id}",
                        VehicleUrl = $"#vehicles/{defect.VehicleId}"
                    });
                }
            }
        }

        var orderedItems = items
            .OrderByDescending(item => ToOrganizationDateTime(item.OccurredAt, zone))
            .ThenBy(x => x.Title)
            .ToList();

        return new OrganizationWorkspaceWorkJournalResult
        {
            WorkItems = orderedWorkItems,
            HistoryItems = orderedItems,
            VehicleOptions = vehicleOptions,
            SelectedKind = selectedKind,
            SelectedTab = selectedTab,
            WorkItemsCount = orderedWorkItems.Count,
            HistoryItemsCount = orderedItems.Count,
            MaintenanceEventsCount = orderedItems.Count(x => x.Kind == "maintenance"),
            DefectsCreatedCount = orderedItems.Count(x => x.Kind == "defect" && x.KindName == "Неисправность создана"),
            DefectsClosedCount = orderedItems.Count(x => x.Kind == "defect" && x.KindName == "Неисправность закрыта"),
            CompletedMaintenancesCount = orderedWorkItems.Count(x => x.Kind == "maintenance"),
            ServedClientsCount = orderedWorkItems
                .Where(x => !string.IsNullOrWhiteSpace(x.ClientName))
                .Select(x => x.ClientName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
        };
    }

    /// <summary> Определяет дату свершения обслуживания по дате завершения и планируемому времени начала. </summary>
    private static DateTime BuildWorkJournalMaintenanceOccurredAt(DateTime completedDate, TimeSpan? plannedStartTime)
    {
        var value = completedDate.Date + (plannedStartTime ?? TimeSpan.Zero);

        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    private static DateTime ToOrganizationDateTime(DateTime value, TimeZoneInfo timeZone) =>
        value.Kind == DateTimeKind.Utc ? TimeZoneInfo.ConvertTimeFromUtc(value, timeZone) : value;

    /// <summary> Нормализует значение типа записи журнала работ. </summary>
    private static string NormalizeWorkJournalKind(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized is "maintenance" or "defect" ? normalized : string.Empty;
    }

    /// <summary> Нормализует значение вкладки журнала работ. </summary>
    private static string NormalizeWorkJournalTab(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();

        return normalized is "history" ? normalized : "works";
    }

    /// <summary> Определяет отображаемое название типа события обслуживания. </summary>
    private static string GetWorkJournalMaintenanceKindName(string? eventType) => eventType switch
    {
        "created" => "Обслуживание создано",
        "updated" => "Обслуживание изменено",
        "status_changed" => "Состояние изменено",
        "deleted" => "Обслуживание удалено",
        "defect_linked" => "Связана неисправность",
        "defect_unlinked" => "Связь снята",
        "fields_changed" => "Сведения изменены",
        "details_changed" => "Работы изменены",
        "document_generated" => "Документ сформирован",
        _ => "Обслуживание"
    };

    /// <summary> Определяет отображаемое имя автора действия по доступным данным. </summary>
    private static string ResolveJournalActorName(
        string? actorDisplayName,
        string? email,
        string? userName,
        string? originalEmail,
        bool isDeleted)
    {
        if (!string.IsNullOrWhiteSpace(actorDisplayName))
        {
            return actorDisplayName;
        }

        var resolvedName = !string.IsNullOrWhiteSpace(originalEmail)
            ? originalEmail
            : !string.IsNullOrWhiteSpace(email)
                ? email
                : userName;

        if (string.IsNullOrWhiteSpace(resolvedName))
        {
            return isDeleted ? "Удаленный пользователь" : "Пользователь не указан";
        }

        return isDeleted ? $"{resolvedName} (удален)" : resolvedName;
    }
}
