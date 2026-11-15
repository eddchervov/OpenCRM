using OpenCRM.Modules.Organizations.Contracts;
using Hangfire;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions;
using OpenCRM.WebApp.Infrastructure.Billing;

namespace OpenCRM.WebApp.Infrastructure.Hangfire;

/// <summary> Регистрирует повторяющиеся фоновые задания при запуске приложения. </summary>
public sealed class RecurringJobsRegistrationService(
    IServiceProvider serviceProvider,
    ILogger<RecurringJobsRegistrationService> logger,
    IConfiguration configuration) : BackgroundService
{
    /// <summary> Регистрирует повторяющиеся задания в хранилище Hangfire. </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        if (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            using var scope = serviceProvider.CreateScope();
            var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();

            recurringJobManager.AddOrUpdate<IOrganizationArchiveService>(
                "organizations.cleanup-archive",
                service => service.CleanUpArchiveAsync(),
                Cron.Daily);

            if (configuration.GetValue("PlatformPolicy:Commercial:Enabled", false))
            {
                recurringJobManager.AddOrUpdate<IPersonalSubscriptionCommandService>(
                    "subscriptions.deactivate-expired",
                    service => service.CheckAndDeactivateExpiredSubscriptionsAsync(),
                    Cron.Daily);

                recurringJobManager.AddOrUpdate<ISubscriptionExpiryReminderService>(
                    "subscriptions.send-expiry-reminders",
                    service => service.SendDueRemindersAsync(),
                    Cron.Daily);

                recurringJobManager.AddOrUpdate<BillingAutomationService>(
                    "billing.reconcile-yookassa",
                    service => service.ReconcilePaymentsAsync(),
                    "*/15 * * * *");

            }
            else
            {
                recurringJobManager.RemoveIfExists("subscriptions.deactivate-expired");
                recurringJobManager.RemoveIfExists("subscriptions.send-expiry-reminders");
                recurringJobManager.RemoveIfExists("billing.reconcile-yookassa");
            }

            recurringJobManager.RemoveIfExists("billing.remind-npd-receipts");
            recurringJobManager.AddOrUpdate<IMaintenanceVisitReminderService>(
                "maintenance.send-visit-reminders",
                service => service.SendDueAsync(CancellationToken.None),
                Cron.Minutely);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Не удалось зарегистрировать периодические задачи Hangfire.");
        }
    }
}
