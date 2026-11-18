using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка истории применения промокода. </summary>
public sealed class PromoCodeUsageRow(AdminPromoCodeUsage usage, bool isLegacy, string benefitType) : ObservableObject
{
    public AdminPromoCodeUsage Usage { get; } = usage;
    public string Email => this.Usage.Email;
    public string Organization => this.Usage.OrganizationName ?? "—";
    public string AppliedAt => this.Usage.RedeemedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string Details => string.Join(" · ", new[]
    {
        AdminLocalization.Instance.Format("native.promoCodes.istochnik.this.usage.sourcecode", this.Usage.SourceCode),
        this.Usage.AssignedAt is DateTime assignedAt ? AdminLocalization.Instance.Format("native.promoCodes.naznachen.assignedat.tolocaltime.dd.mm.yyyy.hh.mm", assignedAt.ToLocalTime()) : null,
        this.Usage.DemoAppliedAt is DateTime demoAppliedAt ? AdminLocalization.Instance.Format("native.promoCodes.demo.demoappliedat.tolocaltime.dd.mm.yyyy.hh.mm", demoAppliedAt.ToLocalTime()) : null,
        this.Usage.SubscriptionPaymentId is int paymentId ? AdminLocalization.Instance["native.promoCodes.platyozh.paymentid"] : null
    }.Where(value => value != null));
    public string Status => this.Usage.ApplicationStatus switch
    {
        "consumed" => AdminLocalization.Instance["native.promoCodes.vygoda.poluchena"],
        "claimed" => AdminLocalization.Instance["native.promoCodes.v.platezhe"],
        "applied" => AdminLocalization.Instance["native.promoCodes.primenyon"],
        null or "" => AdminLocalization.Instance["native.promoCodes.promokod.pogashen"],
        _ => this.Usage.ApplicationStatus
    };
    public string DiscountAvailability => isLegacy || benefitType == "bonus_days"
        ? "—"
        : this.Usage.ApplicationStatus switch
        {
            "consumed" => AdminLocalization.Instance["native.promoCodes.ispolzovana"],
            "claimed" => AdminLocalization.Instance["native.promoCodes.v.platezhe"],
            _ when this.Usage.DiscountValidUntil is DateTime validUntil => validUntil.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            _ => AdminLocalization.Instance["native.promoCodes.bez.sroka"]
        };

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
