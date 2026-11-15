using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис команд по подпискам организаций. </summary>
internal sealed class OrganizationSubscriptionCommandService(
    OpenCRMContext context,
    IFeatureDefaultsInitializer featureDefaultsInitializer,
    IOrganizationEntitlementService organizationEntitlementService,
    IOrganizationPermissionService organizationPermissionService,
    IOrganizationAuditService organizationAuditService,
    ILogger<OrganizationSubscriptionCommandService> logger) : IOrganizationSubscriptionCommandService
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Зависимость сервиса прав организации. </summary>
    private readonly IOrganizationEntitlementService _organizationEntitlementService = organizationEntitlementService;

    /// <summary> Зависимость инициализатора возможностей. </summary>
    private readonly IFeatureDefaultsInitializer _featureDefaultsInitializer = featureDefaultsInitializer;

    /// <summary> Зависимость сервиса прав организаций. </summary>
    private readonly IOrganizationPermissionService _organizationPermissionService = organizationPermissionService;

    /// <summary> Зависимость сервиса аудита организаций. </summary>
    private readonly IOrganizationAuditService _organizationAuditService = organizationAuditService;

    /// <summary> Логгер сервиса. </summary>
    private readonly ILogger<OrganizationSubscriptionCommandService> _logger = logger;

    /// <summary> Активирует демо-подписку организации на выбранный тариф. </summary>
    public async Task<SubscriptionResult> ActivateDemoAsync(string userId, Guid organizationId, int planId, int? durationDays = null)
    {
        await this._featureDefaultsInitializer.EnsureDefaultsAsync();
        var organization = await this.EnsureCanManageBillingAsync(userId, organizationId);

        var plan = await this._context.SubscriptionPlans
            .FirstOrDefaultAsync(x => x.Id == planId
                && x.IsActive
                && x.IsDemoPlan
                && !x.IsDefaultPlan
                && x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);

        if (plan is null)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф не найден или недоступен.");
        }

        var demoAvailability = await this._organizationEntitlementService.CheckDemoAvailabilityAsync(userId, organizationId);
        if (!demoAvailability.Available)
        {
            return SubscriptionResult.CreateFailed(demoAvailability.Message);
        }

        await this.ActivatePlanWithoutPaymentAsync(
            organization,
            userId,
            plan,
            "billing.subscription.activated_demo",
            "Демо-тариф активирован",
            durationDays);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Активирует тариф организации для тестирования без оплаты. </summary>
    public async Task<SubscriptionResult> ActivateForTestingAsync(string userId, Guid organizationId, int planId)
    {
        await this._featureDefaultsInitializer.EnsureDefaultsAsync();

        return await this.ExecuteSubscriptionTransactionAsync(
            () => this.ActivateForTestingCoreAsync(userId, organizationId, planId), IsolationLevel.Serializable);
    }

    /// <summary> Внутренняя логика активации тарифа для тестирования. </summary>
    private async Task<SubscriptionResult> ActivateForTestingCoreAsync(string userId, Guid organizationId, int planId)
    {
        var organization = await this.EnsureCanManageBillingAsync(userId, organizationId);

        var user = await this._context.ApplicationUsers
            .AsNoTracking()
            .FirstAsync(x => x.Id == userId);

        var plan = await this._context.SubscriptionPlans
            .FirstOrDefaultAsync(x => x.Id == planId
                && x.IsActive
                && !x.IsDefaultPlan
                && x.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                && x.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);

        if (plan is null)
        {
            return SubscriptionResult.CreateFailed("Бизнес-тариф не найден или недоступен.");
        }

        if (!user.CanUseTestSubscriptions)
        {
            return SubscriptionResult.CreateFailed("Тестовая активация бизнес-тарифа недоступна для этого аккаунта.");
        }

        var currentSubscription = await this.LoadCurrentSubscriptionAsync(organizationId);
        if (currentSubscription != null
            && !currentSubscription.SubscriptionPlan.IsDemoPlan
            && !currentSubscription.SubscriptionPlan.IsDefaultPlan)
        {
            return SubscriptionResult.CreateFailed(
                $"Сменить тариф можно после окончания текущего: {currentSubscription.EndDate:dd.MM.yyyy}. "
                + "Продление текущего тарифа откроется за 7 дней до окончания.");
        }

        await this.ActivatePlanWithoutPaymentAsync(
            organization,
            userId,
            plan,
            "billing.subscription.activated_test",
            "Тариф активирован в тестовом режиме");

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Продлевает подписку организации для тестирования. </summary>
    public async Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId, Guid organizationId)
    {
        try
        {
            return await this.ExecuteSubscriptionTransactionAsync(
                () => this.ExtendSubscriptionForTestingCoreAsync(userId, organizationId), IsolationLevel.ReadCommitted);
        }
        catch (Exception exception)
        {
            this._logger.LogError(exception, "Ошибка тестового продления тарифа организации {OrganizationId}.", organizationId);

            return SubscriptionResult.CreateFailed("Ошибка при тестовом продлении тарифа.");
        }
    }

    /// <summary> Внутренняя логика продления подписки для тестирования. </summary>
    private async Task<SubscriptionResult> ExtendSubscriptionForTestingCoreAsync(string userId, Guid organizationId)
    {
        await this._featureDefaultsInitializer.EnsureDefaultsAsync();
        await this.EnsureCanManageBillingAsync(userId, organizationId);

        var user = await this._context.ApplicationUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId)
            ?? throw new InvalidOperationException("Пользователь не найден.");

        if (!user.CanUseTestSubscriptions)
        {
            return SubscriptionResult.CreateFailed("Тестовое управление тарифами недоступно для этого аккаунта.");
        }

        var currentSubscription = await this._context.OrganizationSubscriptions
            .Include(x => x.SubscriptionPlan)
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.EndDate >= DateTime.UtcNow);

        if (currentSubscription == null)
        {
            return SubscriptionResult.CreateFailed("У рабочего пространства нет активного тарифа для продления.");
        }

        if (currentSubscription.SubscriptionPlan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф нельзя продлить.");
        }

        if (currentSubscription.SubscriptionPlan.IsDefaultPlan)
        {
            return SubscriptionResult.CreateFailed("Базовый тариф не продлевается.");
        }

        if ((currentSubscription.EndDate - DateTime.UtcNow).TotalDays > 7)
        {
            var daysUntilExpiry = (int)Math.Ceiling((currentSubscription.EndDate - DateTime.UtcNow).TotalDays);
            return SubscriptionResult.CreateFailed($"Продление откроется за 7 дней до окончания. Осталось дней: {daysUntilExpiry}.");
        }

        var oldEndDate = currentSubscription.EndDate;
        currentSubscription.EndDate = currentSubscription.EndDate.AddDays(currentSubscription.SubscriptionPlan.DurationDays);
        await this.AddBillingAuditAsync(
            organizationId,
            userId,
            "billing.subscription.extended_test",
            "Тариф продлен в тестовом режиме",
            $"{currentSubscription.SubscriptionPlan.Name}: {oldEndDate:dd.MM.yyyy} -> {currentSubscription.EndDate:dd.MM.yyyy}",
            currentSubscription.SubscriptionPlanId,
            saveChanges: false);

        await this._context.SaveChangesAsync();

        this._logger.LogInformation(
            "Extended business subscription in test mode. User: {UserId}, Organization: {OrganizationId}, OldEndDate: {OldEndDate}, NewEndDate: {NewEndDate}",
            userId,
            organizationId,
            oldEndDate,
            currentSubscription.EndDate);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Загружает текущую подписку организации или null, если её нет. </summary>
    private async Task<OrganizationSubscription?> LoadCurrentSubscriptionAsync(Guid organizationId)
    {
        return await this._context.OrganizationSubscriptions
            .AsNoTracking()
            .Include(x => x.SubscriptionPlan)
                .ThenInclude(x => x.Features)
                    .ThenInclude(x => x.FeatureDefinition)
            .Where(x => x.OrganizationId == organizationId && x.EndDate >= DateTime.UtcNow)
            .OrderByDescending(x => x.EndDate)
            .FirstOrDefaultAsync();
    }

    /// <summary> Активирует тариф организации без проведения оплаты и пишет аудит. </summary>
    private async Task ActivatePlanWithoutPaymentAsync(
        Organization organization,
        string userId,
        SubscriptionPlan plan,
        string eventType,
        string auditTitle,
        int? durationDays = null)
    {
        var now = DateTime.UtcNow;
        var subscriptionDurationDays = durationDays ?? plan.DurationDays;
        var activeSubscriptions = await this._context.OrganizationSubscriptions
            .Where(x => x.OrganizationId == organization.Id && x.EndDate >= now)
            .ToListAsync();

        foreach (var activeSubscription in activeSubscriptions)
        {
            activeSubscription.EndDate = now;
        }

        this._context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organization.Id,
            SubscriptionPlanId = plan.Id,
            SourceCode = plan.IsDemoPlan ? "demo" : "manual",
            StartDate = now,
            EndDate = now.AddDays(subscriptionDurationDays),
            AutoRenewal = false
        });

        organization.UpdatedAt = now;
        await this.AddBillingAuditAsync(
            organization.Id,
            userId,
            eventType,
            auditTitle,
            $"{plan.Name}, до {now.AddDays(subscriptionDurationDays):dd.MM.yyyy}",
            plan.Id,
            saveChanges: false);

        await this._context.SaveChangesAsync();
    }

    /// <summary> Проверяет право пользователя управлять биллингом организации. </summary>
    private async Task<Organization> EnsureCanManageBillingAsync(string userId, Guid organizationId)
    {
        var organization = await this._context.Organizations
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == organizationId && !x.IsDeleted && !x.IsArchived && x.IsActive)
            ?? throw new InvalidOperationException("Организация не найдена.");

        var roleCode = organization.Members
            .FirstOrDefault(x => x.UserId == userId)
            ?.RoleCode;

        if (roleCode is null)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        if (!await this._organizationPermissionService.HasPermissionAsync(userId, organizationId, OrganizationPermissionCodes.BillingManage))
        {
            throw new InvalidOperationException(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage));
        }

        return organization;
    }

    /// <summary> Добавляет запись аудита биллинга с сохранением при необходимости. </summary>
    private Task AddBillingAuditAsync(
        Guid organizationId,
        string userId,
        string eventType,
        string title,
        string? description,
        int planId,
        bool saveChanges)
    {
        return this._organizationAuditService.AddAsync(new OrganizationAuditEventCreateDto
        {
            OrganizationId = organizationId,
            ActorUserId = userId,
            EventType = eventType,
            Title = title,
            Description = description,
            TargetType = "subscription_plan",
            TargetId = planId.ToString()
        }, saveChanges);
    }

    /// <summary> Выполняет действие с подпиской в транзакции с заданной изоляцией. </summary>
    private async Task<SubscriptionResult> ExecuteSubscriptionTransactionAsync(
        Func<Task<SubscriptionResult>> action,
        IsolationLevel isolationLevel)
    {
        if (this._context.Database.CurrentTransaction != null)
        {
            throw new InvalidOperationException("Команду подписки нельзя выполнять внутри внешней транзакции.");
        }

        var originalAuditIds = this._context.ChangeTracker.Entries<OrganizationAuditEvent>()
            .Select(entry => entry.Entity.Id).ToHashSet();
        Guid[] candidateAuditIds = [];
        SubscriptionResult? candidateResult = null;
        var commitAttempted = false;
        var isRetry = false;
        var strategy = this._context.Database.CreateExecutionStrategy();

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                if (isRetry)
                {
                    this._context.ChangeTracker.Clear();
                    if (commitAttempted)
                    {
                        if (candidateAuditIds.Length == 0)
                        {
                            throw new InvalidOperationException("Результат фиксации команды нельзя подтвердить без записи аудита.");
                        }

                        var committedCount = await this._context.OrganizationAuditEvents.AsNoTracking()
                            .CountAsync(audit => candidateAuditIds.Contains(audit.Id));
                        if (committedCount == candidateAuditIds.Length)
                        {
                            return candidateResult!;
                        }
                    }
                }

                isRetry = true;
                commitAttempted = false;
                using var transaction = await this._context.Database.BeginTransactionAsync(isolationLevel);
                var result = await action();
                if (!result.Success)
                {
                    return result;
                }

                candidateAuditIds = this._context.ChangeTracker.Entries<OrganizationAuditEvent>()
                    .Select(entry => entry.Entity.Id)
                    .Where(id => !originalAuditIds.Contains(id)).ToArray();
                candidateResult = result;
                commitAttempted = true;
                // Аудит фиксируется вместе с подпиской и подтверждает исход при потере ответа COMMIT.
                await transaction.CommitAsync();

                return result;
            });
        }
        catch
        {
            this._context.ChangeTracker.Clear();
            throw;
        }
    }
}
