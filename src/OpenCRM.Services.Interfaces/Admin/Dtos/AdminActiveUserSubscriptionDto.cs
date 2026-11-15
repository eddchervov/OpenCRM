namespace OpenCRM.Services.Interfaces.Admin.Dtos;

/// <summary> Активная подписка пользователя с датой окончания. </summary>
public sealed record AdminActiveUserSubscriptionDto(int Id, DateTime EndDate);