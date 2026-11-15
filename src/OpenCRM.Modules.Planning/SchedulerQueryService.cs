using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Planning.Contracts;
using OpenCRM.Modules.Planning.Contracts.Dtos;

namespace OpenCRM.Modules.Planning;

/// <summary> Сервис чтения данных планировщика организации. </summary>
internal sealed class SchedulerQueryService(OpenCRMContext context, SchedulerAccess access) : ISchedulerQueryService
{
    /// <summary> Возвращает наступившие напоминания задач планировщика. </summary>
    public async Task<IReadOnlyList<OrganizationSchedulerReminderDto>> GetDueRemindersAsync(
        string userId, Guid organizationId, DateTime now, int take = 20)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanReadAsync(userId, organizationId);
        var limit = Math.Clamp(take, 1, 100);
        var normalizedNow = now.ToUniversalTime();
        var rows = await context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && !value.IsDeleted
                && value.ReminderAt.HasValue && value.ReminderAt.Value <= normalizedNow
                && value.ReminderSentAt == null && value.StatusCode != OrganizationSchedulerTaskStatusCodes.Closed
                && (value.ResponsibleUserId == null || value.ResponsibleUserId == userId || value.CreatedByUserId == userId))
            .OrderBy(value => value.ReminderAt)
            .ThenBy(value => value.StartAt)
            .Take(limit)
            .Select(value => new
            {
                value.Id,
                value.OrganizationId,
                OrganizationName = value.Organization.Name,
                value.VehicleId,
                value.OrganizationClientId,
                value.ResponsibleUserId,
                value.Title,
                value.Comment,
                ReminderAt = value.ReminderAt!.Value,
                value.StartAt,
                value.EndAt,
                VehicleBrandName = value.Vehicle != null
                    ? (value.Vehicle.BrandId.HasValue ? (value.Vehicle.Brand != null ? value.Vehicle.Brand.Name : string.Empty) : value.Vehicle.ManualBrandName ?? string.Empty)
                    : string.Empty,
                VehicleModelName = value.Vehicle != null
                    ? (value.Vehicle.ModelId.HasValue ? (value.Vehicle.Model != null ? value.Vehicle.Model.Name : string.Empty) : value.Vehicle.ManualModelName ?? string.Empty)
                    : string.Empty,
                VehicleName = value.Vehicle != null ? value.Vehicle.Name : null,
                VehicleRegistrationPlate = value.Vehicle != null ? value.Vehicle.RegistrationPlate : null,
                ClientName = value.OrganizationClient != null ? value.OrganizationClient.Name : null
            })
            .ToListAsync();

        return rows.Select(value =>
        {
            var vehicleTitle = value.VehicleId.HasValue
                ? Helpers.VehicleDisplayName.Format(value.VehicleBrandName, value.VehicleModelName, value.VehicleRegistrationPlate)
                : null;
            var vehicleParts = new[] { vehicleTitle, value.VehicleName }
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim())
                .ToList();

            return new OrganizationSchedulerReminderDto
            {
                TaskId = value.Id,
                OrganizationId = value.OrganizationId,
                OrganizationName = value.OrganizationName,
                VehicleId = value.VehicleId,
                OrganizationClientId = value.OrganizationClientId,
                ResponsibleUserId = value.ResponsibleUserId,
                Title = value.Title,
                Comment = value.Comment,
                ReminderAt = value.ReminderAt,
                StartAt = value.StartAt,
                EndAt = value.EndAt,
                VehicleDisplayName = vehicleParts.Count == 0 ? null : string.Join(" • ", vehicleParts),
                ClientName = value.ClientName,
                DetailUrl = $"#scheduler?task={value.Id}"
            };
        }).ToList();
    }

    /// <summary> Возвращает элементы календаря ручных задач за период с фильтрами. </summary>
    public async Task<IReadOnlyList<OrganizationSchedulerCalendarItemDto>> GetManualTaskCalendarAsync(
        string userId, Guid organizationId, DateTime dateFrom, DateTime dateTo, Guid? vehicleId, Guid? clientId,
        string? responsibleUserId, string? taskType, string? priority)
    {
        await access.EnsureSchedulerModuleEnabledAsync(userId, organizationId);
        await access.EnsureCanReadAsync(userId, organizationId);
        var from = dateFrom.Date;
        var to = dateTo.Date.AddDays(1);
        var normalizedResponsible = SchedulerRules.NormalizeNullable(responsibleUserId);
        var normalizedTaskType = SchedulerRules.NormalizeNullable(taskType);
        var normalizedPriority = SchedulerRules.NormalizeNullable(priority);
        var query = context.OrganizationSchedulerTasks.AsNoTracking()
            .Where(value => value.OrganizationId == organizationId && !value.IsDeleted && value.StartAt.HasValue
                && (value.EndAt ?? value.StartAt.Value.AddHours(1)) > from && value.StartAt.Value < to);
        if (vehicleId.HasValue)
        {
            query = query.Where(value => value.VehicleId == vehicleId.Value);
        }
        if (clientId.HasValue)
        {
            query = query.Where(value => value.OrganizationClientId == clientId.Value);
        }
        if (!string.IsNullOrWhiteSpace(normalizedResponsible))
        {
            query = normalizedResponsible == "__none"
                ? query.Where(value => value.ResponsibleUserId == null || value.ResponsibleUserId == string.Empty)
                : query.Where(value => value.ResponsibleUserId == normalizedResponsible);
        }
        if (!string.IsNullOrWhiteSpace(normalizedTaskType))
        {
            query = query.Where(value => value.TypeCode == OrganizationSchedulerTaskTypeCodes.Normalize(normalizedTaskType));
        }
        if (!string.IsNullOrWhiteSpace(normalizedPriority))
        {
            query = query.Where(value => value.PriorityCode == OrganizationSchedulerTaskPriorityCodes.Normalize(normalizedPriority));
        }
        var rows = await query.Select(value => new
            {
                value.Id, value.VehicleId, value.Title, value.Comment, value.TypeCode, value.PriorityCode,
                value.StatusCode, value.RecurrenceCode, value.ReminderAt, value.LinkedDefectId, value.LinkedMaintenanceId,
                value.StartAt, value.EndAt,
                VehicleBrandName = value.Vehicle != null
                    ? (value.Vehicle.BrandId.HasValue ? (value.Vehicle.Brand != null ? value.Vehicle.Brand.Name : string.Empty) : value.Vehicle.ManualBrandName ?? string.Empty)
                    : string.Empty,
                VehicleModelName = value.Vehicle != null
                    ? (value.Vehicle.ModelId.HasValue ? (value.Vehicle.Model != null ? value.Vehicle.Model.Name : string.Empty) : value.Vehicle.ManualModelName ?? string.Empty)
                    : string.Empty,
                VehicleName = value.Vehicle != null ? value.Vehicle.Name : null,
                VehicleRegistrationPlate = value.Vehicle != null ? value.Vehicle.RegistrationPlate : null,
                ClientName = value.OrganizationClient != null ? value.OrganizationClient.Name : null
            })
            .OrderBy(value => value.StartAt)
            .ThenBy(value => value.Title)
            .ToListAsync();

        return rows.Select(value =>
        {
            var start = value.StartAt!.Value;
            var end = value.EndAt ?? start.AddHours(1);
            var subtitle = SchedulerRules.BuildCalendarSubtitle(value.VehicleId.HasValue
                ? Helpers.VehicleDisplayName.Format(value.VehicleBrandName, value.VehicleModelName, value.VehicleRegistrationPlate)
                : null, value.VehicleName, value.ClientName);

            return new OrganizationSchedulerCalendarItemDto
            {
                Id = value.Id,
                ItemKind = "task",
                VehicleId = value.VehicleId,
                Title = value.Title,
                Subtitle = subtitle,
                TypeName = $"{OrganizationSchedulerTaskTypeCodes.GetName(value.TypeCode)} • {OrganizationSchedulerTaskPriorityCodes.GetName(value.PriorityCode)}",
                StatusName = OrganizationSchedulerTaskStatusCodes.GetName(value.StatusCode),
                StatusBadgeClass = SchedulerRules.GetTaskStatusBadgeClass(value.StatusCode),
                Date = start.Date,
                PlannedStartTime = start.ToString("HH:mm"),
                PlannedEndDate = end.Date,
                PlannedEndTime = end.ToString("HH:mm"),
                Comment = SchedulerRules.BuildCalendarComment(value.Comment, value.RecurrenceCode, value.ReminderAt, value.LinkedDefectId, value.LinkedMaintenanceId),
                DetailUrl = $"#scheduler?task={value.Id}"
            };
        }).ToList();
    }
}
