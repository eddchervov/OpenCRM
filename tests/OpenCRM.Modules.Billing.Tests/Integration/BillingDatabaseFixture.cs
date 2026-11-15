using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using OpenCRM.Modules.Organizations;
using OpenCRM.Domain;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Infrastructure.Migrations;
using OpenCRM.Modules.Billing;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Modules.Payments.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;
using Testcontainers.PostgreSql;

namespace OpenCRM.Modules.Billing.Tests.Integration;

[CollectionDefinition(Name)]
public sealed class BillingDatabaseCollection : ICollectionFixture<BillingDatabaseFixture>
{
    public const string Name = "Billing PostgreSQL database";
}

public sealed class BillingDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:15-alpine").Build();

    public string ConnectionString => this._postgres.GetConnectionString();

    public OpenCRMContext CreateContext(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<OpenCRMContext>()
        .UseNpgsql(this.ConnectionString, database =>
        {
            database.MigrationsAssembly(typeof(AssemblyMarker).Assembly.GetName().Name);
            database.EnableRetryOnFailure();
        })
        .AddInterceptors(interceptors).Options);

    public async Task<Organization> CreateOrganizationAsync(
        OpenCRMContext context,
        string? ownerUserId = null,
        string scenario = "detailing",
        DateTime? createdAt = null)
    {
        var owner = ownerUserId is null
            ? null
            : await context.ApplicationUsers.FirstOrDefaultAsync(user => user.Id == ownerUserId);
        owner ??= new ApplicationUser { Email = $"billing-{Guid.NewGuid():N}@example.test" };
        if (ownerUserId is not null && owner.Id != ownerUserId)
        {
            owner.Id = ownerUserId;
        }

        var organization = new Organization
        {
            Name = "Тестовый биллинг",
            OwnerUser = owner,
            OwnerUserId = owner.Id,
            PrimaryScenarioCode = scenario,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Members = [new OrganizationMember { User = owner, UserId = owner.Id, RoleCode = OrganizationRoleCodes.Owner }]
        };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        return organization;
    }

    public async Task<SubscriptionPlan> CreatePlanAsync(
        OpenCRMContext context,
        string name,
        bool isDefault = false,
        bool isDemo = false,
        int vehicleLimit = 20,
        params (FeatureDefinition Definition, bool? BoolValue, int? IntValue)[] features)
    {
        var plan = new SubscriptionPlan
        {
            Name = name,
            Description = $"План {name}",
            Price = 500,
            DurationDays = 30,
            VehicleLimit = vehicleLimit,
            Order = 10,
            Group = name,
            PlanTypeCode = SubscriptionPlanCatalogCodes.PlanTypes.Business,
            IsDefaultPlan = isDefault,
            IsDemoPlan = isDemo,
            Features = features.Select(feature => new SubscriptionPlanFeature
            {
                FeatureDefinition = feature.Definition,
                BoolValue = feature.BoolValue,
                IntValue = feature.IntValue
            }).ToList()
        };
        context.SubscriptionPlans.Add(plan);
        await context.SaveChangesAsync();

        return plan;
    }

    public async Task<FeatureDefinition> CreateFeatureAsync(
        OpenCRMContext context,
        string code,
        string name,
        FeatureValueType valueType,
        string scopeCode = FeatureCatalogCodes.Scopes.Organization,
        bool isActive = true,
        string? category = "Тест")
    {
        var definition = new FeatureDefinition
        {
            Code = code,
            Name = name,
            Description = name,
            Category = category,
            ScopeCode = scopeCode,
            AudienceCode = FeatureCatalogCodes.Audiences.User,
            ValueType = valueType,
            IsActive = isActive
        };
        context.FeatureDefinitions.Add(definition);
        await context.SaveChangesAsync();

        return definition;
    }

    public async Task AddSubscriptionAsync(
        OpenCRMContext context,
        Organization organization,
        SubscriptionPlan plan,
        DateTime endDate,
        DateTime? startDate = null)
    {
        context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            StartDate = startDate ?? endDate.AddDays(-30),
            EndDate = endDate,
            SourceCode = plan.IsDemoPlan ? "demo" : "manual"
        });
        await context.SaveChangesAsync();
    }

    public async Task<SubscriptionPurchase> AddPurchaseSnapshotAsync(
        OpenCRMContext context,
        string userId,
        Guid? organizationId,
        SubscriptionPlan plan,
        string planSnapshot,
        DateTime grantedFrom,
        DateTime grantedUntil)
    {
        var now = DateTime.UtcNow;
        var amount = plan.EffectivePrice;
        var payment = new SubscriptionPayment
        {
            UserId = userId,
            SubscriptionPlanId = plan.Id,
            Amount = amount,
            CreatedAt = now,
            PaymentMethod = "payments-service",
            PaymentProviderCode = "payments-service",
            PaymentStatus = "completed"
        };
        context.SubscriptionPayments.Add(payment);
        await context.SaveChangesAsync();

        var operation = new PaymentCreationOperation
        {
            IdempotenceKey = Guid.NewGuid().ToString("N"),
            UserId = userId,
            PaymentProviderCode = "payments-service",
            SubscriptionPlanId = plan.Id,
            OperationType = organizationId.HasValue ? $"yookassa_workspace_purchase:{organizationId}" : "yookassa_purchase",
            OrganizationId = organizationId,
            Amount = amount,
            Description = "Снимок оплаченного периода",
            BuyerTypeCode = "individual",
            RequestTimestamp = now,
            RequestBody = "{}",
            Status = PaymentCreationOperationStatuses.Created,
            CreatedAt = now,
            SubscriptionPaymentId = payment.Id
        };
        context.PaymentCreationOperations.Add(operation);
        await context.SaveChangesAsync();

        var purchase = new SubscriptionPurchase
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            OrganizationId = organizationId,
            BeneficiaryKey = organizationId.HasValue ? $"organization:{organizationId}" : $"user:{userId}",
            IsOpen = false,
            CreationOperationId = operation.Id,
            SubscriptionPaymentId = payment.Id,
            PlanSnapshot = planSnapshot,
            AmountMinor = decimal.ToInt64(amount * 100),
            ProviderStatus = "Succeeded",
            CreationStatus = "Resolved",
            IsTest = true,
            ApplicationStatus = SubscriptionPurchaseStatuses.Applied,
            CreatedAt = now,
            UpdatedAt = now,
            AppliedAt = now,
            GrantedFrom = grantedFrom,
            GrantedUntil = grantedUntil,
            NextApplicationAttemptAt = now,
            NextReconcileAt = now
        };
        context.SubscriptionPurchases.Add(purchase);
        await context.SaveChangesAsync();

        return purchase;
    }

    public Mock<IFeatureDefaultsInitializer> CreateDefaultsInitializer()
    {
        var initializer = new Mock<IFeatureDefaultsInitializer>();
        initializer.Setup(service => service.EnsureDefaultsAsync()).Returns(Task.CompletedTask);

        return initializer;
    }

    public Mock<IPromoCodeService> CreatePromoCodeService()
    {
        var promoCodeService = new Mock<IPromoCodeService>();
        promoCodeService.Setup(service => service.CalculatePriceAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<decimal>()))
            .ReturnsAsync((string _, Guid _, int _, decimal originalPrice) => new PromotionPriceDto
            {
                OriginalPrice = originalPrice,
                FinalPrice = originalPrice
            });

        return promoCodeService;
    }

    public ServiceProvider CreateServices(
        OpenCRMContext context,
        IFeatureDefaultsInitializer defaultsInitializer,
        IPromoCodeService? promoCodeService = null,
        IOrganizationPermissionService? organizationPermissionService = null,
        IOrganizationAuditService? organizationAuditService = null,
        IPaymentCreationGateway? paymentCreationGateway = null,
        IPlatformCommercialPolicy? platformCommercialPolicy = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(context);
        services.AddSingleton(defaultsInitializer);
        var permissions = new Mock<IOrganizationPermissionService>();
        permissions.Setup(service => service.HasPermissionAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        services.AddSingleton(organizationPermissionService ?? permissions.Object);
        services.AddSingleton(organizationAuditService ?? new OrganizationAuditService(context));
        services.AddSingleton(paymentCreationGateway ?? Mock.Of<IPaymentCreationGateway>());
        services.AddSingleton(Moq.Mock.Of<OpenCRM.Services.Interfaces.Payments.IYooKassaService>());
        services.AddSingleton(Mock.Of<IWorkspaceAccessService>());
        services.AddBillingExternalDependencies();
        services.AddOpenCRMBilling();
        services.AddSingleton(platformCommercialPolicy
            ?? Mock.Of<IPlatformCommercialPolicy>(policy => policy.CommercialEnabled == true));
        services.AddSingleton(promoCodeService ?? this.CreatePromoCodeService().Object);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public async Task ResetDatabaseAsync()
    {
        await using var context = this.CreateContext();
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE \"admin\".\"NpdReceiptAudits\", \"admin\".\"SubscriptionAudits\", \"admin\".\"PaymentStatusAudits\", "
            + "\"EmailMessages\", \"PaymentCreationOperations\", \"SubscriptionPayments\", \"UserSubscriptions\", \"UserSubscriptionHistory\", "
            + "\"TelegramNotificationOutbox\", \"OrganizationSubscriptions\", \"SubscriptionPlanFeatures\", \"UserFeatureGrants\", "
            + "\"FeatureDefinitions\", \"SubscriptionPlans\", \"Organizations\", \"identity\".\"AspNetUsers\" "
            + "RESTART IDENTITY CASCADE");
    }

    public async Task InitializeAsync()
    {
        await this._postgres.StartAsync();
        await using var context = this.CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => this._postgres.DisposeAsync().AsTask();
}
