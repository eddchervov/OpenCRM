using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка истории применения промокода. </summary>
public sealed class PromoCodeUsageRow(AdminPromoCodeUsage usage, bool isLegacy, string benefitType)
{
    public AdminPromoCodeUsage Usage { get; } = usage;
    public string Email => this.Usage.Email;
    public string Organization => this.Usage.OrganizationName ?? "—";
    public string AppliedAt => this.Usage.RedeemedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string Details => string.Join(" · ", new[]
    {
        $"Источник: {this.Usage.SourceCode}",
        this.Usage.AssignedAt is DateTime assignedAt ? $"Назначен: {assignedAt.ToLocalTime():dd.MM.yyyy HH:mm}" : null,
        this.Usage.DemoAppliedAt is DateTime demoAppliedAt ? $"Демо: {demoAppliedAt.ToLocalTime():dd.MM.yyyy HH:mm}" : null,
        this.Usage.SubscriptionPaymentId is int paymentId ? $"Платёж #{paymentId}" : null
    }.Where(value => value != null));
    public string Status => this.Usage.ApplicationStatus switch
    {
        "consumed" => "Выгода получена",
        "claimed" => "В платеже",
        "applied" => "Применён",
        null or "" => "Промокод погашен",
        _ => this.Usage.ApplicationStatus
    };
    public string DiscountAvailability => isLegacy || benefitType == "bonus_days"
        ? "—"
        : this.Usage.ApplicationStatus switch
        {
            "consumed" => "Использована",
            "claimed" => "В платеже",
            _ when this.Usage.DiscountValidUntil is DateTime validUntil => validUntil.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
            _ => "Без срока"
        };
}
