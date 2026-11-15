namespace OpenCRM.Modules.Maintenance.Contracts.Dtos;

/// <summary> Занятый интервал поста на выбранную дату. </summary>
public sealed record OccupiedMaintenanceSlotDto(
    Guid Id,
    double StartMinute,
    double EndMinute,
    string Vehicle,
    string? Service,
    DateTime? StartAt = null,
    DateTime? EndAt = null);
