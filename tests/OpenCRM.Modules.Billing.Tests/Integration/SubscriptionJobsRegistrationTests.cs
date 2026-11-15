using Microsoft.Extensions.Configuration;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Organizations.Contracts;
using OpenCRM.WebApp.Infrastructure.Hangfire;

namespace OpenCRM.Modules.Billing.Tests.Integration;

public sealed class SubscriptionJobsRegistrationTests
{
    [Fact]
    public async Task ExpiryJobUsesBillingCommandContractAndPreservesItsScheduleAndIdentity()
    {
        var manager = new Mock<IRecurringJobManager>();
        using var provider = new ServiceCollection()
            .AddSingleton(manager.Object)
            .BuildServiceProvider();
        using var service = new RecurringJobsRegistrationService(
            provider,
            NullLogger<RecurringJobsRegistrationService>.Instance,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformPolicy:Commercial:Enabled"] = "true"
            }).Build());
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        var registration = Assert.Single(manager.Invocations,
            invocation => invocation.Method.Name == "AddOrUpdate"
                && Equals(invocation.Arguments[0], "subscriptions.deactivate-expired"));
        var job = Assert.IsType<Job>(registration.Arguments[1]);

        Assert.Equal(typeof(IPersonalSubscriptionCommandService), job.Type);
        Assert.Equal(nameof(IPersonalSubscriptionCommandService.CheckAndDeactivateExpiredSubscriptionsAsync), job.Method.Name);
        Assert.Equal(Cron.Daily(), registration.Arguments[2]);

        var archiveRegistration = Assert.Single(manager.Invocations,
            invocation => invocation.Method.Name == "AddOrUpdate"
                && Equals(invocation.Arguments[0], "organizations.cleanup-archive"));

        var archiveJob = Assert.IsType<Job>(archiveRegistration.Arguments[1]);
        Assert.Equal(typeof(IOrganizationArchiveService), archiveJob.Type);
        Assert.Equal(nameof(IOrganizationArchiveService.CleanUpArchiveAsync), archiveJob.Method.Name);
        Assert.Equal(Cron.Daily(), archiveRegistration.Arguments[2]);
        Assert.Equal(5, manager.Invocations.Count(invocation => invocation.Method.Name == "AddOrUpdate"));
    }
    [Fact]
    public async Task NeutralModeRemovesCommercialJobsAndKeepsOperationalJobs()
    {
        var manager = new Mock<IRecurringJobManager>();
        using var provider = new ServiceCollection().AddSingleton(manager.Object).BuildServiceProvider();
        using var service = new RecurringJobsRegistrationService(provider,
            NullLogger<RecurringJobsRegistrationService>.Instance, new ConfigurationBuilder().Build());
        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        foreach (var jobId in new[] { "subscriptions.deactivate-expired", "subscriptions.send-expiry-reminders", "billing.reconcile-yookassa" })
        {
            manager.Verify(jobs => jobs.RemoveIfExists(jobId), Times.Once);
            Assert.DoesNotContain(manager.Invocations,
                invocation => invocation.Method.Name == "AddOrUpdate" && Equals(invocation.Arguments[0], jobId));
        }

        Assert.Contains(manager.Invocations, invocation => invocation.Method.Name == "AddOrUpdate"
            && Equals(invocation.Arguments[0], "organizations.cleanup-archive"));
        Assert.Contains(manager.Invocations, invocation => invocation.Method.Name == "AddOrUpdate"
            && Equals(invocation.Arguments[0], "maintenance.send-visit-reminders"));
        Assert.Equal(2, manager.Invocations.Count(invocation => invocation.Method.Name == "AddOrUpdate"));
    }
}
