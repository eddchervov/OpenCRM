using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using OpenCRM.Domain;
using OpenCRM.Domain.Assistant;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Maintenance.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.WebApp.Infrastructure.AiPilot;
using OpenCRM.WebApp.Infrastructure.Assistant;
using OpenCRM.Modules.Vehicles;
using OpenCRM.Modules.Vehicles.Contracts;
using Testcontainers.PostgreSql;
using Moq;

namespace OpenCRM.Assistant.Tests.Integration;

public sealed class AssistantIntegrationFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();
    private ServiceProvider? _usageServiceProvider;

    public IConfiguration Configuration { get; private set; } = null!;

    public AssistantOptions Options { get; private set; } = null!;

    public AssistantUsageService UsageService { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        this.Configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("assistant-appsettings.json", optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Assistant:RouterApiKey"] = "REMOVED_SECRET",
                ["Assistant:MonthlyBudgetRub"] = "1000",
                ["Assistant:DailyBudgetRub"] = "1000",
                ["Assistant:TextReservationRub"] = "2",
                ["Assistant:AudioReservationRub"] = "2",
                ["AssistantTelegram:BotToken"] = "123456:test-integration-token",
                ["AssistantTelegram:WebhookSecret"] = "IntegrationWebhookSecret_123",
                ["AssistantTelegram:BotUsername"] = "integration_test_bot",
                ["AssistantTelegram:BotLinkBaseUrl"] = "https://t.me/"
            })
            .Build();
        this.Options = AssistantOptions.Load(this.Configuration);
        var services = new ServiceCollection();
        services.AddScoped(_ => this.CreateContext());
        this._usageServiceProvider = services.BuildServiceProvider();
        this.UsageService = new AssistantUsageService(this._usageServiceProvider.GetRequiredService<IServiceScopeFactory>(), this.Options);

        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public OpenCRMContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OpenCRMContext>()
            .UseNpgsql(this._postgres.GetConnectionString(), db =>
            {
                db.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
                db.EnableRetryOnFailure();
            })
            .Options;

        return new OpenCRMContext(options);
    }

    public async Task<Guid> CreateOrganizationAsync(string suffix)
    {
        var organizationId = Guid.NewGuid();
        var userId = $"assistant-integration-{suffix}-{Guid.NewGuid():N}";
        await using var context = this.CreateContext();
        context.ApplicationUsers.Add(new ApplicationUser
        {
            Id = userId,
            UserName = userId,
            NormalizedUserName = userId.ToUpperInvariant(),
            Email = $"{userId}@example.test",
            NormalizedEmail = $"{userId}@example.test".ToUpperInvariant()
        });
        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            OwnerUserId = userId,
            Name = $"Тестовая организация {suffix}",
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
            ShiftTimeZoneId = "UTC"
        });
        await context.SaveChangesAsync();

        var settings = await new AssistantSettingsStore(context, this.Options).GetAsync(CancellationToken.None);
        settings.IsEnabled = true;
        settings.WebEnabled = true;
        settings.TelegramEnabled = true;
        settings.TextReservationRub = 2;
        settings.AudioReservationRub = 2;
        settings.DailyBudgetRub = 1000;
        settings.MonthlyBudgetRub = 1000;
        context.Set<AssistantOrganizationAccess>().Add(new AssistantOrganizationAccess
        {
            OrganizationId = organizationId,
            IsEnabled = true,
            WebEnabled = true,
            TelegramEnabled = true,
            DailyBudgetRub = 1000,
            MonthlyBudgetRub = 1000,
            UpdatedByUserId = userId
        });
        await context.SaveChangesAsync();

        return organizationId;
    }

    public async Task<string> GetOwnerUserIdAsync(Guid organizationId)
    {
        await using var context = this.CreateContext();

        return await context.Organizations
            .Where(organization => organization.Id == organizationId)
            .Select(organization => organization.OwnerUserId)
            .SingleAsync();
    }

    public async Task SetBudgetsAsync(Guid organizationId, decimal globalBudget, decimal organizationBudget)
    {
        await using var context = this.CreateContext();
        var settings = await context.Set<AssistantSettings>().SingleAsync(item => item.Id == 1);
        settings.DailyBudgetRub = globalBudget;
        settings.MonthlyBudgetRub = globalBudget;
        var access = await context.Set<AssistantOrganizationAccess>().SingleAsync(item => item.OrganizationId == organizationId);
        access.DailyBudgetRub = organizationBudget;
        access.MonthlyBudgetRub = organizationBudget;
        await context.SaveChangesAsync();
    }

    public async Task SetAssistantEnabledAsync(bool enabled)
    {
        await using var context = this.CreateContext();
        var settings = await context.Set<AssistantSettings>().SingleAsync(item => item.Id == 1);
        settings.IsEnabled = enabled;
        await context.SaveChangesAsync();
    }

    public async Task ClearUsageAsync()
    {
        await using var context = this.CreateContext();
        await context.Set<AssistantUsage>().ExecuteDeleteAsync();
    }

    public AssistantTestScope CreateAssistant(
        IAssistantActionExecutor executor,
        FakeRouterHandler routerHandler,
        bool hasAssistantFeature = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(this.Configuration);
        services.AddSingleton(this.Options);
        services.AddSingleton(this.UsageService);
        services.AddSingleton(executor);
        services.AddSingleton<IVehicleRegistrationPlatePolicy>(new VehicleRegistrationPlatePolicy(this.Configuration));
        services.AddScoped(_ => this.CreateContext());
        services.AddScoped<AssistantProviderContext>();
        services.AddScoped<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));
        services.AddScoped(_ => new HttpClient(routerHandler));
        services.AddScoped(provider => new RouterAiPilotClient(
            provider.GetRequiredService<HttpClient>(),
            this.Configuration,
            provider.GetRequiredService<AssistantProviderContext>(),
            this.UsageService));
        services.AddScoped(provider => new AiPilotConversationService(
            provider.GetRequiredService<IMemoryCache>(),
            provider.GetRequiredService<RouterAiPilotClient>(),
            provider.GetRequiredService<IVehicleRegistrationPlatePolicy>()));
        services.AddScoped(provider => this.CreateAccessService(provider.GetRequiredService<OpenCRMContext>(), hasAssistantFeature));
        services.AddScoped(provider => new AssistantService(
            provider.GetRequiredService<OpenCRMContext>(),
            provider.GetRequiredService<AssistantAccessService>(),
            provider.GetRequiredService<AssistantProviderContext>(),
            provider.GetRequiredService<AiPilotConversationService>(),
            provider.GetRequiredService<RouterAiPilotClient>(),
            provider.GetRequiredService<IAssistantActionExecutor>(),
            NullLogger<AssistantService>.Instance,
            this.Options,
            provider.GetRequiredService<IVehicleRegistrationPlatePolicy>()));

        var serviceProvider = services.BuildServiceProvider();
        var scope = serviceProvider.CreateAsyncScope();

        return new AssistantTestScope(
            serviceProvider,
            scope,
            scope.ServiceProvider.GetRequiredService<AssistantService>(),
            scope.ServiceProvider.GetRequiredService<OpenCRMContext>(),
            scope.ServiceProvider.GetRequiredService<AssistantAccessService>());
    }

    public async Task DisposeAsync()
    {
        if (this._usageServiceProvider is not null)
        {
            await this._usageServiceProvider.DisposeAsync();
        }
        await this._postgres.DisposeAsync();
    }

    private AssistantAccessService CreateAccessService(OpenCRMContext context, bool hasAssistantFeature)
    {
        var subscriptions = new Mock<IOrganizationSubscriptionService>();
        subscriptions.Setup(service => service.GetWorkspaceAccessAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .ReturnsAsync(new OrganizationWorkspaceAccessDto());
        subscriptions.Setup(service => service.EnsureWorkspaceCanReadAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>()))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.EnsureWorkspaceCanWriteAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);
        subscriptions.Setup(service => service.HasFeatureAsync(It.IsAny<string>(), It.IsAny<Guid>(), Helpers.FeatureCodes.Assistant))
            .ReturnsAsync(hasAssistantFeature);

        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        return new AssistantAccessService(context, subscriptions.Object, permissions.Object, this.Options);
    }
}

