using Helpers;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Features;
using OpenCRM.Services.Interfaces.Features.Dtos;

namespace OpenCRM.Modules.Billing.Features;

/// <summary> Сервис администрирования возможностей тарифов. </summary>
public class FeatureAdminService : IFeatureAdminService
{
    /// <summary> Контекст базы данных. </summary>
    private readonly OpenCRMContext _context;

    /// <summary> Создаёт сервис с контекстом базы данных. </summary>
    public FeatureAdminService(OpenCRMContext context)
    {
        _context = context;
    }

    /// <summary> Гарантирует наличие значений по умолчанию возможностей. </summary>
    public Task EnsureDefaultsAsync() => FeatureBootstrapper.EnsureDefaultsAsync(_context);

    /// <summary> Возвращает определения возможностей. </summary>
    public async Task<List<FeatureDefinitionDto>> GetDefinitionsAsync()
    {
        await EnsureDefaultsAsync();

        return await _context.FeatureDefinitions
            .Where(x => x.Code != "workspace.count.limit")
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .Select(x => MapDefinition(x))
            .ToListAsync();
    }

    /// <summary> Возвращает определение возможности по идентификатору. </summary>
    public async Task<FeatureDefinitionDto?> GetDefinitionByIdAsync(int id)
    {
        await EnsureDefaultsAsync();
        var definition = await _context.FeatureDefinitions
            .FirstOrDefaultAsync(x => x.Id == id && x.Code != "workspace.count.limit");
        return definition == null ? null : MapDefinition(definition);
    }

    /// <summary> Создаёт определение возможности. </summary>
    public async Task<FeatureDefinitionDto> CreateDefinitionAsync(FeatureDefinitionUpsertDto dto)
    {
        await EnsureDefaultsAsync();

        var code = dto.Code.Trim().ToLowerInvariant();
        if (await _context.FeatureDefinitions.AnyAsync(x => x.Code == code))
        {
            throw new InvalidOperationException("Фича с таким кодом уже существует.");
        }

        var definition = new FeatureDefinition
        {
            Code = code,
            Name = dto.Name.Trim(),
            Description = dto.Description?.Trim() ?? string.Empty,
            Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim(),
            ScopeCode = FeatureCatalogCodes.Scopes.Normalize(dto.ScopeCode),
            AudienceCode = FeatureCatalogCodes.Audiences.Normalize(dto.AudienceCode),
            ValueType = ParseValueType(dto.ValueType),
            IsActive = dto.IsActive
        };

        _context.FeatureDefinitions.Add(definition);
        await _context.SaveChangesAsync();

        return MapDefinition(definition);
    }

    /// <summary> Обновляет определение возможности. </summary>
    public async Task<FeatureDefinitionDto?> UpdateDefinitionAsync(int id, FeatureDefinitionUpsertDto dto)
    {
        await EnsureDefaultsAsync();

        var definition = await _context.FeatureDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (definition == null)
        {
            return null;
        }

        var code = dto.Code.Trim().ToLowerInvariant();
        if (await _context.FeatureDefinitions.AnyAsync(x => x.Code == code && x.Id != id))
        {
            throw new InvalidOperationException("Фича с таким кодом уже существует.");
        }

        definition.Code = code;
        definition.Name = dto.Name.Trim();
        definition.Description = dto.Description?.Trim() ?? string.Empty;
        definition.Category = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        definition.ScopeCode = FeatureCatalogCodes.Scopes.Normalize(dto.ScopeCode);
        definition.AudienceCode = FeatureCatalogCodes.Audiences.Normalize(dto.AudienceCode);
        definition.ValueType = ParseValueType(dto.ValueType);
        definition.IsActive = dto.IsActive;

        await _context.SaveChangesAsync();
        return MapDefinition(definition);
    }

