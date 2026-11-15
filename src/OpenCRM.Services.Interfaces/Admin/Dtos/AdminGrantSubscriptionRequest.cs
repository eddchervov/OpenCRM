namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Запрос на выдачу подписки с тарифом и длительностью. </summary>
public sealed record AdminGrantSubscriptionRequest(int PlanId, int Days, string? Reason);
