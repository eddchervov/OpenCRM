using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка биллинга. </summary>
public sealed class BillingRow : ObservableObject
{
    /// <summary> Исходный объект данных строки. </summary>
    private readonly object _source;

    /// <summary> Создаёт строку из платежа. </summary>
    public BillingRow(AdminBillingPayment payment)
    {
        this._source = payment;
        this.Id = payment.Id;
        this.IsPayment = true;
        this.HasAttention = payment.IsProblem;
        this.SortDate = payment.CreatedAt;
        this.SortAmount = payment.Amount;
        this.UpdateDisplay();
    }

    /// <summary> Создаёт строку из подписки организации. </summary>
    public BillingRow(AdminBillingOrganizationSubscription subscription)
    {
        this._source = subscription;
        this.Id = subscription.Id;
        this.IsPayment = false;
        this.HasAttention = subscription.IsExpiringSoon;
        this.SortDate = subscription.StartDate;
        this.SortAmount = 0;
        this.UpdateDisplay();
    }

    /// <summary> Идентификатор записи. </summary>
    public int Id { get; }

    /// <summary> Признак того, что строка является платежом. </summary>
    public bool IsPayment { get; }

    /// <summary> Название плательщика. </summary>
    public string Customer { get; private set; } = string.Empty;

    /// <summary> Контакт плательщика. </summary>
    public string Contact { get; private set; } = string.Empty;

    /// <summary> Тарифный план. </summary>
    public string Plan { get; private set; } = string.Empty;

    /// <summary> Сумма. </summary>
    public string Amount { get; private set; } = string.Empty;

    /// <summary> Дата операции. </summary>
    public string Date { get; private set; } = string.Empty;

    /// <summary> Дата окончания подписки. </summary>
    public string EndDate { get; private set; } = string.Empty;

    /// <summary> Статус. </summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary> Признак требующей внимания строки. </summary>
    public bool HasAttention { get; }

    /// <summary> Источник записи. </summary>
    public string Source { get; private set; } = string.Empty;

    /// <summary> Дата для сортировки. </summary>
    internal DateTime SortDate { get; }

    /// <summary> Сумма для сортировки. </summary>
    internal decimal SortAmount { get; }

    /// <summary> Исходный объект данных. </summary>
    internal object Data => this._source;

    public void RefreshLocalizedValues()
    {
        this.UpdateDisplay();
        this.OnPropertyChanged((string?)null);
    }

    /// <summary> Возвращает код статуса подписки организации. </summary>
    internal static string SubscriptionStatus(AdminBillingOrganizationSubscription subscription) => subscription.IsActive
        ? AdminLocalization.Instance["native.billing.aktivna"]
        : subscription.StartDate > DateTime.UtcNow ? AdminLocalization.Instance["native.billing.eshchyo.ne.nachalas"] : AdminLocalization.Instance["native.billing.zavershena"];

    private void UpdateDisplay()
    {
        if (this._source is AdminBillingPayment payment)
        {
            this.Customer = UserDisplayFormatting.Text(payment.User);
            this.Contact = UserDisplayFormatting.Text(payment.UserEmail ?? payment.UserPhone);
            this.Plan = UserDisplayFormatting.Text(payment.PlanName);
            this.Amount = UserDisplayFormatting.Money(payment.Amount);
            this.Date = UserDisplayFormatting.DateTime(payment.CreatedAt);
            this.EndDate = UserDisplayFormatting.DateTime(payment.StatusUpdatedAt);
            this.Status = AdminDisplayLabels.PaymentStatus(payment.PaymentStatus ?? string.Empty, payment.StatusName, payment.ApplicationStatus);
            this.Source = AdminDisplayLabels.PaymentMethod(payment.PaymentMethod ?? string.Empty, payment.PaymentMethodName);

            return;
        }

        var subscription = (AdminBillingOrganizationSubscription)this._source;
        this.Customer = UserDisplayFormatting.Text(subscription.OrganizationName);
        this.Contact = UserDisplayFormatting.Text(subscription.Owner);
        this.Plan = UserDisplayFormatting.Text(subscription.PlanName);
        this.Amount = UserDisplayFormatting.Text(subscription.Source);
        this.Date = UserDisplayFormatting.DateTime(subscription.StartDate);
        this.EndDate = UserDisplayFormatting.DateTime(subscription.EndDate);
        this.Status = BillingRow.SubscriptionStatus(subscription);
        this.Source = UserDisplayFormatting.Text(subscription.Source);
    }
}
