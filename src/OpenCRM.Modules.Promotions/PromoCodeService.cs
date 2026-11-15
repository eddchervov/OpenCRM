using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Promotions;
using OpenCRM.Services.Interfaces.Promotions.Dtos;

namespace OpenCRM.Modules.Promotions;

/// <summary> Сервис управления и применения промокодов. </summary>
public class PromoCodeService(OpenCRMContext context) : IPromoCodeService
{
    /// <summary> Контекст базы данных для работы с промокодами. </summary>
    private readonly OpenCRMContext _context = context;

    /// <summary> Рассчитывает цену тарифа организации по закреплённым за ней промокодам. </summary>
    public Task<PromotionPriceDto> CalculatePriceAsync(
        string userId,
        Guid organizationId,
        int planId,
        decimal originalPrice)
    {
        return OrganizationPromoPricing.CalculateAsync(this._context, organizationId, planId, originalPrice);
    }

    /// <summary> Возвращает список всех промокодов для администрирования. </summary>
    public async Task<IReadOnlyList<PromoCodeAdminDto>> GetAllAsync()
    {
        var promoCodes = await this._context.PromoCodes
            .AsNoTracking()
            .Include(item => item.Plans)
                .ThenInclude(item => item.SubscriptionPlan)
            .Include(item => item.Redemptions)
            .Include(item => item.Scenarios)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync();

        // Подсчёт применений по организациям батчами, без запросов на каждый промокод.
        var promoCodeIds = promoCodes.Select(item => item.Id).ToList();

        var applicationCounts = await this._context.PromoCodeApplications
            .AsNoTracking()
            .Where(item => promoCodeIds.Contains(item.PromoCodeId))
            .GroupBy(item => item.PromoCodeId)
            .Select(group => new { PromoCodeId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.PromoCodeId, item => item.Count);

        return promoCodes
            .Select(promoCode => MapAdminDto(
                promoCode,
                applicationCounts.GetValueOrDefault(promoCode.Id)))
            .ToList();
    }

    /// <summary> Возвращает промокод по идентификатору для администрирования. </summary>
    public async Task<PromoCodeAdminDto?> GetByIdAsync(int id)
    {
        var promoCode = await this._context.PromoCodes
            .AsNoTracking()
            .Include(item => item.Plans)
                .ThenInclude(item => item.SubscriptionPlan)
            .Include(item => item.Redemptions)
            .Include(item => item.Scenarios)
            .Include(item => item.Applications)
            .FirstOrDefaultAsync(item => item.Id == id);

        if (promoCode == null)
        {
            return null;
        }

        var organizationApplicationCount = promoCode.Applications.Count;

        return MapAdminDto(promoCode, organizationApplicationCount);
    }

    /// <summary> Создаёт промокод по данным формы. </summary>
    public async Task<PromoCodeAdminDto> CreateAsync(PromoCodeSaveDto dto)
    {
        // Все новые промокоды создаются как организационные; legacy-канал регистрации не пополняется.
        ValidateDto(dto, isOrganizationCode: true);
        var normalizedCode = NormalizeCode(dto.Code);
        var codeExists = await this._context.PromoCodes.AnyAsync(item => item.Code == normalizedCode);
        if (codeExists)
        {
            throw new InvalidOperationException("Промокод с таким кодом уже существует.");
        }

        await ValidatePlansAsync(dto, isOrganizationCode: true);
        ValidateScenarios(dto);
        var now = DateTime.UtcNow;
        var promoCode = new PromoCode
        {
            Code = normalizedCode,
            IsOrganizationCode = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyDto(promoCode, dto, isOrganizationCode: true);
        ApplyPlans(promoCode, dto);
        ApplyScenarios(promoCode, dto);
        this._context.PromoCodes.Add(promoCode);
        await this._context.SaveChangesAsync();

        return (await GetByIdAsync(promoCode.Id))!;
    }

    /// <summary> Обновляет промокод по данным формы. </summary>
    public async Task<PromoCodeAdminDto?> UpdateAsync(int id, PromoCodeSaveDto dto)
    {
        // Обновление выполняется в собственной транзакции: внешняя транзакция или
        // уже отслеживаемые изменения контекста сделали бы её неконтролируемой.
        if (this._context.Database.CurrentTransaction != null || System.Transactions.Transaction.Current != null)
        {
            throw new InvalidOperationException("Обновление промокода выполняется в собственной транзакции.");
        }

        if (this._context.ChangeTracker.HasChanges())
        {
            throw new InvalidOperationException("Обновление промокода доступно только с чистым контекстом изменений.");
        }

        return await this.UpdateCoreAsync(id, dto);
    }

    /// <summary> Выполняет обновление промокода и возвращает DTO. </summary>
    private async Task<PromoCodeAdminDto?> UpdateCoreAsync(int id, PromoCodeSaveDto dto)
    {
        var strategy = this._context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            // Каждая попытка начинается с чистого трекера; ошибки откатывают транзакцию.
            this._context.ChangeTracker.Clear();

            await using var transaction = await this._context.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            try
            {
                // Блокировка строки промокода сериализует одновременные Apply и Update.
                var locked = await this._context.PromoCodes
                    .FromSqlInterpolated($"""SELECT * FROM "PromoCodes" WHERE "Id" = {id} FOR UPDATE""")
                    .AsNoTracking()
                    .SingleOrDefaultAsync();
                if (locked == null)
                {
                    await transaction.CommitAsync();

                    return null;
                }

                var promoCode = await this._context.PromoCodes
                    .Include(item => item.Plans)
                    .Include(item => item.Redemptions)
                    .Include(item => item.Scenarios)
                    .Include(item => item.Applications)
                    .SingleAsync(item => item.Id == id);

                // Маркер канала не меняется: обычное редактирование не конвертирует промокод.
                ValidateDto(dto, promoCode.IsOrganizationCode);

                var normalizedCode = NormalizeCode(dto.Code);
                var codeExists = await this._context.PromoCodes
                    .AnyAsync(item => item.Id != id && item.Code == normalizedCode);
                if (codeExists)
                {
                    throw new InvalidOperationException("Промокод с таким кодом уже существует.");
                }

                if ((promoCode.Redemptions.Count > 0 || promoCode.Applications.Count > 0)
                    && !string.Equals(promoCode.Code, normalizedCode, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Код уже использован. Его можно отключить, но нельзя переименовать.");
                }

                // Свежая проверка применений под блокировкой строки защищает закреплённые выгоды.
                if (promoCode.IsOrganizationCode
                    && await this._context.PromoCodeApplications.AnyAsync(item => item.PromoCodeId == id))
                {
                    ValidateImmutableScope(promoCode, dto);
                }

                await ValidatePlansAsync(dto, promoCode.IsOrganizationCode);
                ValidateScenarios(dto);
                promoCode.Code = normalizedCode;
                ApplyDto(promoCode, dto, promoCode.IsOrganizationCode);
                ApplyPlans(promoCode, dto);
                ApplyScenarios(promoCode, dto);
                promoCode.UpdatedAt = DateTime.UtcNow;

                await this._context.SaveChangesAsync();
                var result = await this.GetByIdAsync(id);
                await transaction.CommitAsync();

                return result;
            }
            catch
            {
                this._context.ChangeTracker.Clear();
                throw;
            }
        });
    }

    /// <summary> Переключает активность промокода. </summary>
    public async Task<bool> SetActiveAsync(int id, bool isActive)
    {
        var promoCode = await this._context.PromoCodes.FirstOrDefaultAsync(item => item.Id == id);
        if (promoCode == null)
        {
            return false;
        }

        promoCode.IsActive = isActive;
        promoCode.UpdatedAt = DateTime.UtcNow;
        await this._context.SaveChangesAsync();

        return true;
    }

    /// <summary> Удаляет промокод. </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var promoCode = await this._context.PromoCodes
            .Include(item => item.Redemptions)
            .Include(item => item.Applications)
            .FirstOrDefaultAsync(item => item.Id == id);
        if (promoCode == null)
        {
            return false;
        }

        if (promoCode.Redemptions.Count > 0 || promoCode.Applications.Count > 0)
        {
            throw new InvalidOperationException("Использованный промокод нельзя удалить. Его можно отключить.");
        }

        // Исторические платежи и резервирования также запрещают удаление.
        var hasRelatedPayments = await this._context.SubscriptionPayments
            .AnyAsync(payment => payment.PromoCodeId == id);
        if (hasRelatedPayments)
        {
            throw new InvalidOperationException("Использованный промокод нельзя удалить. Его можно отключить.");
        }

        var hasRelatedOperations = await this._context.PaymentCreationOperations
            .AnyAsync(operation => operation.PromoCodeId == id);
        if (hasRelatedOperations)
        {
            throw new InvalidOperationException("Использованный промокод нельзя удалить. Его можно отключить.");
        }

        this._context.PromoCodes.Remove(promoCode);
        await this._context.SaveChangesAsync();

        return true;
    }

