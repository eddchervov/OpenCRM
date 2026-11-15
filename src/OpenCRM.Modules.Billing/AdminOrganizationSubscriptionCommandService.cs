using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Billing.Contracts.Dtos;

namespace OpenCRM.Modules.Billing;

/// <summary> Сервис административных команд по подпискам организаций. </summary>
internal sealed class AdminOrganizationSubscriptionCommandService(
    OpenCRMContext opencrmContext,
    AdminSubscriptionMutation mutation)
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _opencrmContext = opencrmContext;

    /// <summary> Зависимость мутации подписок. </summary>
    private readonly AdminSubscriptionMutation _mutation = mutation;

    /// <summary> Выдаёт организации тариф на заданное число дней. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantOrganizationPlanAsync(
        Guid organizationId,
        int planId,
        int days,
        string? reason,
        string adminUserId)
    {
        return this._mutation.ExecuteAsync(() =>
            this.GrantOrganizationPlanCoreAsync(organizationId, planId, days, reason, adminUserId));
    }

    /// <summary> Основная логика выдачи тарифа организации. </summary>
    private async Task<AdminBillingSubscriptionOperationDto> GrantOrganizationPlanCoreAsync(
        Guid organizationId,
        int planId,
        int days,
        string? reason,
        string adminUserId)
    {
        var normalizedReason = reason?.Trim();
        if (days is < 1 or > 366 || string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Укажите срок от 1 до 366 дней и причину длиной до 500 символов.");
        }

        var plan = await this._opencrmContext.SubscriptionPlans
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == planId && item.IsActive
                && item.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                && !item.IsDemoPlan && !item.IsDefaultPlan
                && item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);
        if (plan == null)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Активный тариф организации не найден.");
        }

        var organization = await this._opencrmContext.Organizations
            .FirstOrDefaultAsync(item => item.Id == organizationId);
        if (organization == null)
        {
            return AdminBillingOperationResults.SubscriptionMissing("Организация не найдена.");
        }

        if (!organization.IsActive || organization.IsArchived || organization.IsDeleted)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Тариф нельзя выдать неактивной или архивной организации.");
        }

        var now = DateTime.UtcNow;
        var hasActiveSubscription = await this._opencrmContext.OrganizationSubscriptions
            .AnyAsync(subscription => subscription.OrganizationId == organizationId && subscription.EndDate >= now);
        if (hasActiveSubscription)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("У организации уже есть действующий тариф. Сначала завершите его.");
        }

        var endDate = now.AddDays(days);
        var subscription = new OrganizationSubscription
        {
            OrganizationId = organizationId,
            SubscriptionPlanId = plan.Id,
            SourceCode = "manual",
            StartDate = now,
            EndDate = endDate,
            AutoRenewal = false
        };
        this._opencrmContext.OrganizationSubscriptions.Add(subscription);
        this._opencrmContext.OrganizationAuditEvents.Add(new OrganizationAuditEvent
        {
            OrganizationId = organizationId,
            ActorUserId = adminUserId,
            EventType = "billing.subscription.admin_granted",
            Title = "Тариф предоставлен администратором",
            Description = $"{plan.Name}, до {endDate:dd.MM.yyyy}",
            TargetType = "subscription_plan",
            TargetId = plan.Id.ToString(),
            CreatedAt = now
        });
        organization.UpdatedAt = now;
        await this._opencrmContext.SaveChangesAsync();
        this._mutation.AddAudit("organizations", subscription.Id, organizationId.ToString(), "granted", null, endDate, adminUserId, normalizedReason, now);
        await this._opencrmContext.SaveChangesAsync();

        return new AdminBillingSubscriptionOperationDto
        {
            Succeeded = true,
            Message = $"Тариф предоставлен до {endDate:dd.MM.yyyy} без оплаты.",
            EndDate = endDate
        };
    }

    /// <summary> Выдаёт организации демо-доступ. </summary>
    public Task<AdminBillingSubscriptionOperationDto> GrantOrganizationDemoAsync(
        string userId,
        Guid organizationId,
        string adminUserId,
        string? reason)
    {
        return this.UpdateOrganizationDemoAsync(userId, organizationId, adminUserId, reason, grant: true);
    }

    /// <summary> Отзывает демо-доступ организации. </summary>
    public Task<AdminBillingSubscriptionOperationDto> RevokeOrganizationDemoAsync(
        string userId,
        Guid organizationId,
        string adminUserId,
        string? reason)
    {
        return this.UpdateOrganizationDemoAsync(userId, organizationId, adminUserId, reason, grant: false);
    }

    /// <summary> Выдаёт или отзывает демо-доступ организации. </summary>
    private async Task<AdminBillingSubscriptionOperationDto> UpdateOrganizationDemoAsync(
        string userId,
        Guid organizationId,
        string adminUserId,
        string? reason,
        bool grant)
    {
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 500)
        {
            return AdminBillingOperationResults.SubscriptionInvalid("Укажите причину длиной до 500 символов.");
        }

        return await this._mutation.ExecuteAsync(async () =>
        {
            var now = DateTime.UtcNow;
            var organization = await this._opencrmContext.Organizations
                .FirstOrDefaultAsync(item => item.Id == organizationId
                    && (item.OwnerUserId == userId || item.Members.Any(member => member.UserId == userId)));

            if (organization == null)
            {
                return AdminBillingOperationResults.SubscriptionMissing("Организация не найдена в карточке пользователя.");
            }

            if (grant && (!organization.IsActive || organization.IsArchived || organization.IsDeleted))
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Демо нельзя выдать неактивной или архивной организации.");
            }

            if (grant && organization.PrimaryScenarioCode is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation)
            {
                return AdminBillingOperationResults.SubscriptionInvalid(
                    "Коммерческие тарифы не настроены: рабочее пространство работает без подписки.");
            }

            var activeSubscriptions = await this._opencrmContext.OrganizationSubscriptions
                .Include(item => item.SubscriptionPlan)
                .Where(item => item.OrganizationId == organizationId && item.EndDate >= now)
                .ToListAsync();

            if (grant)
            {
                if (activeSubscriptions.Count > 0)
                {
                    return AdminBillingOperationResults.SubscriptionInvalid(activeSubscriptions.Any(item => !item.SubscriptionPlan.IsDemoPlan)
                        ? "У организации уже действует платный тариф. Демо не выдано."
                        : "Демо уже активно в этой организации.");
                }

                var demoPlan = await this._opencrmContext.SubscriptionPlans
                    .AsNoTracking()
                    .FirstOrDefaultAsync(item => item.IsActive
                        && item.IsDemoPlan
                        && !item.IsDefaultPlan
                        && item.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business);

                if (demoPlan == null || demoPlan.DurationDays <= 0)
                {
                    return AdminBillingOperationResults.SubscriptionInvalid("Активный демо-тариф для организаций не настроен.");
                }

                var endDate = now.AddDays(demoPlan.DurationDays);
                var newDemoSubscription = new OrganizationSubscription
                {
                    OrganizationId = organizationId,
                    SubscriptionPlanId = demoPlan.Id,
                    SourceCode = "demo",
                    StartDate = now,
                    EndDate = endDate,
                    AutoRenewal = false
                };
                this._opencrmContext.OrganizationSubscriptions.Add(newDemoSubscription);
                this._opencrmContext.OrganizationAuditEvents.Add(new OrganizationAuditEvent
                {
                    OrganizationId = organizationId,
                    ActorUserId = adminUserId,
                    EventType = "billing.subscription.admin_demo_granted",
                    Title = "Демо выдано администратором",
                    Description = $"{demoPlan.Name}, до {endDate:dd.MM.yyyy}",
                    TargetType = "subscription_plan",
                    TargetId = demoPlan.Id.ToString(),
                    CreatedAt = now
                });

                organization.UpdatedAt = now;
                await this._opencrmContext.SaveChangesAsync();
                this._mutation.AddAudit("organizations", newDemoSubscription.Id, organizationId.ToString(), "demo_granted", null, endDate, adminUserId, normalizedReason, now);
                await this._opencrmContext.SaveChangesAsync();

                return new AdminBillingSubscriptionOperationDto
                {
                    Succeeded = true,
                    Message = $"Демо для организации выдано до {endDate:dd.MM.yyyy}.",
                    EndDate = endDate
                };
            }

            var demoSubscription = activeSubscriptions
                .Where(item => item.SubscriptionPlan.IsDemoPlan)
                .OrderByDescending(item => item.EndDate)
                .FirstOrDefault();
            if (demoSubscription == null)
            {
                return AdminBillingOperationResults.SubscriptionInvalid("Активного демо у организации нет.");
            }

            var hasOtherActiveSubscription = activeSubscriptions.Any(item => item.Id != demoSubscription.Id);
            var previousEndDate = demoSubscription.EndDate;
            demoSubscription.EndDate = now.AddTicks(-1);
            demoSubscription.AutoRenewal = false;
            this._mutation.AddAudit(
                "organizations", demoSubscription.Id, organizationId.ToString(), "demo_revoked", previousEndDate,
                demoSubscription.EndDate, adminUserId, normalizedReason, now);
            organization.UpdatedAt = now;
            this._opencrmContext.OrganizationAuditEvents.Add(new OrganizationAuditEvent
            {
                OrganizationId = organizationId,
                ActorUserId = adminUserId,
                EventType = "billing.subscription.admin_demo_revoked",
                Title = "Демо отозвано администратором",
                Description = demoSubscription.SubscriptionPlan.Name,
                TargetType = "subscription_plan",
                TargetId = demoSubscription.SubscriptionPlanId.ToString(),
                CreatedAt = now
            });

            await this._opencrmContext.SaveChangesAsync();

            return new AdminBillingSubscriptionOperationDto
            {
                Succeeded = true,
                Message = hasOtherActiveSubscription
                    ? "Демо организации отозвано. Другой действующий тариф сохранён."
                    : organization.PrimaryScenarioCode is OrganizationTypeCodes.Detailing or OrganizationTypeCodes.ServiceStation
                        ? "Демо отозвано. Организация продолжает работу на Free, история сохранена."
                        : "Демо организации отозвано. Рабочее пространство перешло в режим чтения."
            };
        });
    }
}
