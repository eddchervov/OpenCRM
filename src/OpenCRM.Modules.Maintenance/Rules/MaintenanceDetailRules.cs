using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Materials;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Правила работы со строками обслуживания и материалами. </summary>
internal sealed class MaintenanceDetailRules(
    OpenCRMContext context,
    MaintenanceEmployeeResolver maintenanceEmployeeResolver)
{
    /// <summary> Зависимость для определения сотрудника по строкам обслуживания. </summary>
    private readonly MaintenanceEmployeeResolver _maintenanceEmployeeResolver = maintenanceEmployeeResolver;

    /// <summary> Максимально допустимое количество материала в строке. </summary>
    internal const int MaxMaterialQuantity = 999_999_999;

    /// <summary> Преобразует DTO создания строк в сущности строк обслуживания. </summary>
    internal static List<MaintenanceDetail> MapDetails(IEnumerable<CreateMaintenanceDetailDto> details)
    {
        if (details == null)
        {
            throw new InvalidOperationException("Добавьте хотя бы одну услугу или материал.");
        }

        var mappedDetails = details.Select(MapDetail).ToList();
        if (mappedDetails.Count == 0)
        {
            throw new InvalidOperationException("Добавьте хотя бы одну услугу или материал.");
        }

        return mappedDetails;
    }

    /// <summary> Преобразует DTO обновления строк в сущности строк обслуживания. </summary>
    internal static List<MaintenanceDetail> MapDetails(IEnumerable<UpdateMaintenanceDetailDto> details)
    {
        if (details == null)
        {
            throw new InvalidOperationException("Добавьте хотя бы одну услугу или материал.");
        }

        var mappedDetails = details.Select(MapDetail).ToList();
        if (mappedDetails.Count == 0)
        {
            throw new InvalidOperationException("Добавьте хотя бы одну услугу или материал.");
        }

        return mappedDetails;
    }

    /// <summary> Назначает идентификаторы строкам, сохраняя прежние где возможно. </summary>
    internal static void AssignDetailIds(
        IReadOnlyList<MaintenanceDetail> details,
        IReadOnlyList<MaintenanceDetail>? previousDetails = null)
    {
        var previous = previousDetails ?? [];
        var previousIds = previous.Select(detail => detail.DetailId).Where(id => id != Guid.Empty).ToHashSet();
        var usedIds = new HashSet<Guid>();

        foreach (var detail in details)
        {
            if (detail.DetailId == Guid.Empty)
            {
                var matches = previous.Where(item => item.DetailId != Guid.Empty
                    && !usedIds.Contains(item.DetailId)
                    && item.CatalogItemId == detail.CatalogItemId
                    && item.MaterialCatalogItemId == detail.MaterialCatalogItemId
                    && string.Equals(item.Name, detail.Name, StringComparison.Ordinal)).ToList();
                detail.DetailId = matches.Count == 1 ? matches[0].DetailId : Guid.NewGuid();
            }
            else if (!previousIds.Contains(detail.DetailId))
            {
                throw new InvalidOperationException("Идентификатор строки работы не принадлежит обслуживанию.");
            }

            if (!usedIds.Add(detail.DetailId))
            {
                throw new InvalidOperationException("Одна работа не может повторяться в составе обслуживания.");
            }
        }
    }

    /// <summary> Проверяет корректность строк обслуживания и материалов. </summary>
    internal async Task ValidateServiceDetailsAsync(
        Guid? organizationId,
        IReadOnlyList<MaintenanceDetail> details,
        IReadOnlyList<MaintenanceDetail>? previousDetails = null)
    {
        await this._maintenanceEmployeeResolver.ValidateDetailEmployeesAsync(organizationId, details, previousDetails);
        if (!organizationId.HasValue)
        {
            return;
        }

        var serviceLines = details.Where(detail => !MaintenanceDetailRules.IsMaterialDetail(detail)).ToList();
        if (serviceLines.Count == 0)
        {
            throw new InvalidOperationException("Добавьте хотя бы одну работу.");
        }

        if (details.Any(detail => MaintenanceDetailRules.IsMaterialDetail(detail) && detail.CatalogItemId.HasValue))
        {
            throw new InvalidOperationException("Услуга и материал должны быть отдельными строками записи.");
        }

        var previousMaterials = previousDetails?
            .Where(detail => detail.MaterialCatalogItemId.HasValue)
            .Select(detail => (detail.DetailId, MaterialId: detail.MaterialCatalogItemId!.Value))
            .ToHashSet() ?? [];

        var newMaterialIds = details
            .Where(detail => detail.MaterialCatalogItemId.HasValue)
            .Where(detail => !previousMaterials.Contains((detail.DetailId, detail.MaterialCatalogItemId!.Value)))
            .Select(detail => detail.MaterialCatalogItemId!.Value)
            .Distinct()
            .ToList();
        if (newMaterialIds.Count > 0)
        {
            var activeMaterialIds = await context.OrganizationScenarioCatalogItems.AsNoTracking()
                .Where(item => newMaterialIds.Contains(item.Id)
                    && item.OrganizationId == organizationId.Value
                    && item.KindCode == OrganizationScenarioCatalogKindCodes.Material
                    && item.IsActive)
                .Select(item => item.Id)
                .ToListAsync();
            var activeMaterials = activeMaterialIds.ToHashSet();

            if (newMaterialIds.Any(materialId => !activeMaterials.Contains(materialId)))
            {
                throw new InvalidOperationException("Материал не найден в справочнике этой организации или находится в архиве. Выберите действующий материал.");
            }
        }

        var catalogLines = serviceLines.Where(detail => detail.CatalogItemId.HasValue).ToList();
        var hasDuplicateServices = catalogLines.GroupBy(detail => detail.CatalogItemId!.Value).Any(group =>
            group.Count() > Math.Max(1, previousDetails?.Count(detail => detail.CatalogItemId == group.Key) ?? 0));

        if (serviceLines.Count > 100 || hasDuplicateServices)
        {
            throw new InvalidOperationException("Не добавляйте услугу повторно: измените её количество. В записи допускается до 100 строк услуг.");
        }

        var ids = catalogLines.Select(detail => detail.CatalogItemId!.Value).Distinct().ToList();
        var catalog = await context.OrganizationScenarioCatalogItems.AsNoTracking()
            .Where(item => ids.Contains(item.Id)
                && item.OrganizationId == organizationId.Value
                && item.KindCode == OrganizationScenarioCatalogKindCodes.Service)
            .ToDictionaryAsync(item => item.Id);
        var remainingPrevious = previousDetails?.Where(detail => !MaintenanceDetailRules.IsMaterialDetail(detail)).ToList() ?? [];

        foreach (var detail in catalogLines)
        {
            if (!catalog.TryGetValue(detail.CatalogItemId!.Value, out var service))
            {
                throw new InvalidOperationException("Услуга не найдена в справочнике этой организации. Выберите другую услугу.");
            }

            var previous = remainingPrevious.FirstOrDefault(item => item.CatalogItemId == service.Id && item.Name == detail.Name)
                ?? remainingPrevious.FirstOrDefault(item => item.CatalogItemId == service.Id);
            if (previous != null)
            {
                detail.Name = previous.Name;
                remainingPrevious.Remove(previous);
                continue;
            }

            if (!service.IsActive)
            {
                throw new InvalidOperationException($"Услуга «{service.Name}» в архиве. Для новой строки выберите действующую услугу.");
            }

            detail.Name = service.Name;
        }
    }

    /// <summary> Определяет, является ли строка строкой материала. </summary>
    internal static bool IsMaterialDetail(MaintenanceDetail detail)
    {
        return detail.MaterialCatalogItemId.HasValue
            || !string.IsNullOrWhiteSpace(detail.MaterialName)
            || detail.MaterialQuantity.HasValue;
    }

    /// <summary> Создаёт строку обслуживания из переданных полей. </summary>
    internal static MaintenanceDetail MapDetail(
        Guid? detailId,
        Guid? employeeId,
        string name,
        string? description,
        decimal? price,
        string? vatRateCode,
        int quantity,
        Guid? catalogItemId,
        Guid? materialCatalogItemId,
        string? materialName,
        decimal? materialQuantity,
        string? materialUnit,
        decimal? materialUnitCost,
        decimal? materialTotalCost)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new InvalidOperationException("Укажите название услуги или материала.");
        }

        MaintenanceTextRules.ValidateSafeText("Название услуги или материала", normalizedName);
        var normalizedDescription = MaintenanceTextRules.NormalizeOptional(description);

        if (quantity < 1 || quantity > 10000)
        {
            throw new InvalidOperationException("Количество услуг должно быть целым числом от 1 до 10000.");
        }

        if (price.HasValue && (price.Value < 0 || price.Value > 999999999))
        {
            throw new InvalidOperationException("Стоимость услуги не может быть отрицательной.");
        }

        if (price.HasValue && Math.Round((decimal)price.Value, 2, MidpointRounding.AwayFromZero) != (decimal)price.Value)
        {
            throw new InvalidOperationException("Стоимость услуги указывайте с точностью до копеек.");
        }

        if (vatRateCode != null && !MaintenanceVatRates.IsValid(vatRateCode))
        {
            throw new InvalidOperationException("Неизвестная ставка НДС.");
        }

        if (materialQuantity.HasValue && materialQuantity.Value < 0)
        {
            throw new InvalidOperationException("Количество материала не может быть отрицательным.");
        }
        if (materialQuantity.HasValue && materialQuantity.Value > MaintenanceDetailRules.MaxMaterialQuantity)
        {
            throw new InvalidOperationException("Расход материала слишком большой.");
        }

        return new MaintenanceDetail
        {
            DetailId = detailId.GetValueOrDefault(),
            EmployeeId = employeeId,
            Description = normalizedDescription,
            Name = normalizedName,
            Price = price,
            VatRateCode = vatRateCode,
            Quantity = quantity,
            CatalogItemId = catalogItemId,
            MaterialCatalogItemId = materialCatalogItemId,
            MaterialName = MaintenanceTextRules.NormalizeOptional(materialName),
            MaterialQuantity = materialQuantity,
            MaterialUnit = MaintenanceDetailRules.NormalizeMaterialUnit(materialUnit, materialCatalogItemId, materialName, materialQuantity),
            MaterialUnitCost = MaintenanceMoneyRules.NormalizeMoney(materialUnitCost),
            MaterialTotalCost = MaintenanceMoneyRules.NormalizeMoney(materialTotalCost)
        };
    }

    /// <summary> Создаёт строку обслуживания из DTO создания. </summary>
    internal static MaintenanceDetail MapDetail(CreateMaintenanceDetailDto detail) => MaintenanceDetailRules.MapDetail(
        detail.DetailId,
        detail.EmployeeId,
        detail.Name,
        detail.Description,
        detail.Price,
        detail.VatRateCode,
        detail.Quantity,
        detail.CatalogItemId,
        detail.MaterialCatalogItemId,
        detail.MaterialName,
        detail.MaterialQuantity,
        detail.MaterialUnit,
        detail.MaterialUnitCost,
        detail.MaterialTotalCost);

    /// <summary> Создаёт строку обслуживания из DTO обновления. </summary>
    internal static MaintenanceDetail MapDetail(UpdateMaintenanceDetailDto detail) => MaintenanceDetailRules.MapDetail(
        detail.DetailId,
        detail.EmployeeId,
        detail.Name,
        detail.Description,
        detail.Price,
        detail.VatRateCode,
        detail.Quantity,
        detail.CatalogItemId,
        detail.MaterialCatalogItemId,
        detail.MaterialName,
        detail.MaterialQuantity,
        detail.MaterialUnit,
        detail.MaterialUnitCost,
        detail.MaterialTotalCost);

    /// <summary> Приводит единицу измерения материала или берёт её из каталога. </summary>
    internal static string? NormalizeMaterialUnit(string? value, Guid? materialCatalogItemId, string? materialName, decimal? materialQuantity)
    {
        var hasMaterial = materialCatalogItemId.HasValue
            || !string.IsNullOrWhiteSpace(materialName)
            || materialQuantity.HasValue;

        if (!hasMaterial)
        {
            return null;
        }

        return MaterialUnitRules.Normalize(
            value,
            "Выберите единицу измерения из справочника: шт, л, мл, кг, г, м, м2 или компл.");
    }
}