    /// <summary> Возвращает список применений промокода. </summary>
    public async Task<IReadOnlyList<PromoCodeUsageDto>> GetUsagesAsync(int id)
    {
        // Применения промокода организациями, независимо от оплаты и статуса.
        var applications = await this._context.PromoCodeApplications
            .AsNoTracking()
            .Include(item => item.Organization)
            .Where(item => item.PromoCodeId == id)
            .ToListAsync();

        var actorUserIds = applications
            .Select(item => item.ApplyActorUserId)
            .Where(userId => userId.Length > 0)
            .Distinct()
            .ToList();

        var actorEmails = await this._context.ApplicationUsers
            .AsNoTracking()
            .Where(user => actorUserIds.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Email ?? string.Empty);

        var applicationUsages = applications
            .Select(item => new PromoCodeUsageDto
            {
                Id = item.Id,
                UserId = item.ApplyActorUserId,
                Email = actorEmails.GetValueOrDefault(item.ApplyActorUserId) ?? string.Empty,
                OrganizationId = item.OrganizationId,
                OrganizationName = item.Organization?.Name,
                RedeemedAt = item.CreatedAt,
                ApplicationStatus = item.Status,
                DiscountValidUntil = item.DiscountValidUntil,
                SubscriptionPaymentId = item.SubscriptionPaymentId,
                SourceCode = "organization"
            })
            .ToList();

        // Legacy регистрации.
        var registrationUsages = await this._context.PromoCodeRedemptions
            .AsNoTracking()
            .Where(item => item.PromoCodeId == id)
            .Select(item => new PromoCodeUsageDto
            {
                Id = item.Id,
                UserId = item.UserId,
                Email = this._context.ApplicationUsers
                    .Where(user => user.Id == item.UserId)
                    .Select(user => user.Email ?? string.Empty)
                    .FirstOrDefault() ?? string.Empty,
                OrganizationId = item.OrganizationId,
                OrganizationName = item.Organization == null ? null : item.Organization.Name,
                RedeemedAt = item.RedeemedAt,
                AssignedAt = item.AssignedAt,
                DemoAppliedAt = item.DemoAppliedAt,
                SourceCode = "registration"
            })
            .ToListAsync();

        return registrationUsages
            .Concat(applicationUsages)
            .OrderByDescending(item => item.RedeemedAt)
            .ToList();
    }

