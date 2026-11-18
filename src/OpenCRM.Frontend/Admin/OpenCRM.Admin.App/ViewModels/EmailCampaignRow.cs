using CommunityToolkit.Mvvm.ComponentModel;
using OpenCRM.Admin.Api.Models;
using OpenCRM.Admin.App.Localization;

namespace OpenCRM.Admin.App.ViewModels;

public sealed class EmailCampaignRow(AdminEmailCampaign campaign) : ObservableObject
{
    public Guid Id { get; } = campaign.Id;
    public string Subject { get; } = campaign.Subject;
    public string StatusCode { get; } = campaign.StatusCode;
    public string StatusLabel => this.StatusCode switch
    {
        "scheduled" => AdminLocalization.Instance["native.email.zaplanirovana"],
        "running" => AdminLocalization.Instance["native.email.vypolnyaetsya"],
        "paused" => AdminLocalization.Instance["native.email.priostanovlena"],
        "completed" => AdminLocalization.Instance["native.billing.zavershena"],
        "canceled" => AdminLocalization.Instance["native.email.otmenena"],
        "failed" => AdminLocalization.Instance["native.billing.oshibka"],
        _ => this.StatusCode
    };
    public string TargetLabel => campaign.TargetMode switch
    {
        "email" => AdminLocalization.Instance["native.email.odin.adres"],
        "user" => AdminLocalization.Instance["native.promoCodes.polzovatel"],
        "organizationType" => string.IsNullOrWhiteSpace(campaign.TargetOrganizationTypeCode)
            ? AdminLocalization.Instance["native.email.tip.organizatsii"]
            : AdminLocalization.Instance.Format("native.email.tip.organizatsii.campaign.targetorganizationtypecode", campaign.TargetOrganizationTypeCode),
        "all" => AdminLocalization.Instance["native.email.vse.polzovateli"],
        _ => campaign.TargetMode
    };
    public string StartAt { get; } = campaign.StartAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string Progress => AdminLocalization.Instance.Format("native.email.campaign.sentcount.n0.iz.campaign.totalcount.n0", campaign.SentCount, campaign.TotalCount);
    public double ProgressPercent => campaign.TotalCount == 0 ? 0 : (double)(campaign.SentCount + campaign.SimulatedCount + campaign.FailedCount + campaign.SkippedCount) * 100 / campaign.TotalCount;
    public string Details => AdminLocalization.Instance.Format("native.email.oshibki.campaign.failedcount.n0.propushcheno.campaign.skippedcou", campaign.FailedCount, campaign.SkippedCount, campaign.SimulatedCount);
    public string? LastError { get; } = campaign.LastError;
    public bool CanPause => this.StatusCode == "running";
    public bool CanResume => this.StatusCode == "paused";
    public bool CanCancel => this.StatusCode is "scheduled" or "running" or "paused";

    public void RefreshLocalizedValues() => this.OnPropertyChanged((string?)null);
}