    /// <summary> Переключает статус активности возможности. </summary>
    public async Task<bool> ToggleDefinitionStatusAsync(int id)
    {
        await EnsureDefaultsAsync();

        var definition = await _context.FeatureDefinitions.FirstOrDefaultAsync(x => x.Id == id);
        if (definition == null)
        {
            return false;
        }

        definition.IsActive = !definition.IsActive;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary> Возвращает возможности, привязанные к тарифу. </summary>
    public async Task<List<PlanFeatureAssignmentDto>> GetPlanFeaturesAsync(int planId)
    {
        await EnsureDefaultsAsync();

        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == planId);
        if (plan == null)
        {
            throw new InvalidOperationException("Тариф не найден.");
        }

        var familyPlans = await GetPlanFamilyAsync(plan);
        var featureSourcePlan = familyPlans
            .OrderBy(x => x.DurationDays)
            .ThenBy(x => x.Id)
            .First();

        var definitionsQuery = _context.FeatureDefinitions.AsQueryable();
        if (plan.PlanTypeCode == SubscriptionPlanCatalogCodes.PlanTypes.Personal)
        {
            definitionsQuery = definitionsQuery.Where(x =>
                x.IsActive &&
                x.Code != "workspace.count.limit" &&
                (x.ScopeCode == FeatureCatalogCodes.Scopes.Personal ||
                 x.ScopeCode == FeatureCatalogCodes.Scopes.Both));
        }
        else
        {
            definitionsQuery = definitionsQuery.Where(x =>
                x.IsActive &&
                x.Code != "workspace.count.limit" &&
                (x.ScopeCode == FeatureCatalogCodes.Scopes.Organization ||
                 x.ScopeCode == FeatureCatalogCodes.Scopes.Both));
        }

        var definitions = await definitionsQuery
            .OrderBy(x => x.Category)
            .ThenBy(x => x.Name)
            .ToListAsync();

        var assignments = await _context.SubscriptionPlanFeatures
            .Where(x => x.SubscriptionPlanId == featureSourcePlan.Id)
            .ToListAsync();

        return definitions.Select(definition =>
        {
            var assignment = assignments.FirstOrDefault(x => x.FeatureDefinitionId == definition.Id);
            return new PlanFeatureAssignmentDto
            {
                FeatureId = definition.Id,
                Code = definition.Code,
                Name = definition.Name,
                Description = definition.Description,
                Category = definition.Category,
                ScopeCode = definition.ScopeCode,
                ScopeName = FeatureCatalogCodes.Scopes.GetName(definition.ScopeCode),
                AudienceCode = definition.AudienceCode,
                AudienceName = FeatureCatalogCodes.Audiences.GetName(definition.AudienceCode),
                ValueType = definition.ValueType.ToString(),
                IsAssigned = assignment != null,
                BoolValue = assignment?.BoolValue,
                IntValue = assignment?.IntValue
            };
        }).ToList();
    }

