using Hangfire;
using OpenCRM.Services.Interfaces.Admin;

namespace OpenCRM.WebApp.Infrastructure.Hangfire;

/// <summary> Планировщик отложенных задач обработки email-кампаний. </summary>
public sealed class EmailCampaignScheduler(IBackgroundJobClient backgroundJobClient) : IEmailCampaignScheduler
{
    /// <summary> Планирует фоновую задачу обработки кампании с задержкой. </summary>
    public void ScheduleNext(Guid campaignId, TimeSpan delay)
    {
        if (delay < TimeSpan.Zero)
        {
            delay = TimeSpan.Zero;
        }

        backgroundJobClient.Schedule<IEmailCampaignService>(
            service => service.ProcessCampaignAsync(campaignId),
            delay);
    }
}
