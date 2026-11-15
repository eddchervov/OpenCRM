using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка списка биллинга. </summary>
public sealed class BillingRow
{
    /// <summary> Исходный объект данных строки. </summary>
    private readonly object _source;

    /// <summary> Создаёт строку из платежа. </summary>
    public BillingRow(AdminBillingPayment payment)
    {
        this._source = payment;
        this.Id = payment.Id;
        this.IsPayment = true;
        this.Customer = UserDisplayFormatting.Text(payment.User);
        this.Contact = UserDisplayFormatting.Text(payment.UserEmail ?? payment.UserPhone);
        this.Plan = UserDisplayFormatting.Text(payment.PlanName);
        this.Amount = UserDisplayFormatting.Money(payment.Amount);
        this.Date = UserDisplayFormatting.DateTime(payment.CreatedAt);
        this.EndDate = UserDisplayFormatting.DateTime(payment.StatusUpdatedAt);
        this.Status = UserDisplayFormatting.Text(payment.StatusName);
        this.HasAttention = payment.IsProblem;
        this.Source = UserDisplayFormatting.Text(payment.PaymentMethodName);
        this.SortDate = payment.CreatedAt;
        this.SortAmount = payment.Amount;
    }

    /// <summary> Создаёт строку из подписки организации. </summary>
    public BillingRow(AdminBillingOrganizationSubscription subscription)
    {
        this._source = subscription;
        this.Id = subscription.Id;
        this.IsPayment = false;
        this.Customer = UserDisplayFormatting.Text(subscription.OrganizationName);
        this.Contact = UserDisplayFormatting.Text(subscription.Owner);
        this.Plan = UserDisplayFormatting.Text(subscription.PlanName);
        this.Amount = UserDisplayFormatting.Text(subscription.Source);
        this.Date = UserDisplayFormatting.DateTime(subscription.StartDate);
        this.EndDate = UserDisplayFormatting.DateTime(subscription.EndDate);
        this.Status = BillingRow.SubscriptionStatus(subscription);
        this.HasAttention = subscription.IsExpiringSoon;
        this.Source = UserDisplayFormatting.Text(subscription.Source);
        this.SortDate = subscription.StartDate;
        this.SortAmount = 0;
    }

    /// <summary> Идентификатор записи. </summary>
    public int Id { get; }

    /// <summary> Признак того, что строка является платежом. </summary>
    public bool IsPayment { get; }

    /// <summary> Название плательщика. </summary>
    public string Customer { get; }

    /// <summary> Контакт плательщика. </summary>
    public string Contact { get; }

    /// <summary> Тарифный план. </summary>
    public string Plan { get; }

    /// <summary> Сумма. </summary>
    public string Amount { get; }

    /// <summary> Дата операции. </summary>
    public string Date { get; }

    /// <summary> Дата окончания подписки. </summary>
    public string EndDate { get; }

    /// <summary> Статус. </summary>
    public string Status { get; }

    /// <summary> Признак требующей внимания строки. </summary>
    public bool HasAttention { get; }

    /// <summary> Источник записи. </summary>
    public string Source { get; }

    /// <summary> Дата для сортировки. </summary>
    internal DateTime SortDate { get; }

    /// <summary> Сумма для сортировки. </summary>
    internal decimal SortAmount { get; }

    /// <summary> Исходный объект данных. </summary>
    internal object Data => this._source;

    /// <summary> Возвращает код статуса подписки организации. </summary>
    internal static string SubscriptionStatus(AdminBillingOrganizationSubscription subscription) => subscription.IsActive
        ? "Активна"
        : subscription.StartDate > DateTime.UtcNow ? "Ещё не началась" : "Завершена";
}
