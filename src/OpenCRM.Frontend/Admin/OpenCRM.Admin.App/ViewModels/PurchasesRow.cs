using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

public sealed class PurchasesRow(AdminBillingPurchase purchase) : ObservableObject
{
    public AdminBillingPurchase Purchase { get; } = purchase;

    public int Id => this.Purchase.PaymentId;

    public string Buyer => string.Join(" · ", new[]
    {
        this.Purchase.User, this.Purchase.UserEmail, this.Purchase.BuyerName, this.Purchase.BuyerInn
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public string BuyerType => this.Purchase.BuyerType switch { "business" => AdminLocalization.Instance["native.billing.organizatsiya.ip"], "individual" => AdminLocalization.Instance["native.billing.fizicheskoe.litso"], _ => AdminLocalization.Instance["native.billing.tip.ne.ukazan"] };

    public string Plan => this.Purchase.PlanName;

    public string PaidAt => UserDisplayFormatting.DateTime(this.Purchase.PaidAt);

    public decimal Refund => this.Purchase.PaymentStatus == "refunded" && this.Purchase.RefundedAmount == 0 ? this.Purchase.Amount : this.Purchase.RefundedAmount;

    public bool IsRefunded => this.Purchase.RefundedAmount >= this.Purchase.Amount || this.Purchase.PaymentStatus.Contains("refund", StringComparison.OrdinalIgnoreCase);

    public bool IsOverdue => !this.IsRefunded && !this.Purchase.NpdReceiptIssued && DateTime.UtcNow - this.Purchase.PaidAt.ToUniversalTime() > TimeSpan.FromHours(24);

    public bool NeedsAttention => !this.IsRefunded && (!this.Purchase.NpdReceiptIssued || this.Purchase.NpdReceiptDeliveryStatus == "failed" || this.Purchase.PaymentStatus is "failed" or "processing");

    public string Amount => AdminLocalization.Instance.Format("native.billing.userdisplayformatting.money.this.purchase.amount.vozvrat.userdis", UserDisplayFormatting.Money(this.Purchase.Amount), UserDisplayFormatting.Money(this.Refund))
        + AdminLocalization.Instance.Format("native.billing.itogo.userdisplayformatting.money.this.purchase.amount.this.refu", UserDisplayFormatting.Money(this.Purchase.Amount - this.Refund));

    public string Receipt => this.Purchase.NpdReceiptIssued ? AdminLocalization.Instance["native.billing.vydan"] : this.IsOverdue ? AdminLocalization.Instance["native.billing.ne.otmechen.bolee.24.ch"] : AdminLocalization.Instance["native.billing.ne.otmechen"];

    public string Delivery => this.Purchase.NpdReceiptDeliveryStatus switch
    {
        "queued" => AdminLocalization.Instance["native.billing.v.ocheredi"], "sent" => AdminLocalization.Instance["native.billing.prinyat.pochtovym.servisom"], "delivered" => AdminLocalization.Instance["native.billing.dostavlen"],
        "failed" => AdminLocalization.Instance["native.billing.oshibka.otpravki"], "simulated" => AdminLocalization.Instance["native.billing.simulyatsiya.otpravki"], _ => AdminLocalization.Instance["native.billing.ne.otpravlen"]
    };
    public string PaymentStatus => this.Purchase.PaymentStatus switch
    {
        "completed" => AdminLocalization.Instance["native.billing.oplachen"], "refunded" => AdminLocalization.Instance["native.billing.vozvrashchyon"], "failed" => AdminLocalization.Instance["native.billing.oshibka.aktivatsii"], "processing" => AdminLocalization.Instance["native.billing.aktivatsiya"], _ => this.Purchase.PaymentStatus
    };
    public string ReceiptHistory => string.Join(" · ", new[]
    {
        this.Purchase.NpdReceiptIssuedAt.HasValue ? AdminLocalization.Instance.Format("native.billing.otmetka.userdisplayformatting.datetime.this.purchase.npdreceipti", UserDisplayFormatting.DateTime(this.Purchase.NpdReceiptIssuedAt)) : null,
        this.Purchase.NpdReceiptMarkedBy, this.Purchase.NpdReceiptReason,
        this.Purchase.NpdReceiptSentAt.HasValue ? AdminLocalization.Instance.Format("native.billing.otpravka.userdisplayformatting.datetime.this.purchase.npdreceipt", UserDisplayFormatting.DateTime(this.Purchase.NpdReceiptSentAt)) : null,
        this.Purchase.NpdReceiptSentTo, this.Purchase.NpdReceiptSendError
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
