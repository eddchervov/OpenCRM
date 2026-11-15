using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Promotions;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Promotions.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class PromotionsDatabaseCollection : ICollectionFixture<PromotionsDatabaseFixture>
{
    public const string Name = "Promotions PostgreSQL database";
}

public sealed class PromotionsDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public string ConnectionString => this._postgres.GetConnectionString();

    public OpenCRMContext CreateContext() => new(new DbContextOptionsBuilder<OpenCRMContext>()
        .UseNpgsql(this.ConnectionString, database =>
        {
            database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
        })
        .Options);

    public async Task<Organization> CreateOrganizationAsync(OpenCRMContext context, string? ownerUserId = null, string scenario = "detailing")
    {
        // Переиспользуем существующего владельца, если он передан.
        ApplicationUser owner = ownerUserId is null
            ? new ApplicationUser { Email = $"promotions-{Guid.NewGuid():N}@example.test" }
            : await context.ApplicationUsers.SingleAsync(user => user.Id == ownerUserId);

        var organization = new Organization
        {
            Name = $"Тестовый {scenario}",
            OwnerUser = owner,
            OwnerUserId = owner.Id,
            PrimaryScenarioCode = scenario,
            CreatedAt = DateTime.UtcNow,
            Members = [new OrganizationMember { User = owner, UserId = owner.Id, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    public async Task<SubscriptionPlan> CreatePlanAsync(OpenCRMContext context, string name, bool isDemo = false)
    {
        var plan = new SubscriptionPlan
        {
            Name = name,
            Price = 1000,
            DurationDays = 30,
            IsActive = true,
            VehicleLimit = 20,
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsDemoPlan = isDemo
        };
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();

        return plan;
    }

    public async Task<PromoCode> CreateGiftCodeAsync(OpenCRMContext context, int templatePlanId, int days = 30, int? cap = null)
    {
        var promo = new PromoCode
        {
            Code = "PG" + Guid.NewGuid().ToString("N").ToUpperInvariant(),
            Name = "Подарочные дни",
            IsOrganizationCode = true,
            DiscountTypeCode = PromoCodeCatalogCodes.DiscountTypes.BonusDays,
            BonusDays = days,
            GiftTemplatePlanId = templatePlanId,
            MaximumOrganizationApplications = cap,
            AppliesToAllScenarios = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.PromoCodes.Add(promo);
        await context.SaveChangesAsync();

        return promo;
    }

    /// <summary>
    /// Собирает провайдер с реальными модулями Billing и Promotions на внешнем контексте;
    /// мокаются только permission, аудит и инициализатор дефолтов.
    /// </summary>
    public ServiceProvider CreateServices(
        OpenCRMContext context,
        Func<OrganizationAuditEventCreateDto, bool, Task>? auditOverride = null,
        Func<IOrganizationPromoAccessGrantPort>? portOverride = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);

        var permissions = new Mock<IOrganizationPermissionService>();
        permissions
            .Setup(service => service.HasPermissionAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        services.AddSingleton(permissions.Object);

        var audit = new Mock<IOrganizationAuditService>();
        audit
            .Setup(service => service.AddAsync(It.IsAny<OrganizationAuditEventCreateDto>(), It.IsAny<bool>()))
            .Returns((OrganizationAuditEventCreateDto dto, bool saveChanges) =>
                auditOverride?.Invoke(dto, saveChanges) ?? Task.CompletedTask);
        services.AddSingleton(audit.Object);

        var initializer = new Mock<IFeatureDefaultsInitializer>();
        initializer
            .Setup(service => service.EnsureDefaultsAsync())
            .Returns(Task.CompletedTask);
        services.AddSingleton(initializer.Object);

        services.AddOpenCRMBilling();
        services.AddOpenCRMPromotions();

        if (portOverride is not null)
        {
            services.AddScoped<IOrganizationPromoAccessGrantPort>(_ => portOverride());
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();
}
