using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Data.Common;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Clients;
using OpenCRM.Services.Interfaces.FileStorages;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Features.Dtos;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Clients.Tests.Integration;

[CollectionDefinition("Clients PostgreSQL database")]
public sealed class ClientsDatabaseCollection : ICollectionFixture<ClientsDatabaseFixture>
{
    public const string Name = "Clients PostgreSQL database";
}

public sealed class ClientsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public OpenCRMContext CreateContext(params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), database =>
            {
                database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                database.EnableRetryOnFailure();
            })
            .AddInterceptors(interceptors)
            .Options;

        return new OpenCRMContext(options);
    }

    public ServiceProvider CreateServices(
        OpenCRMContext context,
        IFileStorageService? storage = null,
        ILoggerProvider? loggerProvider = null,
        IReadOnlySet<string>? deniedPermissionCodes = null,
        IOrganizationSubscriptionService? subscriptionService = null)
    {
        var subscription = new Mock<IOrganizationSubscriptionService>();
        subscription.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        subscription.Setup(service => service.GetFeatureSnapshotAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new FeatureAccessSnapshotDto());
        subscription.Setup(service => service.EnsureWorkspaceCanReadAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((string _, Guid _, string permissionCode) => deniedPermissionCodes?.Contains(permissionCode) != true);

        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddSingleton(subscriptionService ?? subscription.Object);
        services.AddSingleton(permissions.Object);
        services.AddSingleton(Mock.Of<IOrganizationAuditService>());
        services.AddSingleton(storage ?? Mock.Of<IFileStorageService>());
        services.AddLogging(logging =>
        {
            if (loggerProvider is not null)
            {
                logging.AddProvider(loggerProvider);
            }
        });
        services.AddOpenCRMClients();
        services.AddOpenCRMVehicles();
        services.AddOpenCRMMaintenance();

        return services.BuildServiceProvider();
    }

    public async Task<Organization> CreateOrganizationAsync(OpenCRMContext context, string name = "Проверка клиентов")
    {
        var user = new ApplicationUser
        {
            UserName = $"client-test-{Guid.NewGuid():N}",
            Email = $"client-test-{Guid.NewGuid():N}@example.test"
        };
        var organization = new Organization
        {
            Name = name,
            PrimaryScenarioCode = "detailing",
            OwnerUser = user,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };

        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    public async Task<Vehicle> AddVehicleAsync(
        OpenCRMContext context,
        Organization organization,
        OrganizationClient? client = null,
        string? name = null,
        bool isDeleted = false)
    {
        var vehicle = new Vehicle
        {
            Organization = organization,
            UserId = organization.OwnerUserId,
            OrganizationClient = client,
            Name = name,
            ManualBrandName = "Тест",
            ManualModelName = "Автомобиль",
            RegistrationPlate = $"А{Random.Shared.Next(100, 999)}АА154",
            DateTimeCreation = DateTime.UtcNow,
            IsDeleted = isDeleted
        };

        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return vehicle;
    }

    public async Task<OrganizationClientTag> AddTagAsync(OpenCRMContext context, Organization organization, string name)
    {
        var tag = new OrganizationClientTag
        {
            Organization = organization,
            Name = name,
            NormalizedName = name.ToLowerInvariant()
        };

        context.OrganizationClientTags.Add(tag);
        await context.SaveChangesAsync();

        return tag;
    }

    public static OrganizationClient CreateClient(Organization organization, string name, string? responsibleUserId = null)
    {
        return new OrganizationClient
        {
            OrganizationId = organization.Id,
            Organization = organization,
            Name = name,
            ResponsibleUserId = responsibleUserId
        };
    }

    public static OpenCRM.Domain.Maintenance CreateMaintenance(Organization organization, Vehicle vehicle, MaintenanceStatus status, DateTime date)
    {
        return new OpenCRM.Domain.Maintenance
        {
            UserId = organization.OwnerUserId,
            Vehicle = vehicle,
            BuyerClientId = vehicle.OrganizationClientId,
            Status = status,
            Type = MaintenanceType.Maintenance,
            Date = date,
            PlannedDate = status == MaintenanceStatus.Planned ? date : null,
            CompletedDate = status == MaintenanceStatus.Completed ? date : null,
            DateTimeCreation = date
        };
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await this._postgres.DisposeAsync();
    }

    public sealed class SqlCaptureInterceptor : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            this.Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            this.Commands.Add(command.CommandText);

            return ValueTask.FromResult(result);
        }
    }

    public sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<LogEntry> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(this);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(RecordingLoggerProvider provider) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                provider.Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }

    public sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
