using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Work;

/// <summary> Методы сопоставления данных работ обслуживания для DTO. </summary>
internal static class MaintenanceWorkMapping
{
    /// <summary> Определяет, назначена ли строка работы сотруднику. </summary>
    internal static bool IsAssignedTo(MaintenanceEntity maintenance, MaintenanceDetail detail, Guid employeeId) =>
        (detail.EmployeeId ?? maintenance.EmployeeId) is Guid assignedEmployeeId && assignedEmployeeId == employeeId;

    /// <summary> Преобразует отчёт о работе в DTO с именем сотрудника. </summary>
    internal static MaintenanceWorkReportDto MapReport(MaintenanceWorkReport report, string employeeName) => new()
    {
        Id = report.Id,
        MaintenanceId = report.MaintenanceId,
        DetailId = report.DetailId,
        Kind = report.Kind,
        Text = report.Text,
        EmployeeName = employeeName,
        CreatedAt = report.CreatedAt,
        Decision = report.Decision,
        DecisionReason = report.DecisionReason,
        DecidedAt = report.DecidedAt,
        ApprovedPrice = report.ApprovedPrice,
        TimeAdjustmentMinutes = report.TimeAdjustmentMinutes
    };

    /// <summary> Определяет, является ли строка работы строкой материала. </summary>
    internal static bool IsMaterial(MaintenanceDetail detail) =>
        detail.MaterialCatalogItemId.HasValue || detail.MaterialName != null || detail.MaterialQuantity.HasValue;

    /// <summary> Преобразует строку работы в DTO с учётом операции и интервалов. </summary>
    internal static MaintenanceWorkItemDto MapItem(
        MaintenanceEntity maintenance,
        MaintenanceDetail detail,
        MaintenanceWorkOperation? operation,
        IEnumerable<MaintenanceWorkInterval> intervals,
        DateTime now,
        MaintenanceWorkStatus? status = null,
        uint? version = null,
        int adjustmentMinutes = 0,
        string? reworkReason = null)
    {
        var activeTime = intervals.Where(interval => interval.StartedAt <= now)
            .Aggregate(TimeSpan.Zero, (total, interval) =>
                total + ((interval.EndedAt.HasValue && interval.EndedAt.Value < now ? interval.EndedAt.Value : now)
                    - interval.StartedAt));
        var effectiveStatus = status ?? operation?.Status ?? MaintenanceWorkStatus.Assigned;
        activeTime += TimeSpan.FromMinutes(adjustmentMinutes);

        return new MaintenanceWorkItemDto
        {
            MaintenanceId = maintenance.Id,
            DetailId = detail.DetailId,
            Name = detail.Name,
            Description = detail.Description,
            Price = detail.Price,
            CurrencyCode = maintenance.CurrencyCode,
            Quantity = detail.Quantity,
            VehicleName = maintenance.Vehicle.Name,
            RegistrationPlate = maintenance.Vehicle.RegistrationPlate,
            Location = maintenance.Location,
            PlannedDate = maintenance.PlannedDate,
            PlannedStartTime = maintenance.PlannedStartTime,
            PlannedEndDate = maintenance.PlannedEndDate,
            PlannedEndTime = maintenance.PlannedEndTime,
            IntakeNotes = maintenance.IntakeNotes,
            IntakeCompleteness = maintenance.IntakeCompleteness,
            IntakeVisibleDamage = maintenance.IntakeVisibleDamage,
            WorkComment = maintenance.Comment,
            ReworkReason = reworkReason,
            Status = effectiveStatus,
            Version = version ?? operation?.Version ?? 0,
            StartedAt = operation?.StartedAt,
            CompletedAt = effectiveStatus == MaintenanceWorkStatus.Completed
                ? status.HasValue ? now : operation!.CompletedAt
                : null,
            ActiveTime = activeTime
        };
    }
}