    /// <summary> Сохраняет привязки возможностей тарифа. </summary>
    public async Task SavePlanFeaturesAsync(int planId, SavePlanFeaturesDto dto)
    {
        await EnsureDefaultsAsync();

        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(x => x.Id == planId)
            ?? throw new InvalidOperationException("Тариф не найден.");
        var familyPlans = await GetPlanFamilyAsync(plan);
        var familyPlanIds = familyPlans.Select(x => x.Id).ToHashSet();

        var definitions = await _context.FeatureDefinitions.ToListAsync();

        if (plan.Group == SubscriptionPlanCatalogCodes.OrganizationFreeGroup
            && dto.Features.Any(item => !item.IsAssigned && definitions.Any(definition =>
                definition.Id == item.FeatureId
                && definition.Code is FeatureCodes.WorkspaceClientsLimit or FeatureCodes.WorkspaceVehiclesLimit)))
        {
            throw new InvalidOperationException("Лимиты бесплатного тарифа нельзя отключить. Укажите нужное значение лимита.");
        }

        var currentAssignments = await _context.SubscriptionPlanFeatures
            .Where(x => familyPlanIds.Contains(x.SubscriptionPlanId))
            .ToListAsync();

        foreach (var item in dto.Features)
        {
            var definition = definitions.FirstOrDefault(x => x.Id == item.FeatureId)
                ?? throw new InvalidOperationException($"Функция с id={item.FeatureId} не найдена.");

            if (item.IsAssigned)
            {
                ValidateValue(definition, item.BoolValue, item.IntValue);
            }

            foreach (var familyPlan in familyPlans)
            {
                var currentAssignment = currentAssignments.FirstOrDefault(x =>
                    x.SubscriptionPlanId == familyPlan.Id &&
                    x.FeatureDefinitionId == item.FeatureId);

                if (!item.IsAssigned)
                {
                    if (currentAssignment != null)
                    {
                        _context.SubscriptionPlanFeatures.Remove(currentAssignment);
                    }

                    continue;
                }

                if (currentAssignment == null)
                {
                    currentAssignment = new SubscriptionPlanFeature
                    {
                        SubscriptionPlanId = familyPlan.Id,
                        FeatureDefinitionId = item.FeatureId
                    };
                    _context.SubscriptionPlanFeatures.Add(currentAssignment);
                    currentAssignments.Add(currentAssignment);
                }

                currentAssignment.BoolValue = definition.ValueType == FeatureValueType.Boolean ? item.BoolValue : null;
                currentAssignment.IntValue = definition.ValueType == FeatureValueType.Integer ? item.IntValue : null;

                if (definition.Code == FeatureCodes.VehiclesLimit || definition.Code == FeatureCodes.WorkspaceVehiclesLimit)
                {
                    familyPlan.VehicleLimit = item.IntValue ?? familyPlan.VehicleLimit;
                }
            }
        }

        await _context.SaveChangesAsync();
    }

    /// <summary> Возвращает семейство тарифов по имени плана. </summary>
    private async Task<List<SubscriptionPlan>> GetPlanFamilyAsync(SubscriptionPlan plan)
    {
        var candidates = await _context.SubscriptionPlans
            .Where(x =>
                x.PlanTypeCode == plan.PlanTypeCode &&
                x.Group == plan.Group &&
                x.IsDemoPlan == plan.IsDemoPlan &&
                x.IsDefaultPlan == plan.IsDefaultPlan)
            .ToListAsync();

        var familyName = NormalizePlanFamilyName(plan.Name);
        var family = candidates
            .Where(x => string.Equals(
                NormalizePlanFamilyName(x.Name),
                familyName,
                StringComparison.OrdinalIgnoreCase))
            .ToList();

        return family.Count > 0 ? family : [plan];
    }

    /// <summary> Нормализует имя семейства тарифов. </summary>
    private static string NormalizePlanFamilyName(string? name)
    {
        var value = (name ?? string.Empty).Trim();
        return System.Text.RegularExpressions.Regex.Replace(
            value,
            @"\s+\d+\s+(?:день|дня|дней)$",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
    }

    /// <summary> Возвращает персональные выдачи возможностей пользователя. </summary>
    public async Task<List<UserFeatureGrantDto>> GetUserFeatureGrantsAsync(string userId)
    {
        await EnsureDefaultsAsync();

        return await _context.UserFeatureGrants
            .Include(x => x.FeatureDefinition)
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new UserFeatureGrantDto
            {
                Id = x.Id,
                FeatureId = x.FeatureDefinitionId,
                FeatureCode = x.FeatureDefinition.Code,
                FeatureName = x.FeatureDefinition.Name,
                ValueType = x.FeatureDefinition.ValueType.ToString(),
                BoolValue = x.BoolValue,
                IntValue = x.IntValue,
                IsEnabled = x.IsEnabled,
                CreatedAt = x.CreatedAt,
                StartDate = x.StartDate,
                EndDate = x.EndDate,
                Source = x.Source,
                Comment = x.Comment,
                GrantedByUserId = x.GrantedByUserId
            })
            .ToListAsync();
    }

