using System.Data;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Pagination;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Promotions.Contracts;
using OpenCRM.Modules.Promotions.Contracts.Dtos;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Services.Interfaces.Organizations.Dtos;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Promotions;

/// <summary>
/// Применение промокода к организации: немедленная выдача подарка или закрепление скидки.
/// </summary>
internal sealed class OrganizationPromoCodeService(
    OpenCRMContext context,
    IOrganizationPermissionService organizationPermissionService,
    IOrganizationAuditService organizationAuditService,
    IOrganizationPromoAccessGrantPort organizationPromoAccessGrantPort) : IOrganizationPromoCodeService
{
    /// <summary> Применяет промокод к организации и возвращает результат применения. </summary>
    public async Task<OrganizationPromoApplicationDto> ApplyAsync(string actorUserId, Guid organizationId, string code)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new InvalidOperationException("Не указан пользователь, выполняющий действие.");
        }

        var normalizedCode = (code ?? string.Empty).Trim().ToUpperInvariant();

        if (normalizedCode.Length < 3 || normalizedCode.Length > 64
            || normalizedCode.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidOperationException("Неверный формат промокода.");
        }

        if (context.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
        {
            throw new InvalidOperationException("Применение промокода нельзя выполнять внутри внешней транзакции.");
        }

        if (context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Применение промокода нужно выполнять в отдельной единице работы без ожидающих изменений.");
        }

        var strategy = context.Database.CreateExecutionStrategy();
        var isRetry = false;
        long? attemptedApplicationId = null;

        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                if (isRetry)
                {
                    context.ChangeTracker.Clear();
                }

                isRetry = true;

                await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);

                // Блокировка строки организации, затем строки промокода — фиксированный порядок.
                var organization = await context.Organizations
                    .FromSqlInterpolated($"SELECT * FROM \"Organizations\" WHERE \"Id\" = {organizationId} FOR UPDATE")
                    .AsNoTracking()
                    .SingleOrDefaultAsync();

                if (organization == null || !organization.IsActive || organization.IsDeleted || organization.IsArchived)
                {
                    throw new InvalidOperationException("Организация не найдена или недоступна.");
                }

                if (!await context.OrganizationMembers.AnyAsync(member => member.OrganizationId == organizationId && member.UserId == actorUserId))
                {
                    throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
                }

                if (!await organizationPermissionService.HasPermissionAsync(actorUserId, organizationId, OrganizationPermissionCodes.BillingManage))
                {
                    throw new UnauthorizedAccessException(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage));
                }

                var promo = await context.PromoCodes
                    .FromSqlInterpolated($"SELECT * FROM \"PromoCodes\" WHERE \"Code\" = {normalizedCode} FOR UPDATE")
                    .AsNoTracking()
                    .SingleOrDefaultAsync();

                if (promo == null)
                {
                    throw new InvalidOperationException("Промокод не найден.");
                }

                if (!promo.IsOrganizationCode)
                {
                    throw new InvalidOperationException("Этот промокод больше не поддерживается. Используйте действующий промокод организации.");
                }

                var now = DateTime.UtcNow;

                if (!promo.IsActive
                    || (promo.ValidFrom.HasValue && promo.ValidFrom.Value > now)
                    || (promo.ValidUntil.HasValue && promo.ValidUntil.Value < now))
                {
                    throw new InvalidOperationException("Промокод недействителен или срок его действия истёк.");
                }

                // Постоянное закрепление: дубликат отклоняется в любом статусе.
                var isDuplicate = await context.PromoCodeApplications
                    .AnyAsync(application => application.PromoCodeId == promo.Id && application.OrganizationId == organizationId);

                if (isDuplicate)
                {
                    if (attemptedApplicationId.HasValue)
                    {
                        var completedAttempt = await context.PromoCodeApplications
                            .AsNoTracking()
                            .SingleOrDefaultAsync(application => application.Id == attemptedApplicationId.Value
                                && application.PromoCodeId == promo.Id && application.OrganizationId == organizationId);

                        if (completedAttempt is not null)
                        {
                            // Подтверждение транзакции могло потеряться после её успешного завершения в БД.
                            await transaction.CommitAsync();

                            return MapApplication(completedAttempt, promo);
                        }
                    }

                    throw new InvalidOperationException("Промокод уже применён к этой организации.");
                }

                if (promo.MaximumOrganizationApplications.HasValue)
                {
                    var totalApplications = await context.PromoCodeApplications
                        .CountAsync(application => application.PromoCodeId == promo.Id);

                    if (totalApplications >= promo.MaximumOrganizationApplications.Value)
                    {
                        throw new InvalidOperationException("Достигнут максимальный лимит применений промокода.");
                    }
                }

                // Сценарии и тарифы загружаются отдельными запросами после блокировки.
                var scenarioCodes = await context.PromoCodeScenarios
                    .AsNoTracking()
                    .Where(item => item.PromoCodeId == promo.Id)
                    .Select(item => item.ScenarioCode)
                    .ToListAsync();

                if (!promo.AppliesToAllScenarios && !scenarioCodes.Contains(organization.PrimaryScenarioCode))
                {
                    throw new InvalidOperationException("Промокод не действует для сценария этой организации.");
                }

                var planIds = await context.PromoCodePlans
                    .AsNoTracking()
                    .Where(item => item.PromoCodeId == promo.Id)
                    .Select(item => item.SubscriptionPlanId)
                    .ToListAsync();

                var benefitType = promo.DiscountTypeCode?.Trim().ToLowerInvariant() ?? string.Empty;
                int? bonusDays = null;
                decimal? discountValue = null;

                if (benefitType == PromoCodeCatalogCodes.DiscountTypes.BonusDays)
                {
                    if (!promo.GiftTemplatePlanId.HasValue)
                    {
                        throw new InvalidOperationException("У промокода не задан подарочный тариф.");
                    }

                    if (!promo.BonusDays.HasValue || promo.BonusDays.Value < 1 || promo.BonusDays.Value > 3650)
                    {
                        throw new InvalidOperationException("Количество подарочных дней должно быть от 1 до 3650.");
                    }

                    var giftTemplatePlan = await context.SubscriptionPlans
                        .AsNoTracking()
                        .SingleOrDefaultAsync(plan => plan.Id == promo.GiftTemplatePlanId.Value);

                    if (giftTemplatePlan == null
                        || !giftTemplatePlan.IsActive
                        || giftTemplatePlan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                        || SubscriptionPlanCatalogCodes.NormalizePlanType(giftTemplatePlan.PlanTypeCode) != SubscriptionPlanCatalogCodes.PlanTypes.Business
                        || giftTemplatePlan.IsDefaultPlan
                        || giftTemplatePlan.IsDemoPlan)
                    {
                        throw new InvalidOperationException("Подарочный тариф промокода недоступен.");
                    }

                    bonusDays = promo.BonusDays.Value;
                }
                else if (benefitType == PromoCodeCatalogCodes.DiscountTypes.Percentage)
                {
                    if (!promo.DiscountValue.HasValue || promo.DiscountValue.Value <= 0 || promo.DiscountValue.Value > 99)
                    {
                        throw new InvalidOperationException("Процент скидки должен быть от 1 до 99.");
                    }

                    discountValue = promo.DiscountValue.Value;
                }
                else if (benefitType == PromoCodeCatalogCodes.DiscountTypes.FixedAmount)
                {
                    if (!promo.DiscountValue.HasValue || promo.DiscountValue.Value <= 0)
                    {
                        throw new InvalidOperationException("Сумма скидки должна быть положительной.");
                    }

                    discountValue = promo.DiscountValue.Value;
                }
                else
                {
                    throw new InvalidOperationException("Тип выгоды промокода не поддерживается для организаций.");
                }

                // Срок действия промокода влияет только на момент применения: денежная выгода
                // закрепляет срок для этой организации, подарочные дни срока не имеют.
                DateTime? discountValidUntil = null;

                if (benefitType == PromoCodeCatalogCodes.DiscountTypes.BonusDays)
                {
                    if (promo.DiscountValidityDays.HasValue)
                    {
                        throw new InvalidOperationException("Промокод с подарочными днями не может иметь срок действия скидки.");
                    }
                }
                else
                {
                    if (promo.DiscountValidityDays.HasValue
                        && (promo.DiscountValidityDays.Value < 1
                            || promo.DiscountValidityDays.Value > 3650))
                    {
                        throw new InvalidOperationException("Срок действия скидки должен быть от 1 до 3650 дней.");
                    }

                    discountValidUntil = promo.DiscountValidityDays.HasValue
                        ? now.AddDays(promo.DiscountValidityDays.Value)
                        : null;
                }

                if (benefitType != PromoCodeCatalogCodes.DiscountTypes.BonusDays)
                {
                    if (promo.AppliesToAllPaidPlans || planIds.Count == 0)
                    {
                        throw new InvalidOperationException("Промокод должен быть ограничен конкретными тарифами.");
                    }

                    var targetPlans = await context.SubscriptionPlans
                        .AsNoTracking()
                        .Where(plan => planIds.Contains(plan.Id))
                        .ToListAsync();

                    if (targetPlans.Any(plan => !plan.IsActive
                            || SubscriptionPlanCatalogCodes.NormalizePlanType(plan.PlanTypeCode) != SubscriptionPlanCatalogCodes.PlanTypes.Business
                            || plan.IsDefaultPlan
                            || plan.IsDemoPlan)
                        || targetPlans.Count != planIds.Count)
                    {
                        throw new InvalidOperationException("Тарифы промокода недоступны.");
                    }
                }

                var application = new PromoCodeApplication
                {
                    PromoCodeId = promo.Id,
                    OrganizationId = organizationId,
                    ApplyActorUserId = actorUserId,
                    BenefitTypeCode = benefitType,
                    DiscountValue = discountValue,
                    DiscountValidUntil = discountValidUntil,
                    BonusDays = bonusDays,
                    Status = PromoCodeApplicationStatuses.Assigned,
                    CreatedAt = now
                };

                context.PromoCodeApplications.Add(application);
                await context.SaveChangesAsync();
                attemptedApplicationId = application.Id;

                if (benefitType == PromoCodeCatalogCodes.DiscountTypes.BonusDays)
                {
                    var accessUntil = await organizationPromoAccessGrantPort.GrantAsync(
                        organizationId,
                        actorUserId,
                        promo.GiftTemplatePlanId!.Value,
                        promo.BonusDays!.Value,
                        application.Id);

                    application.AccessUntil = accessUntil;
                    application.Status = PromoCodeApplicationStatuses.Consumed;
                    application.ConsumedAt = DateTime.UtcNow;
                }

                var benefitDescription = benefitType == PromoCodeCatalogCodes.DiscountTypes.BonusDays
                    ? $"подарочных дней: {bonusDays}"
                    : $"скидка: {discountValue}";

                await organizationAuditService.AddAsync(new OrganizationAuditEventCreateDto
                {
                    OrganizationId = organizationId,
                    ActorUserId = actorUserId,
                    EventType = "billing.promo.applied",
                    Title = "Промокод применён",
                    Description = $"Промокод {promo.Code} применён к организации ({benefitDescription}).",
                    TargetType = "promo_code",
                    TargetId = promo.Id.ToString()
                }, saveChanges: false);

                await context.SaveChangesAsync();
                await transaction.CommitAsync();

                return MapApplication(application, promo);
            });
        }
        catch
        {
            context.ChangeTracker.Clear();
            throw;
        }
    }

    /// <summary> Возвращает список применённых промокодов организации. </summary>
    public async Task<IReadOnlyList<OrganizationPromoApplicationDto>> GetApplicationsAsync(string actorUserId, Guid organizationId)
    {
        await this.EnsureCanReadApplicationsAsync(actorUserId, organizationId);

        var applications = await context.PromoCodeApplications
            .AsNoTracking()
            .Include(application => application.PromoCode)
            .Where(application => application.OrganizationId == organizationId)
            .OrderByDescending(application => application.CreatedAt)
            .ToListAsync();

        return applications
            .Select(application => MapApplication(application, application.PromoCode))
            .ToList();
    }

    /// <summary> Возвращает ограниченную страницу истории промокодов. </summary>
    public async Task<PagedResult<OrganizationPromoApplicationDto>> GetApplicationsPageAsync(
        string actorUserId,
        Guid organizationId,
        int page,
        int pageSize)
    {
        await this.EnsureCanReadApplicationsAsync(actorUserId, organizationId);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = context.PromoCodeApplications.AsNoTracking()
            .Where(application => application.OrganizationId == organizationId);

        var totalCount = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(totalCount / (double)pageSize)));

        var applications = await query.Include(application => application.PromoCode)
            .OrderByDescending(application => application.CreatedAt)
            .ThenByDescending(application => application.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<OrganizationPromoApplicationDto>
        {
            Items = applications.Select(application => MapApplication(application, application.PromoCode)).ToList(),
            TotalCount = totalCount,
            AllCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    /// <summary> Проверяет доступ к истории промокодов выбранной организации. </summary>
    private async Task EnsureCanReadApplicationsAsync(string actorUserId, Guid organizationId)
    {
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            throw new InvalidOperationException("Не указан пользователь, выполняющий действие.");
        }

        var organizationExists = await context.Organizations
            .AsNoTracking()
            .AnyAsync(organization => organization.Id == organizationId
                && organization.IsActive
                && !organization.IsDeleted
                && !organization.IsArchived
                && organization.Members.Any(member => member.UserId == actorUserId));

        if (!organizationExists)
        {
            throw new InvalidOperationException("Организация не найдена или доступ закрыт.");
        }

        if (!await organizationPermissionService.HasPermissionAsync(actorUserId, organizationId, OrganizationPermissionCodes.BillingManage))
        {
            throw new UnauthorizedAccessException(OrganizationPermissionCatalog.BuildDeniedMessage(OrganizationPermissionCodes.BillingManage));
        }
    }

    /// <summary> Рассчитывает цену тарифа с учётом скидки по промокоду организации. </summary>
    public Task<PromotionPriceDto> CalculateDiscountAsync(Guid organizationId, int planId, decimal originalPrice)
    {
        return OrganizationPromoPricing.CalculateAsync(context, organizationId, planId, originalPrice);
    }

    /// <summary> Преобразует заявку промокода в DTO. </summary>
    private static OrganizationPromoApplicationDto MapApplication(PromoCodeApplication application, PromoCode promo) =>
        new()
        {
            Id = application.Id,
            PromoCodeId = promo.Id,
            Code = promo.Code,
            Name = promo.Name,
            BenefitTypeCode = application.BenefitTypeCode,
            BonusDays = application.BonusDays,
            DiscountValue = application.DiscountValue,
            DiscountValidUntil = application.DiscountValidUntil,
            Status = application.Status,
            AppliedAt = application.CreatedAt,
            ConsumedAt = application.ConsumedAt,
            OrganizationId = application.OrganizationId,
            AccessUntil = application.AccessUntil
        };
}