public sealed class AssistantTestScope(
    ServiceProvider serviceProvider,
    AsyncServiceScope scope,
    AssistantService service,
    OpenCRMContext context,
    AssistantAccessService access) : IAsyncDisposable
{
    public AssistantService Service { get; } = service;

    public OpenCRMContext Context { get; } = context;

    public AssistantAccessService Access { get; } = access;

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
        await serviceProvider.DisposeAsync();
    }
}

public sealed class FakeAssistantActionExecutor(
    bool readOnly = false,
    string? target = null,
    IReadOnlyList<string>? changes = null) : IAssistantActionExecutor
{
    private int _executionCount;

    public int ExecutionCount => Volatile.Read(ref this._executionCount);

    public Task<AssistantPreparedAction> PrepareAsync(
        AiPilotCommand command,
        AssistantActor actor,
        AssistantActionSelection selection,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new AssistantPreparedAction
        {
            Target = target ?? $"Тестовый заказ {command.RegistrationPlate}",
            Changes = changes ?? ["Добавить тестовую услугу"],
            Snapshot = "stable-snapshot",
            ReadOnly = readOnly,
            ExecuteAsync = _ =>
            {
                Interlocked.Increment(ref this._executionCount);
                return Task.FromResult(new AssistantActionResult("Действие выполнено."));
            }
        });
    }
}

public sealed class FakeRouterHandler(IEnumerable<AiPilotCommand> commands) : HttpMessageHandler
{
    private readonly Queue<AiPilotCommand> _commands = new(commands);
    private readonly object _lock = new();
    private readonly List<string> _requestBodies = [];

    public IReadOnlyList<string> RequestBodies
    {
        get
        {
            lock (this._lock)
            {
                return this._requestBodies.ToArray();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
        AiPilotCommand command;
        lock (this._lock)
        {
            this._requestBodies.Add(requestBody);
            command = this._commands.Dequeue();
        }

        var content = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var response = JsonSerializer.Serialize(new
        {
            id = Guid.NewGuid().ToString("N"),
            choices = new[] { new { message = new { content } } },
            usage = new { prompt_tokens = 12, completion_tokens = 8, cost = 0.1m }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response, Encoding.UTF8, "application/json")
        };
    }
}