    /// <summary> Нормализует код промокода. </summary>
    private static string NormalizeCode(string code)
    {
        var normalizedCode = code.Trim().ToUpperInvariant();
        if (normalizedCode.Length is < 3 or > 64
            || normalizedCode.Any(character => !char.IsLetterOrDigit(character) && character is not '-' and not '_'))
        {
            throw new InvalidOperationException("Промокод должен содержать от 3 до 64 букв, цифр, дефисов или подчёркиваний.");
        }

        return normalizedCode;
    }

    /// <summary> Нормализует коды сценариев из формы промокода. </summary>
    private static List<string> NormalizeScenarioCodes(PromoCodeSaveDto dto)
    {
        return dto.ScenarioCodes
            .Select(code => code.Trim().ToLowerInvariant())
            .Where(code => code.Length > 0)
            .Distinct()
            .ToList();
    }

    /// <summary> Проверяет корректность данных формы промокода. </summary>
    private static void ValidateDto(PromoCodeSaveDto dto, bool isOrganizationCode)
    {
        _ = NormalizeCode(dto.Code);

        if (string.IsNullOrWhiteSpace(dto.Name))
        {
            throw new InvalidOperationException("Название промокода обязательно.");
        }

        if (!PromoCodeCatalogCodes.DiscountTypes.All.Contains(dto.DiscountTypeCode))
        {
            throw new InvalidOperationException("Неизвестный тип скидки.");
        }

        if (dto.ValidFrom.HasValue && dto.ValidUntil.HasValue && dto.ValidFrom.Value >= dto.ValidUntil.Value)
        {
            throw new InvalidOperationException("Дата окончания должна быть позже даты начала.");
        }

        if (isOrganizationCode)
        {
            ValidateOrganizationDto(dto);
            return;
        }

        if (dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.BonusDays)
        {
            if (!dto.BonusDays.HasValue || dto.BonusDays.Value is < 1 or > 3650)
            {
                throw new InvalidOperationException("Количество бонусных дней должно быть от 1 до 3650.");
            }

            return;
        }

        if (dto.BonusDays.HasValue)
        {
            throw new InvalidOperationException("Бонусные дни указываются только для типа bonus_days.");
        }

        if (dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.None)
        {
            return;
        }

        if (!dto.DiscountValue.HasValue)
        {
            throw new InvalidOperationException("Укажите размер скидки или итоговую цену.");
        }

        if (dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.Percentage
            && dto.DiscountValue.Value > 99)
        {
            throw new InvalidOperationException("Процент скидки не должен превышать 99.");
        }
    }

