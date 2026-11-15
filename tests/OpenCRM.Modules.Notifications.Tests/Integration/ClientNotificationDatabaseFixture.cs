using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Notifications.Tests.Integration;

public sealed class ClientNotificationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public OpenCRMContext CreateContext(IInterceptor? interceptor = null, bool delayDisposal = false)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), database =>
            {
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                database.EnableRetryOnFailure();
            })
            .AddInterceptors(interceptor is null ? [] : [interceptor]);

        if (delayDisposal)
        {
            options.ReplaceService<IRelationalTransactionFactory, AsyncDisposalTransactionFactory>();
        }

        return new OpenCRMContext(options.Options);
    }

    public async Task<(long NotificationId, string NotificationKey)> CreateNotificationAsync(bool isBotEnabled = true)
    {
        await using var context = this.CreateContext();
        var user = new ApplicationUser
        {
            UserName = $"max-worker-{Guid.NewGuid():N}",
            Email = "max-worker@example.test"
        };
        var organization = new Organization { Name = "Тестовая организация", OwnerUser = user };
        var client = new OrganizationClient
        {
            Organization = organization,
            Name = "Клиент",
            MaxUserId = 123,
            MaxNotificationsEnabled = true
        };
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            OrganizationClient = client,
            RegistrationPlate = "А123АА"
        };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Planned,
            Date = DateTime.UtcNow.Date,
            PlannedDate = DateTime.UtcNow.Date
        };
        var bot = new OrganizationMaxBot
        {
            Organization = organization,
            Username = $"max-{Guid.NewGuid():N}",
            ProtectedToken = "protected-token",
            ProtectedWebhookSecret = "protected-secret",
            IsEnabled = isBotEnabled
        };
        var notification = new MaxClientNotification
        {
            OrganizationId = organization.Id,
            ClientId = client.Id,
            MaxUserId = client.MaxUserId.Value,
            MaintenanceId = maintenance.Id,
            EventCode = "booking_created",
            NotificationKey = $"max:{Guid.NewGuid():N}",
            Body = "Тестовое уведомление",
            Status = "queued",
            NextAttemptAt = DateTime.UtcNow.AddMinutes(-1)
        };
        context.Maintenance.Add(maintenance);
        context.OrganizationMaxBots.Add(bot);
        context.MaxClientNotifications.Add(notification);
        await context.SaveChangesAsync();

        return (notification.Id, notification.NotificationKey);
    }

    public async Task ResetAsync()
    {
        await using var context = this.CreateContext();
        await context.MaxClientNotifications.ExecuteDeleteAsync();
        await context.Maintenance.ExecuteDeleteAsync();
        await context.Vehicles.ExecuteDeleteAsync();
        await context.OrganizationClients.ExecuteDeleteAsync();
        await context.OrganizationMaxBots.ExecuteDeleteAsync();
        await context.Organizations.ExecuteDeleteAsync();
        await context.ApplicationUsers.ExecuteDeleteAsync();
    }

    public async Task DisposeAsync()
    {
        await this._postgres.DisposeAsync();
    }
}
