using OpenCRM.Domain;

namespace OpenCRM.Services.Interfaces.Admin;

/// <summary> Планирование последующих шагов email-рассылки. </summary>
public interface IEmailCampaignScheduler
{
    /// <summary> Планирует следующую обработку кампании с задержкой. </summary>
    void ScheduleNext(Guid campaignId, TimeSpan delay);
}