    /// <summary> Проверяет корректность данных организационного промокода. </summary>
    private static void ValidateOrganizationDto(PromoCodeSaveDto dto)
    {
        if (dto.DiscountValidityDays is < 1 or > 3650)
        {
            throw new InvalidOperationException("Срок использования скидки должен быть от 1 до 3650 дней либо без ограничения.");
        }

        // Организационный промокод несёт ровно один тип выгоды; демо и лимит оплат недоступны.
        if (dto.DiscountTypeCode is not (PromoCodeCatalogCodes.DiscountTypes.BonusDays
            or PromoCodeCatalogCodes.DiscountTypes.Percentage
            or PromoCodeCatalogCodes.DiscountTypes.FixedAmount))
        {
            throw new InvalidOperationException("Для организационного промокода доступны только типы bonus_days, percentage и fixed_amount.");
        }

        if (dto.DemoDurationDays.HasValue)
        {
            throw new InvalidOperationException("Демо-период недоступен для организационного промокода.");
        }

        if (dto.DiscountPaymentLimitPerUser.HasValue)
        {
            throw new InvalidOperationException("Лимит оплачиваемых со скидкой платежей недоступен для организационного промокода.");
        }

        if (dto.MaximumOrganizationApplications.HasValue && dto.MaximumOrganizationApplications.Value < 1)
        {
            throw new InvalidOperationException("Общий лимит применений промокода должен быть больше нуля.");
        }

        if (dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.BonusDays)
        {
            if (dto.DiscountValidityDays.HasValue)
            {
                throw new InvalidOperationException("Срок использования скидки не задаётся для подарочных дней.");
            }

            if (!dto.BonusDays.HasValue || dto.BonusDays.Value is < 1 or > 3650)
            {
                throw new InvalidOperationException("Количество бонусных дней должно быть от 1 до 3650.");
            }

            if (dto.DiscountValue.HasValue)
            {
                throw new InvalidOperationException("Для бонусных дней размер скидки не указывается.");
            }

            if (!dto.GiftTemplatePlanId.HasValue || dto.GiftTemplatePlanId.Value < 1)
            {
                throw new InvalidOperationException("Некорректный тариф-шаблон для подарочного доступа.");
            }

            return;
        }

        if (dto.BonusDays.HasValue)
        {
            throw new InvalidOperationException("Бонусные дни указываются только для типа bonus_days.");
        }

        if (dto.GiftTemplatePlanId.HasValue)
        {
            throw new InvalidOperationException("Тариф-шаблон подарка указывается только для бонусных дней.");
        }

        if (!dto.DiscountValue.HasValue || dto.DiscountValue.Value <= 0)
        {
            throw new InvalidOperationException("Укажите положительный размер скидки.");
        }

        if (dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.Percentage
            && dto.DiscountValue.Value > 99)
        {
            throw new InvalidOperationException("Процент скидки не должен превышать 99.");
        }
    }

