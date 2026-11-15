namespace OpenCRM.Modules.Maintenance.Contracts;

/// <summary> Связь обслуживания с источником в планировщике. </summary>
public sealed record MaintenanceSchedulerLink(string? SourceCode, Guid? SourceEntityId, string? TriggerKey);
