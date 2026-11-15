namespace OpenCRM.Modules.Promotions.Contracts;

/// <summary>
/// Порт выдачи доступа организации по подарочному промокоду.
/// Требует транзакцию вызывающей стороны.
/// Владелец Billing: доступ выдаётся на тарифный план без оплаты и без чтения скидок.
/// </summary>
public interface IOrganizationPromoAccessGrantPort
{
    /// <summary> Выдаёт организации доступ по подарочному промокоду. </summary>
    Task<DateTime> GrantAsync(Guid organizationId, string actorUserId, int planId, int days, long applicationId);
}
