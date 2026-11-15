namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на действие с подпиской в админ-панели. </summary>
public sealed record AdminSubscriptionActionRequest(string? Reason, DateTime? ExpectedEndDate = null);
