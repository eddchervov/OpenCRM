using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts;
using OpenCRM.Services.Interfaces.Subscriptions.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис команд по личным подпискам. </summary>
internal sealed class PersonalSubscriptionCommandService(
    OpenCRMContext context,
    ILogger<PersonalSubscriptionCommandService> logger,
    IPlatformCommercialPolicy? commercialPolicy = null) : IPersonalSubscriptionCommandService
{
    /// <summary> Активирует личный тариф для тестирования. </summary>
    public Task<SubscriptionResult> ActivateSubscriptionForTestingAsync(string userId, int planId) =>
        this.ExecuteCommandAsync(
            () => this.ActivateSubscriptionForTestingCoreAsync(userId, planId),
            "Ошибка при тестовой активации тарифа",
            exception => logger.LogError(exception, "Error activating test subscription for user {UserId}, plan {PlanId}", userId, planId));

    /// <summary> Продлевает личную подписку для тестирования. </summary>
    public Task<SubscriptionResult> ExtendSubscriptionForTestingAsync(string userId) =>
        this.ExecuteCommandAsync(
            () => this.ExtendSubscriptionForTestingCoreAsync(userId),
            "Ошибка при тестовом продлении тарифа",
            exception => logger.LogError(exception, "Error extending test subscription for user {UserId}", userId));

    /// <summary> Отменяет личную подписку для тестирования. </summary>
    public Task<SubscriptionResult> CancelSubscriptionForTestingAsync(string userId) =>
        this.ExecuteCommandAsync(
            () => this.CancelSubscriptionForTestingCoreAsync(userId),
            "Ошибка при тестовой отмене тарифа",
            exception => logger.LogError(exception, "Error canceling test subscription for user {UserId}", userId));

    /// <summary> Выдаёт пользователю демо-подписку. </summary>
    public Task<SubscriptionResult> GrantDemoSubscriptionAsync(string userId) =>
        this.ExecuteCommandAsync(
            () => this.GrantDemoSubscriptionCoreAsync(userId),
            "Ошибка при активации демо-подписки",
            exception => logger.LogError(exception, "Error granting demo subscription to user {UserId}", userId));

    /// <summary> Выдаёт пользователю демо-подписку от имени администратора. </summary>
    public Task<SubscriptionResult> GrantDemoSubscriptionByAdminAsync(string userId) =>
        this.ExecuteCommandAsync(
            () => this.GrantDemoSubscriptionByAdminCoreAsync(userId),
            "Ошибка при выдаче демо-тарифа",
            exception => logger.LogError(exception, "Error granting admin demo subscription to user {UserId}", userId));

    /// <summary> Назначает пользователю тариф от имени администратора. </summary>
    public Task<SubscriptionResult> AssignSubscriptionByAdminAsync(string userId, int planId, string adminName) =>
        this.ExecuteCommandAsync(
            () => this.AssignSubscriptionByAdminCoreAsync(userId, planId, adminName),
            "Ошибка при ручном назначении тарифа",
            exception => logger.LogError(exception, "Error assigning plan {PlanId} by admin {AdminName} to user {UserId}", planId, adminName, userId));

    /// <summary> Возвращает пользователя на тариф по умолчанию от имени администратора. </summary>
    public Task<SubscriptionResult> ReturnUserToDefaultPlanByAdminAsync(string userId, string adminName) =>
        this.ExecuteCommandAsync(
            () => this.ReturnUserToDefaultPlanByAdminCoreAsync(userId, adminName),
            "Ошибка при возврате пользователя на базовый тариф",
            exception => logger.LogError(exception, "Error returning user {UserId} to default plan by admin {AdminName}", userId, adminName));

    /// <summary> Деактивирует истёкшие личные подписки. </summary>
    public async Task CheckAndDeactivateExpiredSubscriptionsAsync()
    {
        if (commercialPolicy?.CommercialEnabled != true)
        {
            return;
        }

        var expiredCount = 0;

        try
        {
            await this.ExecuteSubscriptionTransactionAsync(async () =>
            {
                var cutoff = DateTime.UtcNow;
                var expiredSubscriptions = await context.UserSubscriptions
                    .FromSqlInterpolated($"SELECT * FROM \"UserSubscriptions\" WHERE \"EndDate\" < {cutoff} FOR UPDATE")
                    .ToListAsync();

                foreach (var subscription in expiredSubscriptions)
                {
                    this.CreateHistoryRecord(subscription, "expired", "Подписка истекла автоматически");
                    context.UserSubscriptions.Remove(subscription);
                }

                await context.SaveChangesAsync();
                expiredCount = expiredSubscriptions.Count;

                return SubscriptionResult.CreateSuccess();
            });
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Error deactivating expired subscriptions");
            throw;
        }

        logger.LogInformation("Moved {Count} expired subscriptions to history", expiredCount);
    }

    /// <summary> Внутренняя логика активации тарифа для тестирования. </summary>
    private async Task<SubscriptionResult> ActivateSubscriptionForTestingCoreAsync(string userId, int planId)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return SubscriptionResult.CreateFailed("Пользователь не найден");
        }

        if (!user.CanUseTestSubscriptions)
        {
            return SubscriptionResult.CreateFailed("Тестовое управление тарифами недоступно.");
        }

        var plan = await context.SubscriptionPlans.FindAsync(planId);
        if (plan == null || !plan.IsActive)
        {
            return SubscriptionResult.CreateFailed("Тарифный план не найден или неактивен");
        }

        var currentSubscription = await context.UserSubscriptions
            .FirstOrDefaultAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);
        var newSubscription = new UserSubscription
        {
            UserId = userId,
            SubscriptionPlanId = planId,
            SourceCode = "manual",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(plan.DurationDays),
            AutoRenewal = false
        };

        await context.UserSubscriptions.AddAsync(newSubscription);
        if (currentSubscription != null)
        {
            context.UserSubscriptions.Remove(currentSubscription);
        }

        await context.SaveChangesAsync();
        this.CreateHistoryRecord(
            newSubscription,
            currentSubscription != null ? "replaced" : "created",
            $"Тестовая активация тарифа без оплаты. План: {plan.Name}");
        await context.SaveChangesAsync();

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Внутренняя логика продления подписки для тестирования. </summary>
    private async Task<SubscriptionResult> ExtendSubscriptionForTestingCoreAsync(string userId)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return SubscriptionResult.CreateFailed("Пользователь не найден");
        }

        if (!user.CanUseTestSubscriptions)
        {
            return SubscriptionResult.CreateFailed("Тестовое управление тарифами недоступно.");
        }

        var currentSubscription = await context.UserSubscriptions
            .Include(subscription => subscription.SubscriptionPlan)
            .FirstOrDefaultAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);
        if (currentSubscription == null)
        {
            return SubscriptionResult.CreateFailed("У вас нет активной подписки для продления");
        }

        var oldEndDate = currentSubscription.EndDate;
        currentSubscription.EndDate = currentSubscription.EndDate.AddDays(currentSubscription.SubscriptionPlan.DurationDays);
        this.CreateHistoryRecord(
            currentSubscription,
            "extended",
            $"Тестовое продление без оплаты. Старая дата окончания: {oldEndDate:dd.MM.yyyy}, новая дата: {currentSubscription.EndDate:dd.MM.yyyy}");
        await context.SaveChangesAsync();
        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Внутренняя логика отмены подписки для тестирования. </summary>
    private async Task<SubscriptionResult> CancelSubscriptionForTestingCoreAsync(string userId)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return SubscriptionResult.CreateFailed("Пользователь не найден");
        }

        if (!user.CanUseTestSubscriptions)
        {
            return SubscriptionResult.CreateFailed("Тестовое управление тарифами недоступно.");
        }

        var currentSubscription = await context.UserSubscriptions
            .FirstOrDefaultAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);
        if (currentSubscription == null)
        {
            return SubscriptionResult.CreateFailed("У вас нет активной подписки для отмены");
        }

        this.CreateHistoryRecord(currentSubscription, "cancelled", "Тестовая отмена подписки без оплаты");
        context.UserSubscriptions.Remove(currentSubscription);
        await context.SaveChangesAsync();
        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Внутренняя логика выдачи демо-подписки. </summary>
    private async Task<SubscriptionResult> GrantDemoSubscriptionCoreAsync(string userId)
    {
        var demoPlan = await context.SubscriptionPlans
            .FirstOrDefaultAsync(plan =>
                plan.IsActive
                && plan.IsDemoPlan
                && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);
        if (demoPlan == null)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф не настроен в системе");
        }

        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return SubscriptionResult.CreateFailed("Пользователь не найден");
        }

        var currentSubscription = await context.UserSubscriptions
            .Include(subscription => subscription.SubscriptionPlan)
            .FirstOrDefaultAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);
        if (currentSubscription?.SubscriptionPlan.IsDemoPlan == true)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф уже активен.");
        }

        if (currentSubscription != null && !currentSubscription.SubscriptionPlan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("У вас уже есть активная платная подписка.");
        }

        var hasPaidHistory = await context.UserSubscriptionHistory
            .Include(history => history.SubscriptionPlan)
            .AnyAsync(history => history.UserId == userId && !history.SubscriptionPlan.IsDemoPlan);
        if (hasPaidHistory)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф доступен только тем, кто еще не покупал платные тарифы.");
        }

        var demoSubscription = new UserSubscription
        {
            UserId = userId,
            SubscriptionPlanId = demoPlan.Id,
            SourceCode = "demo",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(demoPlan.DurationDays),
            AutoRenewal = false
        };
        await context.UserSubscriptions.AddAsync(demoSubscription);
        await context.UserSubscriptionHistory.AddAsync(new UserSubscriptionHistory
        {
            UserId = userId,
            SubscriptionPlanId = demoPlan.Id,
            StartDate = demoSubscription.StartDate,
            EndDate = demoSubscription.EndDate,
            Status = "created",
            AutoRenewal = false,
            SourceCode = "demo",
            CreatedAt = DateTime.UtcNow,
            Comment = "Активирована демо-подписка"
        });
        await context.SaveChangesAsync();
        logger.LogInformation("Demo subscription granted to user {UserId}", userId);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Внутренняя логика выдачи демо-подписки администратором. </summary>
    private async Task<SubscriptionResult> GrantDemoSubscriptionByAdminCoreAsync(string userId)
    {
        var demoPlan = await context.SubscriptionPlans
            .FirstOrDefaultAsync(plan =>
                plan.IsActive
                && plan.IsDemoPlan
                && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal);
        if (demoPlan == null)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф не настроен в системе");
        }

        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return SubscriptionResult.CreateFailed("Пользователь не найден");
        }

        var currentSubscription = await context.UserSubscriptions
            .FirstOrDefaultAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);
        var demoSubscription = new UserSubscription
        {
            UserId = userId,
            SubscriptionPlanId = demoPlan.Id,
            SourceCode = "demo",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(demoPlan.DurationDays),
            AutoRenewal = false
        };
        await context.UserSubscriptions.AddAsync(demoSubscription);
        if (currentSubscription != null)
        {
            context.UserSubscriptions.Remove(currentSubscription);
        }

        await context.SaveChangesAsync();
        this.CreateHistoryRecord(
            demoSubscription,
            currentSubscription != null ? "replaced" : "created",
            "Демо-тариф выдан администратором");
        await context.SaveChangesAsync();
        logger.LogInformation("Admin granted demo subscription to user {UserId}", userId);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Внутренняя логика назначения тарифа администратором. </summary>
    private async Task<SubscriptionResult> AssignSubscriptionByAdminCoreAsync(
        string userId,
        int planId,
        string adminName)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return SubscriptionResult.CreateFailed("Пользователь не найден");
        }

        var plan = await context.SubscriptionPlans.FindAsync(planId);
        if (plan == null || !plan.IsActive)
        {
            return SubscriptionResult.CreateFailed("Тарифный план не найден или неактивен");
        }

        if (SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) != SubscriptionPlanCatalogCodes.PlanTypes.Personal)
        {
            return SubscriptionResult.CreateFailed("Бизнес-тариф нельзя назначить пользователю напрямую.");
        }

        if (plan.IsDefaultPlan)
        {
            return SubscriptionResult.CreateFailed("Базовый тариф нельзя назначить вручную");
        }

        if (plan.IsDemoPlan)
        {
            return SubscriptionResult.CreateFailed("Демо-тариф выдавайте через отдельную кнопку");
        }

        var currentSubscription = await context.UserSubscriptions
            .Include(subscription => subscription.SubscriptionPlan)
            .FirstOrDefaultAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);
        var newSubscription = new UserSubscription
        {
            UserId = userId,
            SubscriptionPlanId = planId,
            SourceCode = "manual",
            StartDate = DateTime.UtcNow,
            EndDate = DateTime.UtcNow.AddDays(plan.DurationDays),
            AutoRenewal = false
        };
        await context.UserSubscriptions.AddAsync(newSubscription);
        if (currentSubscription != null)
        {
            context.UserSubscriptions.Remove(currentSubscription);
        }

        await context.SaveChangesAsync();
        var actionComment = currentSubscription != null
            ? $"Тариф вручную заменен администратором. Новый план: {plan.Name}."
            : $"Тариф вручную назначен администратором. План: {plan.Name}.";
        this.CreateHistoryRecord(newSubscription, "admin_assigned", actionComment);
        await context.SaveChangesAsync();
        logger.LogInformation("Admin {AdminName} assigned plan {PlanId} to user {UserId}", adminName, planId, userId);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Внутренняя логика возврата на тариф по умолчанию. </summary>
    private async Task<SubscriptionResult> ReturnUserToDefaultPlanByAdminCoreAsync(
        string userId,
        string adminName)
    {
        var user = await context.Users.FindAsync(userId);
        if (user == null)
        {
            return SubscriptionResult.CreateFailed("Пользователь не найден");
        }

        var currentSubscription = await context.UserSubscriptions
            .Include(subscription => subscription.SubscriptionPlan)
            .FirstOrDefaultAsync(subscription => subscription.UserId == userId && subscription.EndDate >= DateTime.UtcNow);
        if (currentSubscription == null)
        {
            return SubscriptionResult.CreateFailed("У пользователя нет активного тарифа для снятия");
        }

        this.CreateHistoryRecord(
            currentSubscription,
            "admin_cancelled",
            "Тариф снят администратором. Пользователь переведен на стандартный тариф.");
        context.UserSubscriptions.Remove(currentSubscription);
        await context.SaveChangesAsync();
        logger.LogInformation("Admin {AdminName} returned user {UserId} to default plan", adminName, userId);

        return SubscriptionResult.CreateSuccess();
    }

    /// <summary> Выполняет команду с обработкой и логированием ошибок. </summary>
    private async Task<SubscriptionResult> ExecuteCommandAsync(
        Func<Task<SubscriptionResult>> action,
        string errorMessage,
        Action<Exception> logException)
    {
        try
        {
            return await this.ExecuteSubscriptionTransactionAsync(action);
        }
        catch (Exception exception)
        {
            logException(exception);

            return SubscriptionResult.CreateFailed(errorMessage);
        }
    }

    /// <summary> Выполняет действие с подпиской в транзакции. </summary>
    private async Task<SubscriptionResult> ExecuteSubscriptionTransactionAsync(Func<Task<SubscriptionResult>> action)
    {
        if (context.Database.CurrentTransaction != null)
        {
            throw new InvalidOperationException("Команду подписки нельзя выполнять внутри внешней транзакции.");
        }

        var isRetry = false;
        var historyIdsCreatedByCommand = new HashSet<int>();
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            if (isRetry)
            {
                context.ChangeTracker.Clear();
            }

            isRetry = true;
            if (historyIdsCreatedByCommand.Count > 0
                && await context.UserSubscriptionHistory
                    .AsNoTracking()
                    .AnyAsync(history => historyIdsCreatedByCommand.Contains(history.Id)))
            {
                return SubscriptionResult.CreateSuccess();
            }

            var existingHistory = new HashSet<UserSubscriptionHistory>(
                context.ChangeTracker.Entries<UserSubscriptionHistory>().Select(entry => entry.Entity),
                ReferenceEqualityComparer.Instance);
            using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            var result = await action();
            if (result.Success)
            {
                var createdHistoryIds = context.ChangeTracker.Entries<UserSubscriptionHistory>()
                    .Select(entry => entry.Entity)
                    .Where(history => !existingHistory.Contains(history))
                    .Select(history => history.Id)
                    .ToList();
                foreach (var historyId in createdHistoryIds)
                {
                    historyIdsCreatedByCommand.Add(historyId);
                }

                await transaction.CommitAsync();
            }
            else
            {
                await transaction.RollbackAsync();
            }

            return result;
        });
    }

    /// <summary> Создаёт запись в истории подписки. </summary>
    private void CreateHistoryRecord(UserSubscription subscription, string status, string comment)
    {
        context.UserSubscriptionHistory.Add(new UserSubscriptionHistory
        {
            UserId = subscription.UserId,
            SubscriptionPlanId = subscription.SubscriptionPlanId,
            StartDate = subscription.StartDate,
            EndDate = subscription.EndDate,
            ActualEndDate = status is "expired" or "cancelled" ? DateTime.UtcNow : null,
            Status = status,
            AutoRenewal = subscription.AutoRenewal,
            SourceCode = subscription.SourceCode,
            CreatedAt = DateTime.UtcNow,
            Comment = comment
        });

        logger.LogInformation("Created history record for subscription {SubscriptionId} with status {Status}", subscription.Id, status);
    }
}
