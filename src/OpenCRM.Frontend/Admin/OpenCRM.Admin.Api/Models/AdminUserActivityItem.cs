namespace OpenCRM.Admin.Api.Models;

/// <summary> Запись о действии пользователя. </summary>
public sealed record AdminUserActivityItem(
    long Id,
    string UserId,
    string UserLabel,
    Guid? OrganizationId,
    string? OrganizationName,
    Guid SessionId,
    DateTime OccurredAt,
    DateTime ReceivedAt,
    string Kind,
    string Action,
    string Page,
    string Scenario,
    string Section,
    string? TargetId);