    /// <summary> Проверяет корректность сценариев промокода. </summary>
    private static void ValidateScenarios(PromoCodeSaveDto dto)
    {
        if (dto.AppliesToAllScenarios)
        {
            return;
        }

        var scenarioCodes = NormalizeScenarioCodes(dto);
        if (scenarioCodes.Count == 0)
        {
            throw new InvalidOperationException("Выберите хотя бы одно направление.");
        }

        var allowedScenarios = new HashSet<string>
        {
            OrganizationTypeCodes.ServiceStation,
            OrganizationTypeCodes.Detailing,
            OrganizationTypeCodes.Fleet
        };

        if (scenarioCodes.Any(code => !allowedScenarios.Contains(code)))
        {
            throw new InvalidOperationException("Одно или несколько выбранных направлений недоступны для промокода.");
        }
    }

    /// <summary> Проверяет корректность тарифов промокода. </summary>
    private async Task ValidatePlansAsync(PromoCodeSaveDto dto, bool isOrganizationCode)
    {
        // Подарочный шаблон: ровно один активный бизнес-тариф без демо и дефолта;
        // выбранные тарифы скидки для подарка игнорируются.
        if (isOrganizationCode && dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.BonusDays)
        {
            var giftPlanCount = await this._context.SubscriptionPlans
                .CountAsync(plan => plan.Id == dto.GiftTemplatePlanId
                    && plan.IsActive
                    && plan.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                    && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business
                    && !plan.IsDefaultPlan
                    && !plan.IsDemoPlan);

            if (giftPlanCount != 1)
            {
                throw new InvalidOperationException("Тариф-шаблон подарка недоступен для промокода.");
            }

            return;
        }

        // Денежные скидки применяются только к явно выбранным бизнес-тарифам.
        var isMonetaryDiscount = dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.Percentage
            || dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.FixedAmount;
        if (isOrganizationCode && isMonetaryDiscount && dto.AppliesToAllPaidPlans)
        {
            throw new InvalidOperationException("Для процентной и фиксированной скидки выберите конкретные тарифы.");
        }

        if (dto.AppliesToAllPaidPlans)
        {
            return;
        }

        var planIds = dto.SubscriptionPlanIds.Distinct().ToList();
        if (planIds.Count == 0)
        {
            throw new InvalidOperationException("Выберите хотя бы один тариф.");
        }

        var validPlanCount = await this._context.SubscriptionPlans
            .CountAsync(plan => planIds.Contains(plan.Id)
                && plan.IsActive
                && plan.Group != SubscriptionPlanCatalogCodes.OrganizationFreeGroup
                && plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Business
                && !plan.IsDefaultPlan
                && !plan.IsDemoPlan);

        if (validPlanCount != planIds.Count)
        {
            throw new InvalidOperationException("Один или несколько выбранных тарифов недоступны для промокода.");
        }
    }

