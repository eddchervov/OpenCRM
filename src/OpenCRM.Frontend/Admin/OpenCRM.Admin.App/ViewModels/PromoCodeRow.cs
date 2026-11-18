using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка промокодов. </summary>
public sealed class PromoCodeRow(AdminPromoCode promoCode) : ObservableObject
{
    public AdminPromoCode PromoCode { get; } = promoCode;
    public int Id => this.PromoCode.Id;
    public string Code => this.PromoCode.Code;
    public string Name => this.PromoCode.Name;
    public string Benefit => this.PromoCode.IsOrganizationCode
        ? this.PromoCode.DiscountTypeCode switch
        {
            "bonus_days" => AdminLocalization.Instance.Format("native.promoCodes.podarok.this.promocode.bonusdays.0.dn", this.PromoCode.BonusDays ?? 0),
            "percentage" => AdminLocalization.Instance.Format("native.promoCodes.skidka.this.promocode.discountvalue", this.PromoCode.DiscountValue),
            "fixed_amount" => AdminLocalization.Instance.Format("native.promoCodes.skidka.this.promocode.discountvalue.n2", this.PromoCode.DiscountValue),
            _ => AdminLocalization.Instance["native.promoCodes.organizatsionnyy.promokod"]
        }
        : this.PromoCode.DemoDurationDays is int days
            ? AdminLocalization.Instance.Format("native.promoCodes.staryy.registratsionnyy.bonus.days.dn", days)
            : AdminLocalization.Instance.Format("native.promoCodes.registratsionnaya.skidka.this.promocode.discountvalue.n2", this.PromoCode.DiscountValue);
    public string UsageCount => this.PromoCode.IsOrganizationCode
        ? this.PromoCode.MaximumOrganizationApplications is int organizationLimit
            ? AdminLocalization.Instance.Format("native.promoCodes.this.promocode.organizationapplicationcount.iz.organizationlimit", this.PromoCode.OrganizationApplicationCount, organizationLimit)
            : AdminLocalization.Instance.Format("native.promoCodes.this.promocode.organizationapplicationcount.primeneniy", this.PromoCode.OrganizationApplicationCount)
        : this.PromoCode.MaximumRedemptions is int redemptionLimit
            ? AdminLocalization.Instance.Format("native.promoCodes.this.promocode.redemptioncount.iz.redemptionlimit.registratsiy", this.PromoCode.RedemptionCount, redemptionLimit)
            : AdminLocalization.Instance.Format("native.promoCodes.this.promocode.redemptioncount.registratsiy", this.PromoCode.RedemptionCount);
    public string ValidUntil => this.PromoCode.ValidUntil is DateTime date
        ? AdminLocalization.Instance.Format("native.promoCodes.do.date.tolocaltime.dd.mm.yyyy.hh.mm", date.ToLocalTime())
        : AdminLocalization.Instance["native.promoCodes.bez.sroka"];
    public string Status => this.PromoCode.IsActive ? AdminLocalization.Instance["native.plans.aktiven"] : AdminLocalization.Instance["native.plans.otklyuchyon"];

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
