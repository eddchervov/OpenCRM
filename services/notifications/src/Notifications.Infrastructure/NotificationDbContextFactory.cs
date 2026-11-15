using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Notifications.Application;
using Notifications.Domain;

namespace Notifications.Infrastructure;

/// <summary>
/// Создаёт контекст для команд Entity Framework вне запущенного приложения.
/// </summary>
public sealed class NotificationDbContextFactory : IDesignTimeDbContextFactory<NotificationDbContext>
{
    /// <summary>Создаёт контекст со строкой подключения из локальных секретов или окружения.</summary>
    public NotificationDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Development";

        var configurationBuilder = new ConfigurationBuilder();
        if (environment == "Development")
        {
            configurationBuilder.AddUserSecrets<NotificationDbContextFactory>(optional: true);
        }

        var configuration = configurationBuilder.AddEnvironmentVariables().Build();
        var connectionString = configuration.GetConnectionString("NotificationsDb")
            ?? throw new InvalidOperationException("Connection string NotificationsDb must be set in user-secrets or environment.");

        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = new DbContextOptionsBuilder<NotificationDbContext>()
            .UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"))
            .Options;

        return new NotificationDbContext(options);
    }
}