    /// <summary> Применяет данные формы к сущности промокода. </summary>
    private static void ApplyDto(PromoCode promoCode, PromoCodeSaveDto dto, bool isOrganizationCode)
    {
        var isBonusDays = dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.BonusDays;

        promoCode.Name = dto.Name.Trim();
        promoCode.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        promoCode.IsActive = dto.IsActive;
        promoCode.ValidFrom = dto.ValidFrom;
        promoCode.ValidUntil = dto.ValidUntil;
        promoCode.MaximumRedemptions = dto.MaximumRedemptions;
        // Для организационных промокодов демо-период недоступен; legacy-записи сохраняют прежнее поведение.
        promoCode.DemoDurationDays = isOrganizationCode ? null : dto.DemoDurationDays;
        promoCode.DiscountTypeCode = dto.DiscountTypeCode;
        promoCode.DiscountValue = isBonusDays || dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.None
            ? null
            : dto.DiscountValue;
        promoCode.DiscountValidityDays = isOrganizationCode && !isBonusDays ? dto.DiscountValidityDays : null;
        promoCode.DiscountPaymentLimitPerUser = isBonusDays || dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.None
            ? null
            : dto.DiscountPaymentLimitPerUser;
        promoCode.BonusDays = isBonusDays ? dto.BonusDays : null;
        promoCode.AppliesToAllPaidPlans = dto.AppliesToAllPaidPlans;
        promoCode.MaximumOrganizationApplications = dto.MaximumOrganizationApplications;
        promoCode.AppliesToAllScenarios = dto.AppliesToAllScenarios;
        // Шаблон подарка заполняется только для подарочного промокода, иначе сбрасывается.
        promoCode.GiftTemplatePlanId = isBonusDays ? dto.GiftTemplatePlanId : null;
    }

    /// <summary> Применяет список тарифов к промокоду. </summary>
    private static void ApplyPlans(PromoCode promoCode, PromoCodeSaveDto dto)
    {
        var targetPlanIds = dto.AppliesToAllPaidPlans || dto.DiscountTypeCode == PromoCodeCatalogCodes.DiscountTypes.BonusDays
            ? new HashSet<int>()
            : dto.SubscriptionPlanIds.ToHashSet();

        promoCode.Plans.RemoveAll(plan => !targetPlanIds.Contains(plan.SubscriptionPlanId));
        var existingPlanIds = promoCode.Plans
            .Select(plan => plan.SubscriptionPlanId)
            .ToHashSet();
        promoCode.Plans.AddRange(targetPlanIds
            .Where(planId => !existingPlanIds.Contains(planId))
            .Select(planId => new PromoCodePlan
            {
                SubscriptionPlanId = planId
            }));
    }

    /// <summary> Применяет сценарии к промокоду. </summary>
    private static void ApplyScenarios(PromoCode promoCode, PromoCodeSaveDto dto)
    {
        var targetScenarioCodes = dto.AppliesToAllScenarios
            ? new HashSet<string>()
            : NormalizeScenarioCodes(dto).ToHashSet();

        promoCode.Scenarios.RemoveAll(scenario => !targetScenarioCodes.Contains(scenario.ScenarioCode));
        var existingScenarioCodes = promoCode.Scenarios
            .Select(scenario => scenario.ScenarioCode)
            .ToHashSet();
        promoCode.Scenarios.AddRange(targetScenarioCodes
            .Where(scenarioCode => !existingScenarioCodes.Contains(scenarioCode))
            .Select(scenarioCode => new PromoCodeScenario
            {
                PromoCodeId = promoCode.Id,
                ScenarioCode = scenarioCode
            }));
    }