    /// <summary> Создаёт персональную выдачу возможности. </summary>
    public async Task<UserFeatureGrantDto> CreateUserFeatureGrantAsync(string userId, string grantedByUserId, CreateUserFeatureGrantDto dto)
    {
        await EnsureDefaultsAsync();

        var userExists = await _context.ApplicationUsers.AnyAsync(x => x.Id == userId);
        if (!userExists)
        {
            throw new InvalidOperationException("Пользователь не найден.");
        }

        var definition = await _context.FeatureDefinitions.FirstOrDefaultAsync(x => x.Id == dto.FeatureId)
            ?? throw new InvalidOperationException("Фича не найдена.");

        ValidateValue(definition, dto.BoolValue, dto.IntValue);

        var grant = new UserFeatureGrant
        {
            UserId = userId,
            FeatureDefinitionId = definition.Id,
            BoolValue = definition.ValueType == FeatureValueType.Boolean ? dto.BoolValue : null,
            IntValue = definition.ValueType == FeatureValueType.Integer ? dto.IntValue : null,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            StartDate = dto.StartDate,
            EndDate = dto.EndDate,
            Source = dto.Source.Trim().ToLowerInvariant(),
            Comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim(),
            GrantedByUserId = grantedByUserId
        };

        _context.UserFeatureGrants.Add(grant);
        await _context.SaveChangesAsync();

        return new UserFeatureGrantDto
        {
            Id = grant.Id,
            FeatureId = definition.Id,
            FeatureCode = definition.Code,
            FeatureName = definition.Name,
            ValueType = definition.ValueType.ToString(),
            BoolValue = grant.BoolValue,
            IntValue = grant.IntValue,
            IsEnabled = grant.IsEnabled,
            CreatedAt = grant.CreatedAt,
            StartDate = grant.StartDate,
            EndDate = grant.EndDate,
            Source = grant.Source,
            Comment = grant.Comment,
            GrantedByUserId = grant.GrantedByUserId
        };
    }

    /// <summary> Отзывает персональную выдачу возможности. </summary>
    public async Task<bool> RevokeUserFeatureGrantAsync(int grantId)
    {
        await EnsureDefaultsAsync();

        var grant = await _context.UserFeatureGrants.FirstOrDefaultAsync(x => x.Id == grantId);
        if (grant == null)
        {
            return false;
        }

        grant.IsEnabled = false;
        grant.EndDate ??= DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary> Преобразует определение возможности в DTO. </summary>
    private static FeatureDefinitionDto MapDefinition(FeatureDefinition definition)
    {
        return new FeatureDefinitionDto
        {
            Id = definition.Id,
            Code = definition.Code,
            Name = definition.Name,
            Description = definition.Description,
            Category = definition.Category,
            ScopeCode = definition.ScopeCode,
            ScopeName = FeatureCatalogCodes.Scopes.GetName(definition.ScopeCode),
            AudienceCode = definition.AudienceCode,
            AudienceName = FeatureCatalogCodes.Audiences.GetName(definition.AudienceCode),
            ValueType = definition.ValueType.ToString(),
            IsActive = definition.IsActive,
            IsSystem = definition.IsSystem
        };
    }

    /// <summary> Разбирает тип значения возможности. </summary>
    private static FeatureValueType ParseValueType(string valueType)
    {
        if (Enum.TryParse<FeatureValueType>(valueType, true, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException("Некорректный тип значения фичи.");
    }

    /// <summary> Проверяет соответствие значения типу возможности. </summary>
    private static void ValidateValue(FeatureDefinition definition, bool? boolValue, int? intValue)
    {
        if (definition.ValueType == FeatureValueType.Boolean && !boolValue.HasValue)
        {
            throw new InvalidOperationException($"Для булевой фичи '{definition.Name}' нужно указать true/false.");
        }

        if (definition.ValueType == FeatureValueType.Integer && !intValue.HasValue)
        {
            throw new InvalidOperationException($"Для числовой фичи '{definition.Name}' нужно указать число.");
        }
    }
}
