using OpenCRM.Domain;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Catalog.Rules;

namespace OpenCRM.Modules.Catalog.Defaults;

/// <summary> Набор предустановленных элементов каталога по умолчанию. </summary>
internal static class CatalogDefaults
{
    /// <summary> Строит элементы каталога по умолчанию для сценария. </summary>
    internal static IReadOnlyList<OrganizationScenarioCatalogItem> Build(OrganizationScenarioProcessDefinition process)
    {
        var items = new List<OrganizationScenarioCatalogItem>();
        var order = 10;

        foreach (var stage in process.WorkStages)
        {
            items.Add(CatalogDefaults.Create(process.Code, OrganizationScenarioCatalogKindCodes.WorkType, stage.Code, stage.Name, order));
            order += 10;
        }

        foreach (var post in CatalogDefaults.ResolveWorkPosts(process.Code).Select((name, index) => new { name, index }))
        {
            items.Add(CatalogDefaults.Create(process.Code, OrganizationScenarioCatalogKindCodes.WorkPost, CatalogRules.Slug(post.name), post.name, (post.index + 1) * 10));
        }

        foreach (var service in CatalogDefaults.ResolveServices(process.Code).Select((name, index) => new { name, index }))
        {
            items.Add(CatalogDefaults.Create(process.Code, OrganizationScenarioCatalogKindCodes.Service, CatalogRules.Slug(service.name), service.name, (service.index + 1) * 10));
        }

        order = 10;
        foreach (var document in process.DocumentTemplates)
        {
            items.Add(CatalogDefaults.Create(
                process.Code,
                OrganizationScenarioCatalogKindCodes.Document,
                CatalogRules.Slug(document.Name),
                document.Name,
                order,
                document.KindCode));
            order += 10;
        }

        order = 10;
        foreach (var rule in process.PlanningRules)
        {
            items.Add(CatalogDefaults.Create(process.Code, OrganizationScenarioCatalogKindCodes.PlanningRule, CatalogRules.Slug(rule), rule, order));
            order += 10;
        }

        foreach (var material in CatalogDefaults.ResolveMaterials(process.Code).Select((name, index) => new { name, index }))
        {
            items.Add(CatalogDefaults.Create(process.Code, OrganizationScenarioCatalogKindCodes.Material, CatalogRules.Slug(material.name), material.name, (material.index + 1) * 10));
        }

        return items;
    }

    /// <summary> Создаёт элемент каталога по умолчанию. </summary>
    private static OrganizationScenarioCatalogItem Create(
        string scenarioCode,
        string kindCode,
        string code,
        string name,
        int sortOrder,
        string? documentKindCode = null) =>
        new()
        {
            ScenarioCode = scenarioCode,
            KindCode = kindCode,
            Code = code,
            Name = name,
            DocumentKindCode = kindCode == OrganizationScenarioCatalogKindCodes.Document
                ? OrganizationScenarioDocumentKindCodes.Normalize(documentKindCode) ?? CatalogRules.InferDocumentKindCode(name)
                : null,
            SortOrder = sortOrder,
            IsSystem = true,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

    /// <summary> Определяет материалы по умолчанию для сценария. </summary>
    private static IReadOnlyList<string> ResolveMaterials(string scenarioCode) => scenarioCode switch
    {
        OrganizationTypeCodes.ServiceStation => ["Масла", "Фильтры", "Тормозные колодки", "Свечи", "Лампы", "Расходники ремонта"],
        OrganizationTypeCodes.Detailing => ["Химия", "Автошампунь", "Воск", "Очиститель салона", "Микрофибра", "Полировальные круги", "Защитные составы"],
        OrganizationTypeCodes.Wrapping => ["Пленка", "Праймер", "Ракели", "Расходники монтажа"],
        OrganizationTypeCodes.CarWash => ["Автошампунь", "Воск", "Очиститель салона", "Микрофибра", "Чернитель шин"],
        OrganizationTypeCodes.Fleet => ["Масла", "Фильтры", "Шины", "Расходники ремонта"],
        OrganizationTypeCodes.DrivingSchool => ["Масла", "Фильтры", "Учебное оборудование", "Шины"],
        _ => ["Масла", "Фильтры", "Запчасти", "Расходники"]
    };

    /// <summary> Определяет рабочие посты по умолчанию для сценария. </summary>
    private static IReadOnlyList<string> ResolveWorkPosts(string scenarioCode) => scenarioCode switch
    {
        OrganizationTypeCodes.ServiceStation => ["Пост приемки", "Подъемник 1", "Подъемник 2", "Диагностика"],
        OrganizationTypeCodes.Detailing => ["Пост 1", "Пост 2", "Пост 3"],
        OrganizationTypeCodes.CarWash => ["Пост 1", "Пост 2", "Пост 3"],
        _ => []
    };

    /// <summary> Определяет услуги по умолчанию для сценария. </summary>
    private static IReadOnlyList<string> ResolveServices(string scenarioCode) => scenarioCode switch
    {
        OrganizationTypeCodes.ServiceStation =>
        [
            "Диагностика",
            "Замена масла",
            "Тормозная система",
            "Подвеска",
            "Электрика",
            "Шиномонтаж"
        ],
        OrganizationTypeCodes.Detailing =>
        [
            "Экспресс-мойка",
            "Комплексная мойка",
            "Химчистка салона",
            "Полировка кузова",
            "Защитное покрытие",
            "Мойка двигателя"
        ],
        OrganizationTypeCodes.CarWash =>
        [
            "Экспресс-мойка",
            "Комплексная мойка",
            "Мойка салона",
            "Чернение шин",
            "Воск"
        ],
        _ => []
    };
}
