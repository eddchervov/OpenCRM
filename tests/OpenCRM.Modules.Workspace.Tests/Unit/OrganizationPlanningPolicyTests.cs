using System.Text.RegularExpressions;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Workspace.Queries;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Workspace.Tests.Unit;

public sealed class OrganizationPlanningPolicyTests
{
    [Theory]
    [InlineData(OrganizationTypeCodes.Detailing, 90, 14, 10000, 250)]
    [InlineData(OrganizationTypeCodes.Wrapping, 30, 10, 10000, 250)]
    [InlineData(OrganizationTypeCodes.CarWash, 30, 7, 10000, 250)]
    [InlineData(OrganizationTypeCodes.Fleet, 90, 21, 8000, 200)]
    [InlineData(OrganizationTypeCodes.DrivingSchool, 60, 14, 5000, 150)]
    [InlineData("unknown", 180, 30, 10000, 250)]
    public void BuildUsesScenarioDefaults(
        string scenarioCode,
        int dateInterval,
        int soonWindow,
        int mileageInterval,
        int engineHoursInterval)
    {
        var policy = OrganizationPlanningPolicyFactory.Build(
            OrganizationScenarioProcessCatalog.Resolve(scenarioCode), []);

        Assert.Equal(dateInterval, policy.PlannedServiceIntervalDays);
        Assert.Equal(soonWindow, policy.SoonServiceWindowDays);
        Assert.Equal(mileageInterval, policy.MileageIntervalKilometers);
        Assert.Equal(engineHoursInterval, policy.MileageIntervalEngineHours);
    }

    [Fact]
    public void BuildActivatesOnlyRulesThatAreActiveAndUsesTheirFirstValidNumbers()
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(OrganizationTypeCodes.Detailing);
        var rules = new[]
        {
            Rule("date", "Дата ТО", "0 дней; 45 дней; 10 дней"),
            Rule("mileage", "Пробег и моточасы", "пробег 0 км, 6000 км; моточасы 5001, 100"),
            Rule("counter", "Обновить счётчик", "через 20 дней"),
            Rule("first", "Первый осмотр", "через 5 дней"),
            Rule("defect", "Контроль дефектов", null),
            new OrganizationPlanningRuleSnapshot("дата 1", "неактивное", "", false)
        };

        var policy = OrganizationPlanningPolicyFactory.Build(process, rules);

