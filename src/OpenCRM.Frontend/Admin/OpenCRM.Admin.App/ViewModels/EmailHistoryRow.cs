using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

/// <summary> Строка истории email с датами и состоянием доставки. </summary>
public sealed class EmailHistoryRow(EmailMessageListItem message) : ObservableObject
{
    public int Id => message.Id;
    public string Recipient => message.ToEmail;
    public string Subject => message.Subject;
    public string CreatedAt => UserDisplayFormatting.DateTime(message.CreatedAt);
    public string SentAt => UserDisplayFormatting.DateTime(message.SentAt);
    public string Status => EmailHistoryRow.StatusName(message.StatusCode);
    public string Activity => AdminLocalization.Instance.Format("native.email.otkrytiya.message.opencount.perekhody.message.clickcount.sobytiy", message.OpenCount, message.ClickCount, message.EventCount);
    public string Error => message.ErrorMessage ?? string.Empty;

    public static string StatusName(string? status) => status switch
    {
        "pending" => AdminLocalization.Instance["native.email.ozhidaet.otpravki"], "queued" => AdminLocalization.Instance["native.billing.v.ocheredi"], "retrying" => AdminLocalization.Instance["native.email.povtornaya.otpravka"],
        "sent" or "accepted" => AdminLocalization.Instance["native.email.sentStatus"], "delivered" => AdminLocalization.Instance["native.email.dostavleno"],
        "opened" => AdminLocalization.Instance["native.email.otkryto"], "clicked" => AdminLocalization.Instance["native.email.perekhod.po.ssylke"], "failed" => AdminLocalization.Instance["native.billing.oshibka"],
        "hard_bounced" => AdminLocalization.Instance["native.email.adres.nedostupen"], "soft_bounced" => AdminLocalization.Instance["native.email.vremennaya.oshibka"],
        "spam" => AdminLocalization.Instance["native.support.spam"], "spam_block" => AdminLocalization.Instance["native.email.blokirovka.spama"], "unsubscribed" => AdminLocalization.Instance["native.email.otpiska"],
        "simulated" => AdminLocalization.Instance["native.email.otpravka.otklyuchena"], _ => status ?? "—"
    };

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
