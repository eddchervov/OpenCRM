using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Workspace.Queries;

/// <summary>
/// Правила автопланирования организации, рассчитанные из типа организации и активных элементов scenario-catalog.
/// </summary>
internal sealed record OrganizationPlanningPolicy(
    bool EnableDatePlanning,
    bool EnableMileagePlanning,
    bool EnableDefectPlanning,
    bool EnableCounterFreshness,
    bool EnableFirstService,
    int PlannedServiceIntervalDays,
    int SoonServiceWindowDays,
    int CounterStaleWindowDays,
    int FirstServiceGraceDays,
    int MileageIntervalKilometers,
    int MileageSoonKilometers,
    int MileageIntervalEngineHours,
    int MileageSoonEngineHours)
{
    /// <summary> Возвращает интервал по пробегу в единицах измерения техники. </summary>
    public int GetMileageInterval(VehicleMileageUnit mileageUnit) =>
        mileageUnit == VehicleMileageUnit.EngineHours ? MileageIntervalEngineHours : MileageIntervalKilometers;

    /// <summary> Возвращает порог скорого наступления по пробегу. </summary>
    public int GetMileageSoonThreshold(VehicleMileageUnit mileageUnit) =>
        mileageUnit == VehicleMileageUnit.EngineHours ? MileageSoonEngineHours : MileageSoonKilometers;
}

/// <summary>
/// Снимок правила планирования из scenario-catalog.
/// </summary>
internal sealed record OrganizationPlanningRuleSnapshot(string Code, string Name, string? Description, bool IsActive);

/// <summary>
/// Собирает поведение автопланирования из системного сценария и активных правил организации.
/// </summary>
internal static class OrganizationPlanningPolicyFactory
{
    /// <summary> Собирает политику планирования из сценария и активных правил организации. </summary>
    public static OrganizationPlanningPolicy Build(
        OrganizationScenarioProcessDefinition process,
        IReadOnlyList<OrganizationPlanningRuleSnapshot> planningRules)
    {
        var basePolicy = BuildScenarioDefaults(process.Code);
        if (planningRules.Count == 0)
        {
            return basePolicy;
        }

        var activeRules = planningRules.Where(x => x.IsActive).ToList();
        var dateRules = activeRules.Where(IsDateRule).ToList();
        var mileageRules = activeRules.Where(IsMileageRule).ToList();
        var counterRules = activeRules.Where(IsCounterRule).ToList();
        var firstServiceRules = activeRules.Where(IsFirstServiceRule).ToList();

        return basePolicy with
        {
            EnableDatePlanning = dateRules.Count > 0,
            EnableMileagePlanning = mileageRules.Count > 0,
            EnableDefectPlanning = activeRules.Any(IsDefectRule),
            EnableCounterFreshness = counterRules.Count > 0 || (basePolicy.EnableCounterFreshness && mileageRules.Count > 0),
            EnableFirstService = firstServiceRules.Count > 0 || basePolicy.EnableFirstService,
            PlannedServiceIntervalDays = ResolveDays(dateRules, basePolicy.PlannedServiceIntervalDays),
            SoonServiceWindowDays = ResolveSoonDays(dateRules, basePolicy.SoonServiceWindowDays),
            CounterStaleWindowDays = ResolveDays(counterRules, basePolicy.CounterStaleWindowDays),
            FirstServiceGraceDays = ResolveDays(firstServiceRules, basePolicy.FirstServiceGraceDays),
            MileageIntervalKilometers = ResolveMileage(mileageRules, basePolicy.MileageIntervalKilometers),
            MileageIntervalEngineHours = ResolveEngineHours(mileageRules, basePolicy.MileageIntervalEngineHours)
        };
    }

    /// <summary> Возвращает настройки планирования по коду сценария. </summary>
    private static OrganizationPlanningPolicy BuildScenarioDefaults(string scenarioCode)
    {
        var normalized = OrganizationScenarioProcessCatalog.NormalizeScenarioCode(scenarioCode);
        return normalized switch
        {
            OrganizationTypeCodes.Detailing => new(
                EnableDatePlanning: true,
                EnableMileagePlanning: false,
                EnableDefectPlanning: true,
                EnableCounterFreshness: false,
                EnableFirstService: true,
                PlannedServiceIntervalDays: 90,
                SoonServiceWindowDays: 14,
                CounterStaleWindowDays: 60,
                FirstServiceGraceDays: 3,
                MileageIntervalKilometers: 10000,
                MileageSoonKilometers: 1500,
                MileageIntervalEngineHours: 250,
                MileageSoonEngineHours: 40),
            OrganizationTypeCodes.Wrapping => new(
                EnableDatePlanning: true,
                EnableMileagePlanning: false,
                EnableDefectPlanning: true,
                EnableCounterFreshness: false,
                EnableFirstService: true,
                PlannedServiceIntervalDays: 30,
                SoonServiceWindowDays: 10,
                CounterStaleWindowDays: 60,
                FirstServiceGraceDays: 3,
                MileageIntervalKilometers: 10000,
                MileageSoonKilometers: 1500,
                MileageIntervalEngineHours: 250,
                MileageSoonEngineHours: 40),
            OrganizationTypeCodes.CarWash => new(
                EnableDatePlanning: true,
                EnableMileagePlanning: false,
                EnableDefectPlanning: false,
                EnableCounterFreshness: false,
                EnableFirstService: false,
                PlannedServiceIntervalDays: 30,
                SoonServiceWindowDays: 7,
                CounterStaleWindowDays: 60,
                FirstServiceGraceDays: 0,
                MileageIntervalKilometers: 10000,
                MileageSoonKilometers: 1500,
                MileageIntervalEngineHours: 250,
                MileageSoonEngineHours: 40),
            OrganizationTypeCodes.Fleet => new(
                EnableDatePlanning: true,
                EnableMileagePlanning: true,
                EnableDefectPlanning: true,
                EnableCounterFreshness: true,
                EnableFirstService: true,
                PlannedServiceIntervalDays: 90,
                SoonServiceWindowDays: 21,
                CounterStaleWindowDays: 14,
                FirstServiceGraceDays: 7,
                MileageIntervalKilometers: 8000,
                MileageSoonKilometers: 1000,
                MileageIntervalEngineHours: 200,
                MileageSoonEngineHours: 30),
            OrganizationTypeCodes.DrivingSchool => new(
                EnableDatePlanning: true,
                EnableMileagePlanning: true,
                EnableDefectPlanning: true,
                EnableCounterFreshness: true,
                EnableFirstService: true,
                PlannedServiceIntervalDays: 60,
                SoonServiceWindowDays: 14,
                CounterStaleWindowDays: 14,
                FirstServiceGraceDays: 7,
                MileageIntervalKilometers: 5000,
                MileageSoonKilometers: 750,
                MileageIntervalEngineHours: 150,
                MileageSoonEngineHours: 25),
            _ => new(
                EnableDatePlanning: true,
                EnableMileagePlanning: true,
                EnableDefectPlanning: true,
                EnableCounterFreshness: true,
                EnableFirstService: true,
                PlannedServiceIntervalDays: 180,
                SoonServiceWindowDays: 30,
                CounterStaleWindowDays: 45,
                FirstServiceGraceDays: 14,
                MileageIntervalKilometers: 10000,
                MileageSoonKilometers: 1500,
                MileageIntervalEngineHours: 250,
                MileageSoonEngineHours: 40)
        };
    }

