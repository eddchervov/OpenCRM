using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public sealed class PurchasesRow(AdminBillingPurchase purchase)
{
    public AdminBillingPurchase Purchase { get; } = purchase;

    public int Id => this.Purchase.PaymentId;

    public string Buyer => string.Join(" · ", new[]
    {
        this.Purchase.User, this.Purchase.UserEmail, this.Purchase.BuyerName, this.Purchase.BuyerInn
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string BuyerType => this.Purchase.BuyerType switch { "business" => "Организация / ИП", "individual" => "Физическое лицо", _ => "Тип не указан" };

    public string Plan => this.Purchase.PlanName;

    public string PaidAt => UserDisplayFormatting.DateTime(this.Purchase.PaidAt);

    public decimal Refund => this.Purchase.PaymentStatus == "refunded" && this.Purchase.RefundedAmount == 0 ? this.Purchase.Amount : this.Purchase.RefundedAmount;

    public bool IsRefunded => this.Purchase.RefundedAmount >= this.Purchase.Amount || this.Purchase.PaymentStatus.Contains("refund", StringComparison.OrdinalIgnoreCase);

    public bool IsOverdue => !this.IsRefunded && !this.Purchase.NpdReceiptIssued && DateTime.UtcNow - this.Purchase.PaidAt.ToUniversalTime() > TimeSpan.FromHours(24);

    public bool NeedsAttention => !this.IsRefunded && (!this.Purchase.NpdReceiptIssued || this.Purchase.NpdReceiptDeliveryStatus == "failed" || this.Purchase.PaymentStatus is "failed" or "processing");

    public string Amount => $"{UserDisplayFormatting.Money(this.Purchase.Amount)} · возврат {UserDisplayFormatting.Money(this.Refund)}"
        + $" · итого {UserDisplayFormatting.Money(this.Purchase.Amount - this.Refund)}";

    public string Receipt => this.Purchase.NpdReceiptIssued ? "Выдан" : this.IsOverdue ? "Не отмечен · более 24 ч" : "Не отмечен";

    public string Delivery => this.Purchase.NpdReceiptDeliveryStatus switch
    {
        "queued" => "В очереди", "sent" => "Принят почтовым сервисом", "delivered" => "Доставлен",
        "failed" => "Ошибка отправки", "simulated" => "Симуляция отправки", _ => "Не отправлен"
    };
    public string PaymentStatus => this.Purchase.PaymentStatus switch
    {
        "completed" => "Оплачен", "refunded" => "Возвращён", "failed" => "Ошибка активации", "processing" => "Активация", _ => this.Purchase.PaymentStatus
    };
    public string ReceiptHistory => string.Join(" · ", new[]
    {
        this.Purchase.NpdReceiptIssuedAt.HasValue ? $"Отметка: {UserDisplayFormatting.DateTime(this.Purchase.NpdReceiptIssuedAt)}" : null,
        this.Purchase.NpdReceiptMarkedBy, this.Purchase.NpdReceiptReason,
        this.Purchase.NpdReceiptSentAt.HasValue ? $"Отправка: {UserDisplayFormatting.DateTime(this.Purchase.NpdReceiptSentAt)}" : null,
        this.Purchase.NpdReceiptSentTo, this.Purchase.NpdReceiptSendError
    }.Where(value => !string.IsNullOrWhiteSpace(value)));
}