    /// <summary>
    /// Проверка неизменности охвата промокода, уже применённого организациями.
    /// </summary>
    private static void ValidateImmutableScope(PromoCode promoCode, PromoCodeSaveDto dto)
    {
        var hasScopeChanged = promoCode.DiscountTypeCode != dto.DiscountTypeCode
            || promoCode.GiftTemplatePlanId != dto.GiftTemplatePlanId
            || promoCode.AppliesToAllScenarios != dto.AppliesToAllScenarios;

        if (!hasScopeChanged && !promoCode.AppliesToAllScenarios)
        {
            hasScopeChanged = !NormalizeScenarioCodes(dto).ToHashSet()
                .SetEquals(promoCode.Scenarios.Select(item => item.ScenarioCode));
        }

        if (!hasScopeChanged && promoCode.AppliesToAllPaidPlans != dto.AppliesToAllPaidPlans)
        {
            hasScopeChanged = true;
        }

        if (!hasScopeChanged && !dto.AppliesToAllPaidPlans
            && dto.DiscountTypeCode != PromoCodeCatalogCodes.DiscountTypes.BonusDays)
        {
            hasScopeChanged = !dto.SubscriptionPlanIds.ToHashSet()
                .SetEquals(promoCode.Plans.Select(item => item.SubscriptionPlanId));
        }

        if (hasScopeChanged)
        {
            throw new InvalidOperationException("После применения нельзя менять тип бонуса, направления и тарифы промокода. Создайте новый промокод.");
        }
    }

    /// <summary> Преобразует промокод в DTO администрирования. </summary>
    private static PromoCodeAdminDto MapAdminDto(PromoCode promoCode, int organizationApplicationCount)
    {
        var redemptionCount = promoCode.Redemptions.Count;

        return new PromoCodeAdminDto
        {
            Id = promoCode.Id,
            Code = promoCode.Code,
            Name = promoCode.Name,
            Description = promoCode.Description,
            IsActive = promoCode.IsActive,
            ValidFrom = promoCode.ValidFrom,
            ValidUntil = promoCode.ValidUntil,
            MaximumRedemptions = promoCode.MaximumRedemptions,
            RedemptionCount = redemptionCount,
            RemainingRedemptions = promoCode.MaximumRedemptions.HasValue
                ? Math.Max(0, promoCode.MaximumRedemptions.Value - redemptionCount)
                : null,
            DemoDurationDays = promoCode.DemoDurationDays,
            DiscountTypeCode = promoCode.DiscountTypeCode,
            DiscountValue = promoCode.DiscountValue,
            DiscountValidityDays = promoCode.DiscountValidityDays,
            DiscountPaymentLimitPerUser = promoCode.DiscountPaymentLimitPerUser,
            AppliesToAllPaidPlans = promoCode.AppliesToAllPaidPlans,
            SubscriptionPlanIds = promoCode.Plans.Select(item => item.SubscriptionPlanId).ToList(),
            SubscriptionPlanNames = promoCode.Plans.Select(item => item.SubscriptionPlan.Name).ToList(),
            BonusDays = promoCode.BonusDays,
            MaximumOrganizationApplications = promoCode.MaximumOrganizationApplications,
            OrganizationApplicationCount = organizationApplicationCount,
            RemainingOrganizationApplications = promoCode.MaximumOrganizationApplications.HasValue
                ? Math.Max(0, promoCode.MaximumOrganizationApplications.Value - organizationApplicationCount)
                : null,
            AppliesToAllScenarios = promoCode.AppliesToAllScenarios,
            ScenarioCodes = promoCode.Scenarios.Select(item => item.ScenarioCode).ToList(),
            GiftTemplatePlanId = promoCode.GiftTemplatePlanId,
            IsOrganizationCode = promoCode.IsOrganizationCode,
            CreatedAt = promoCode.CreatedAt,
            UpdatedAt = promoCode.UpdatedAt
        };
    }
}
