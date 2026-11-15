using OpenCRM.Domain;
using OpenCRM.Modules.Catalog.Rules;

namespace OpenCRM.Modules.Catalog.Tests.Unit;

public sealed class CatalogRulesTests
{
    [Fact]
    public void NormalizeNameTrimsAndKeepsNamesUpToLimit()
    {
        Assert.Equal("Название", CatalogRules.NormalizeName("  Название  "));
        Assert.Equal(new string('я', 160), CatalogRules.NormalizeName(new string('я', 161)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NormalizeNameRejectsMissingName(string? value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => CatalogRules.NormalizeName(value));

        Assert.Equal("Укажите название элемента справочника.", exception.Message);
    }

    [Theory]
    [InlineData(" описание ", "описание")]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    public void NormalizeOptionalTrimsAndConvertsBlankValuesToNull(string? value, string? expected)
    {
        Assert.Equal(expected, CatalogRules.NormalizeOptional(value));
    }

    [Fact]
    public void NormalizeOptionalTruncatesAtOneThousandCharacters()
    {
        Assert.Equal(new string('x', 1000), CatalogRules.NormalizeOptional(new string('x', 1001)));
    }

    [Theory]
    [InlineData("Заказ", OrganizationScenarioDocumentKindCodes.WorkOrder)]
    [InlineData("Наряд", OrganizationScenarioDocumentKindCodes.WorkOrder)]
    [InlineData("Заявка", OrganizationScenarioDocumentKindCodes.WorkOrder)]
    [InlineData("Акт приема", OrganizationScenarioDocumentKindCodes.InspectionAct)]
    [InlineData("Акт приёма", OrganizationScenarioDocumentKindCodes.InspectionAct)]
    [InlineData("Осмотр", OrganizationScenarioDocumentKindCodes.InspectionAct)]
    [InlineData("Диагностика", OrganizationScenarioDocumentKindCodes.InspectionAct)]
    [InlineData("Акт работ", OrganizationScenarioDocumentKindCodes.WorkAct)]
    [InlineData("Фотоотчет", OrganizationScenarioDocumentKindCodes.PhotoReport)]
    [InlineData("Рекомендации", OrganizationScenarioDocumentKindCodes.Recommendations)]
    [InlineData("Памятка", OrganizationScenarioDocumentKindCodes.Recommendations)]
    [InlineData("Материалы", OrganizationScenarioDocumentKindCodes.MaterialsSpec)]
    [InlineData("Спецификация", OrganizationScenarioDocumentKindCodes.MaterialsSpec)]
    [InlineData("Затраты", OrganizationScenarioDocumentKindCodes.MaterialsSpec)]
    [InlineData("Чек", OrganizationScenarioDocumentKindCodes.Receipt)]
    [InlineData("Квитанция", OrganizationScenarioDocumentKindCodes.Receipt)]
    [InlineData("Готовность", OrganizationScenarioDocumentKindCodes.ReadinessReport)]
    [InlineData("Журнал", OrganizationScenarioDocumentKindCodes.Checklist)]
    [InlineData("Лист", OrganizationScenarioDocumentKindCodes.Checklist)]
    [InlineData("Другой документ", OrganizationScenarioDocumentKindCodes.MaintenanceReport)]
    [InlineData(null, OrganizationScenarioDocumentKindCodes.MaintenanceReport)]
    public void InferDocumentKindCodeMapsEverySupportedKeyword(string? name, string expected)
    {
        Assert.Equal(expected, CatalogRules.InferDocumentKindCode(name));
    }

    [Theory]
    [InlineData("до/после", "обычный документ", true)]
    [InlineData("обычный", "Фото до-после", true)]
    [InlineData("before-after", "Отчет", true)]
    [InlineData("before/after", "Отчет", true)]
    [InlineData("before after", "Отчет", false)]
    [InlineData("ordinary", "Обычный отчет", false)]
    public void IsUnsupportedLegacyDocumentDetectsOnlyUnsupportedLabels(string code, string name, bool expected)
    {
        var item = new OrganizationScenarioCatalogItem { Code = code, Name = name };

        Assert.Equal(expected, CatalogRules.IsUnsupportedLegacyDocument(item));
    }

    [Theory]
    [InlineData("  Защита кузова!  ", "защита-кузова")]
    [InlineData("Мойка 2", "мойка-2")]
    [InlineData("---", "")]
    public void SlugCreatesLowercaseDashSeparatedCode(string value, string expected)
    {
        Assert.Equal(expected, CatalogRules.Slug(value));
    }
}
