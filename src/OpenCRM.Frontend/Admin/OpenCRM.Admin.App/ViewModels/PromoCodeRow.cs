using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка промокодов. </summary>
public sealed class PromoCodeRow(AdminPromoCode promoCode)
{
    public AdminPromoCode PromoCode { get; } = promoCode;
    public int Id => this.PromoCode.Id;
    public string Code => this.PromoCode.Code;
    public string Name => this.PromoCode.Name;
    public string Benefit => this.PromoCode.IsOrganizationCode
        ? this.PromoCode.DiscountTypeCode switch
        {
            "bonus_days" => $"Подарок: {this.PromoCode.BonusDays ?? 0} дн.",
            "percentage" => $"Скидка: {this.PromoCode.DiscountValue}%",
            "fixed_amount" => $"Скидка: {this.PromoCode.DiscountValue:N2} ₽",
            _ => "Организационный промокод"
        }
        : this.PromoCode.DemoDurationDays is int days
            ? $"Старый регистрационный бонус: {days} дн."
            : $"Регистрационная скидка: {this.PromoCode.DiscountValue:N2}";
    public string UsageCount => this.PromoCode.IsOrganizationCode
        ? this.PromoCode.MaximumOrganizationApplications is int organizationLimit
            ? $"{this.PromoCode.OrganizationApplicationCount} из {organizationLimit} применений"
            : $"{this.PromoCode.OrganizationApplicationCount} применений"
        : this.PromoCode.MaximumRedemptions is int redemptionLimit
            ? $"{this.PromoCode.RedemptionCount} из {redemptionLimit} регистраций"
            : $"{this.PromoCode.RedemptionCount} регистраций";
    public string ValidUntil => this.PromoCode.ValidUntil is DateTime date
        ? $"До {date.ToLocalTime():dd.MM.yyyy HH:mm}"
        : "Без срока";
    public string Status => this.PromoCode.IsActive ? "Активен" : "Отключён";
}
