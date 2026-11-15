namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на продление подписки пользователю. </summary>
public sealed record AdminExtendSubscriptionRequest(int Days, string? Reason, DateTime? ExpectedEndDate);