        Assert.True(policy.EnableDatePlanning);
        Assert.True(policy.EnableMileagePlanning);
        Assert.True(policy.EnableCounterFreshness);
        Assert.True(policy.EnableFirstService);
        Assert.True(policy.EnableDefectPlanning);
        Assert.Equal(45, policy.PlannedServiceIntervalDays);
        Assert.Equal(14, policy.SoonServiceWindowDays);
        Assert.Equal(100, policy.CounterStaleWindowDays);
        Assert.Equal(5, policy.FirstServiceGraceDays);
        Assert.Equal(6000, policy.MileageIntervalKilometers);
        Assert.Equal(100, policy.MileageIntervalEngineHours);
        Assert.Equal(1500, policy.GetMileageSoonThreshold(VehicleMileageUnit.Kilometers));
        Assert.Equal(40, policy.GetMileageSoonThreshold(VehicleMileageUnit.EngineHours));
        Assert.Equal(6000, policy.GetMileageInterval(VehicleMileageUnit.Kilometers));
        Assert.Equal(100, policy.GetMileageInterval(VehicleMileageUnit.EngineHours));
    }

    [Fact]
    public void BuildWithInactiveRulesDisablesRuleDrivenFeaturesButPreservesScenarioFallbacks()
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(OrganizationTypeCodes.Fleet);
        var policy = OrganizationPlanningPolicyFactory.Build(process,
        [
            new OrganizationPlanningRuleSnapshot("пробег 500", "дата 1", "дефект счетчик первичный", false)
        ]);

        Assert.False(policy.EnableDatePlanning);
        Assert.False(policy.EnableMileagePlanning);
        Assert.False(policy.EnableDefectPlanning);
        Assert.False(policy.EnableCounterFreshness);
        Assert.True(policy.EnableFirstService);
        Assert.Equal(90, policy.PlannedServiceIntervalDays);
        Assert.Equal(8000, policy.MileageIntervalKilometers);
    }

    [Fact]
    public void BuildEnablesFleetCounterFreshnessForMileageRulesWithoutAnExplicitCounterRule()
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(OrganizationTypeCodes.Fleet);
        var policy = OrganizationPlanningPolicyFactory.Build(
            process,
            [Rule("пробег", "Пробег", "8000 км")]);

        Assert.True(policy.EnableMileagePlanning);
        Assert.True(policy.EnableCounterFreshness);
        Assert.Equal(8000, policy.MileageIntervalKilometers);
    }

    [Fact]
    public void BuildKeepsFirstServiceDisabledWhenScenarioAndActiveRulesDisableIt()
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(OrganizationTypeCodes.CarWash);
        var policy = OrganizationPlanningPolicyFactory.Build(
            process,
            [Rule("date", "Дата обслуживания", "30 дней")]);

        Assert.False(policy.EnableFirstService);
        Assert.Equal(30, policy.PlannedServiceIntervalDays);
    }

    [Fact]
    public void BuildFallsBackWhenRuleNumbersAreMissingOutsideBoundsOrOverflow()
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(OrganizationTypeCodes.Detailing);
        var policy = OrganizationPlanningPolicyFactory.Build(process,
        [
            Rule("дата", "Дата 0 и 731", "999999999999"),
            Rule("пробег", "Пробег 0 км и 100001 км", "моточасы 9 и 500000"),
            Rule("дата", "Дата без чисел", "٠٠٠")
        ]);

        Assert.Equal(90, policy.PlannedServiceIntervalDays);
        Assert.Equal(14, policy.SoonServiceWindowDays);
        Assert.Equal(10000, policy.MileageIntervalKilometers);
        Assert.Equal(250, policy.MileageIntervalEngineHours);
    }

    [Theory]
    [InlineData("-12", 12)]
    [InlineData("12.5", 12)]
    [InlineData("731 и 10", 10)]
    [InlineData("٩", 90)]
    [InlineData("١2", 90)]
    [InlineData("999999999999999999999", 90)]
    [InlineData("срок 30", 30)]
    [InlineData("", 90)]
    public void BuildResolvesNumbersLikeThePreviousRegexImplementation(string description, int expected)
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(OrganizationTypeCodes.Detailing);
        var rules = new[] { Rule("дата", "", description) };
        var policy = OrganizationPlanningPolicyFactory.Build(process, rules);
        var previousResult = ResolveNumberWithRegex(rules, fallback: 90, min: 1, max: 730);

        Assert.Equal(previousResult, policy.PlannedServiceIntervalDays);
        Assert.Equal(expected, policy.PlannedServiceIntervalDays);
    }

    [Theory]
    [InlineData("дата")]
    [InlineData("дате")]
    [InlineData("датам")]
    [InlineData("визит")]
    [InlineData("запись")]
    [InlineData("регламент")]
    [InlineData("этап")]
    public void RecognizesDatePlanningTerms(string text) =>
        Assert.True(BuildWithRule(text).EnableDatePlanning);

    [Theory]
    [InlineData("пробег")]
    [InlineData("моточас")]
    [InlineData("наработка")]
    [InlineData("ресурс")]
    [InlineData("км")]
    [InlineData("километр")]
    public void RecognizesMileagePlanningTerms(string text) =>
        Assert.True(BuildWithRule(text).EnableMileagePlanning);

    [Theory]
    [InlineData("неисправ")]
    [InlineData("замеч")]
    [InlineData("заявк")]
    [InlineData("дефект")]
    public void RecognizesDefectPlanningTerms(string text) =>
        Assert.True(BuildWithRule(text).EnableDefectPlanning);

    [Theory]
    [InlineData("счетчик")]
    [InlineData("счётчик")]
    [InlineData("обнов")]
    [InlineData("данн")]
    [InlineData("пробег")]
    [InlineData("наработка")]
    public void RecognizesCounterPlanningTerms(string text) =>
        Assert.True(BuildWithRule(text).EnableCounterFreshness);

    [Theory]
    [InlineData("первич")]
    [InlineData("первое")]
    [InlineData("осмотр")]
    [InlineData("прием")]
    [InlineData("приём")]
    [InlineData("запуск")]
    public void RecognizesFirstServicePlanningTerms(string text) =>
        Assert.True(BuildWithRule(text).EnableFirstService);

    private static OrganizationPlanningPolicy BuildWithRule(string text)
    {
        var process = OrganizationScenarioProcessCatalog.Resolve("unknown");

        return OrganizationPlanningPolicyFactory.Build(process,
        [
            Rule(text, "", null)
        ]);
    }

    private static OrganizationPlanningRuleSnapshot Rule(string code, string name, string? description) =>
        new(code, name, description, true);

    private static int ResolveNumberWithRegex(
        IReadOnlyList<OrganizationPlanningRuleSnapshot> rules,
        int fallback,
        int min,
        int max)
    {
        foreach (var rule in rules)
        {
            var text = $"{rule.Code} {rule.Name} {rule.Description}".ToLowerInvariant();
            foreach (Match match in Regex.Matches(text, @"\d+"))
            {
                if (int.TryParse(match.Value, out var value) && value >= min && value <= max)
                {
                    return value;
                }
            }
        }

        return fallback;
    }
}