    /// <summary> Проверяет, что правило относится к планированию по датам. </summary>
    private static bool IsDateRule(OrganizationPlanningRuleSnapshot rule) =>
        ContainsAny(rule, "дата", "дате", "датам", "визит", "запись", "регламент", "этап");

    /// <summary> Проверяет, что правило относится к планированию по пробегу. </summary>
    private static bool IsMileageRule(OrganizationPlanningRuleSnapshot rule) =>
        ContainsAny(rule, "пробег", "моточас", "наработка", "ресурс", "км", "километр");

    /// <summary> Проверяет, что правило относится к планированию по дефектам. </summary>
    private static bool IsDefectRule(OrganizationPlanningRuleSnapshot rule) =>
        ContainsAny(rule, "неисправ", "замеч", "заявк", "дефект");

    /// <summary> Проверяет, что правило относится к планированию по счётчикам. </summary>
    private static bool IsCounterRule(OrganizationPlanningRuleSnapshot rule) =>
        ContainsAny(rule, "счетчик", "счётчик", "обнов", "данн", "пробег", "наработка");

    /// <summary> Проверяет, что правило относится к первому обслуживанию. </summary>
    private static bool IsFirstServiceRule(OrganizationPlanningRuleSnapshot rule) =>
        ContainsAny(rule, "первич", "первое", "осмотр", "прием", "приём", "запуск");

    /// <summary> Определяет интервал в днях из правил с учётом значения по умолчанию. </summary>
    private static int ResolveDays(IReadOnlyList<OrganizationPlanningRuleSnapshot> rules, int fallback) =>
        ResolveNumber(rules, fallback, 1, 730);

    /// <summary> Определяет окно скорого обслуживания в днях из правил. </summary>
    private static int ResolveSoonDays(IReadOnlyList<OrganizationPlanningRuleSnapshot> rules, int fallback) =>
        Math.Min(ResolveNumber(rules, fallback, 1, 120), fallback);

    /// <summary> Определяет порог по пробегу из правил с учётом значения по умолчанию. </summary>
    private static int ResolveMileage(IReadOnlyList<OrganizationPlanningRuleSnapshot> rules, int fallback) =>
        ResolveNumber(rules, fallback, 100, 100000);

    /// <summary> Определяет порог по моточасам из правил с учётом значения по умолчанию. </summary>
    private static int ResolveEngineHours(IReadOnlyList<OrganizationPlanningRuleSnapshot> rules, int fallback) =>
        ResolveNumber(rules, fallback, 10, 5000);

    /// <summary> Определяет числовое значение из правил с ограничениями диапазона. </summary>
    private static int ResolveNumber(IReadOnlyList<OrganizationPlanningRuleSnapshot> rules, int fallback, int min, int max)
    {
        foreach (var rule in rules)
        {
            var text = BuildText(rule);
            for (var index = 0; index < text.Length;)
            {
                if (!char.IsDigit(text[index]))
                {
                    index++;
                    continue;
                }

                var start = index;
                while (index < text.Length && char.IsDigit(text[index]))
                {
                    index++;
                }

                if (!int.TryParse(text.AsSpan(start, index - start), out var value) || value < min || value > max)
                {
                    continue;
                }

                return value;
            }
        }

        return fallback;
    }

    /// <summary> Проверяет наличие любого из ключевых слов в правиле. </summary>
    private static bool ContainsAny(OrganizationPlanningRuleSnapshot rule, params string[] tokens)
    {
        var text = BuildText(rule);
        return tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary> Формирует текст правила из его полей. </summary>
    private static string BuildText(OrganizationPlanningRuleSnapshot rule) =>
        $"{rule.Code} {rule.Name} {rule.Description}".ToLowerInvariant();
}
