using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Defaults;
using OpenCRM.Modules.Catalog.Rules;
using OpenCRM.Services.Interfaces.Organizations;

namespace OpenCRM.Modules.Catalog.Tests.Unit;

public sealed class CatalogDefaultsTests
{
    [Fact]
    public void BuildInfersDocumentKindWhenScenarioTemplateHasUnknownKind()
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(OrganizationTypeCodes.Detailing) with
        {
            DocumentTemplates = [new OrganizationScenarioDocumentDefinition("Заказ на работу", "unsupported-kind")]
        };

        var document = CatalogDefaults.Build(process).Single(item =>
            item.KindCode == OrganizationScenarioCatalogKindCodes.Document);

        Assert.Equal(OrganizationScenarioDocumentKindCodes.WorkOrder, document.DocumentKindCode);
    }

    [Theory]
    [InlineData(OrganizationTypeCodes.ServiceStation,
        "Пост приемки|Подъемник 1|Подъемник 2|Диагностика",
        "Диагностика|Замена масла|Тормозная система|Подвеска|Электрика|Шиномонтаж",
        "Масла|Фильтры|Тормозные колодки|Свечи|Лампы|Расходники ремонта")]
    [InlineData(OrganizationTypeCodes.Detailing,
        "Пост 1|Пост 2|Пост 3",
        "Экспресс-мойка|Комплексная мойка|Химчистка салона|Полировка кузова|Защитное покрытие|Мойка двигателя",
        "Химия|Автошампунь|Воск|Очиститель салона|Микрофибра|Полировальные круги|Защитные составы")]
    [InlineData(OrganizationTypeCodes.Wrapping,
        "",
        "",
        "Пленка|Праймер|Ракели|Расходники монтажа")]
    [InlineData(OrganizationTypeCodes.CarWash,
        "Пост 1|Пост 2|Пост 3",
        "Экспресс-мойка|Комплексная мойка|Мойка салона|Чернение шин|Воск",
        "Автошампунь|Воск|Очиститель салона|Микрофибра|Чернитель шин")]
    [InlineData(OrganizationTypeCodes.Fleet,
        "",
        "",
        "Масла|Фильтры|Шины|Расходники ремонта")]
    [InlineData(OrganizationTypeCodes.DrivingSchool,
        "",
        "",
        "Масла|Фильтры|Учебное оборудование|Шины")]
    [InlineData("unknown-scenario",
        "",
        "",
        "Масла|Фильтры|Запчасти|Расходники")]
    public void BuildCreatesScenarioItemsWithExpectedDefaults(
        string scenarioCode,
        string workPosts,
        string services,
        string materials)
    {
        var process = OrganizationScenarioProcessCatalog.Resolve(scenarioCode);

        var items = CatalogDefaults.Build(process);

        Assert.Equal(process.WorkStages.Count + process.DocumentTemplates.Count + process.PlanningRules.Count
            + CatalogDefaultsTests.Split(workPosts).Length + CatalogDefaultsTests.Split(services).Length + CatalogDefaultsTests.Split(materials).Length, items.Count);
        Assert.Equal(CatalogDefaultsTests.Split(workPosts), CatalogDefaultsTests.GetNames(items, OrganizationScenarioCatalogKindCodes.WorkPost));
        Assert.Equal(CatalogDefaultsTests.Split(services), CatalogDefaultsTests.GetNames(items, OrganizationScenarioCatalogKindCodes.Service));
        Assert.Equal(CatalogDefaultsTests.Split(materials), CatalogDefaultsTests.GetNames(items, OrganizationScenarioCatalogKindCodes.Material));
        Assert.Equal(process.WorkStages.Select(value => value.Name), CatalogDefaultsTests.GetNames(items, OrganizationScenarioCatalogKindCodes.WorkType));
        Assert.Equal(process.DocumentTemplates.Select(value => value.Name), CatalogDefaultsTests.GetNames(items, OrganizationScenarioCatalogKindCodes.Document));
        Assert.Equal(process.PlanningRules, CatalogDefaultsTests.GetNames(items, OrganizationScenarioCatalogKindCodes.PlanningRule));
        Assert.All(items, item =>
        {
            Assert.Equal(process.Code, item.ScenarioCode);
            Assert.NotEqual(Guid.Empty, item.Id);
            Assert.True(item.IsSystem);
            Assert.True(item.IsActive);
            Assert.NotEqual(default, item.CreatedAt);
            Assert.NotEqual(default, item.UpdatedAt);
            Assert.Null(item.Description);
            Assert.Null(item.UnitPrice);
            Assert.Null(item.DurationMinutes);
            Assert.Empty(item.BundleItems);
            var expectedCode = item.KindCode == OrganizationScenarioCatalogKindCodes.WorkType
                ? process.WorkStages.Single(stage => stage.Name == item.Name).Code
                : CatalogRules.Slug(item.Name);
            Assert.Equal(expectedCode, item.Code);
        });
        Assert.Equal(process.WorkStages.Select((_, index) => (index + 1) * 10), CatalogDefaultsTests.GetSortOrders(items, OrganizationScenarioCatalogKindCodes.WorkType));
        Assert.Equal(process.DocumentTemplates.Select((_, index) => (index + 1) * 10), CatalogDefaultsTests.GetSortOrders(items, OrganizationScenarioCatalogKindCodes.Document));
        Assert.Equal(process.PlanningRules.Select((_, index) => (index + 1) * 10), CatalogDefaultsTests.GetSortOrders(items, OrganizationScenarioCatalogKindCodes.PlanningRule));
        Assert.Equal(process.DocumentTemplates.Select(value => OrganizationScenarioDocumentKindCodes.Normalize(value.KindCode)),
            items.Where(item => item.KindCode == OrganizationScenarioCatalogKindCodes.Document).Select(item => item.DocumentKindCode));
        Assert.All(items.Where(item => item.KindCode != OrganizationScenarioCatalogKindCodes.Document), item => Assert.Null(item.DocumentKindCode));
    }

    private static string[] Split(string values) => string.IsNullOrEmpty(values) ? [] : values.Split('|');

    private static string[] GetNames(IReadOnlyCollection<OrganizationScenarioCatalogItem> items, string kindCode) =>
        items.Where(item => item.KindCode == kindCode).Select(item => item.Name).ToArray();

    private static int[] GetSortOrders(IReadOnlyCollection<OrganizationScenarioCatalogItem> items, string kindCode) =>
        items.Where(item => item.KindCode == kindCode).Select(item => item.SortOrder).ToArray();
}
