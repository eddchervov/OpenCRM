using OpenCRM.Admin.Api.Models;

namespace OpenCRM.Admin.App.ViewModels;

public sealed class EmailCampaignRow(AdminEmailCampaign campaign)
{
    public Guid Id { get; } = campaign.Id;
    public string Subject { get; } = campaign.Subject;
    public string StatusCode { get; } = campaign.StatusCode;
    public string StatusLabel => this.StatusCode switch
    {
        "scheduled" => "Запланирована",
        "running" => "Выполняется",
        "paused" => "Приостановлена",
        "completed" => "Завершена",
        "canceled" => "Отменена",
        "failed" => "Ошибка",
        _ => this.StatusCode
    };
    public string TargetLabel => campaign.TargetMode switch
    {
        "email" => "Один адрес",
        "user" => "Пользователь",
        "organizationType" => string.IsNullOrWhiteSpace(campaign.TargetOrganizationTypeCode)
            ? "Тип организации"
            : $"Тип организации · {campaign.TargetOrganizationTypeCode}",
        "all" => "Все пользователи",
        _ => campaign.TargetMode
    };
    public string StartAt { get; } = campaign.StartAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    public string Progress => $"{campaign.SentCount:N0} из {campaign.TotalCount:N0}";
    public double ProgressPercent => campaign.TotalCount == 0 ? 0 : (double)(campaign.SentCount + campaign.SimulatedCount + campaign.FailedCount + campaign.SkippedCount) * 100 / campaign.TotalCount;
    public string Details => $"Ошибки: {campaign.FailedCount:N0} · пропущено: {campaign.SkippedCount:N0} · имитация: {campaign.SimulatedCount:N0}";
    public string? LastError { get; } = campaign.LastError;
    public bool CanPause => this.StatusCode == "running";
    public bool CanResume => this.StatusCode == "paused";
    public bool CanCancel => this.StatusCode is "scheduled" or "running" or "paused";
}
