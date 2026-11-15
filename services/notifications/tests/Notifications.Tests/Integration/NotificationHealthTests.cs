using System.Net;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Notifications.Infrastructure;

namespace Notifications.Tests.Integration;

public sealed class NotificationHealthTests(MessagingFixture fixture) : IClassFixture<MessagingFixture>
{
    [Fact]
    public async Task ReadinessIncludesTheDatabaseAndRunningBroker()
    {
        var checks = fixture.Services.GetRequiredService<HealthCheckService>();
        var report = await checks.CheckHealthAsync();

        Assert.Equal(HealthStatus.Healthy, report.Status);
        Assert.Contains("notifications-database", report.Entries.Keys);
        Assert.True(report.Entries.Count >= 2);
    }

    [Fact]
    public async Task ReadinessDetectsStoppedBusAndRecovers()
    {
        var bus = fixture.Services.GetRequiredService<IBusControl>();
        var checks = fixture.Services.GetRequiredService<HealthCheckService>();
        try
        {
            await bus.StopAsync();
            var report = await checks.CheckHealthAsync();

            Assert.NotEqual(HealthStatus.Healthy, report.Status);
        }
        finally
        {
            await bus.StartAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        HealthReport recovered;
        do
        {
            recovered = await checks.CheckHealthAsync(timeout.Token);
            if (recovered.Status != HealthStatus.Healthy)
            {
                await Task.Delay(50, timeout.Token);
            }
        } while (recovered.Status != HealthStatus.Healthy);

        Assert.Equal(HealthStatus.Healthy, recovered.Status);
    }

    [Fact]
    public async Task DatabaseFailureDoesNotReturnConnectionDetails()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NotificationDbContext>(options => options.UseNpgsql(
            "Host=127.0.0.1;Port=1;Database=missing;Username=;Password=;Timeout=1;Command Timeout=1"));
        await using var provider = services.BuildServiceProvider();
        var check = new NotificationDatabaseHealthCheck(provider.GetRequiredService<IServiceScopeFactory>());
        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Null(result.Exception);
        Assert.DoesNotContain("private-", result.Description ?? string.Empty, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, HttpStatusCode.OK, "Healthy")]
    [InlineData(HealthStatus.Degraded, HttpStatusCode.ServiceUnavailable, "Degraded")]
    [InlineData(HealthStatus.Unhealthy, HttpStatusCode.ServiceUnavailable, "Unhealthy")]
    public async Task HttpReadinessReturnsOnlyStatusWhileLivenessRemainsAvailable(
        HealthStatus dependencyStatus,
        HttpStatusCode expectedCode,
        string expectedBody)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddHealthChecks().AddCheck("dependency", () =>
            new HealthCheckResult(dependencyStatus, "private-connection-details"));
        await using var application = builder.Build();
        application.MapNotificationHealthChecks();
        await application.StartAsync();
        var address = application.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var readiness = await client.GetAsync("/health/ready");
        using var liveness = await client.GetAsync("/health/live");

        Assert.Equal(expectedCode, readiness.StatusCode);
        Assert.Equal(expectedBody, await readiness.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, liveness.StatusCode);
        Assert.Equal("Healthy", await liveness.Content.ReadAsStringAsync());
        Assert.Contains("no-store", readiness.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }
}