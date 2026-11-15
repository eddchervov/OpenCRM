using OpenCRM.Domain;
using OpenCRM.Domain.Enums;

namespace OpenCRM.Modules.Maintenance.Rules;

/// <summary> Правила выявления и описания изменений обслуживания. </summary>
internal static class MaintenanceChangeRules
{
    /// <summary> Определяет, изменилось ли обслуживание относительно снимка. </summary>
    internal static bool HasMaintenanceChanges(MaintenanceSnapshot previous, MaintenanceEntity maintenance)
    {
        return previous.Status != maintenance.Status
            || MaintenanceChangeRules.BuildFieldChanges(previous, maintenance).Count > 0
            || MaintenanceChangeRules.BuildDetailChanges(previous.Details, maintenance.Details).Count > 0;
    }

    /// <summary> Формирует список описаний изменённых полей обслуживания. </summary>
    internal static List<string> BuildFieldChanges(MaintenanceSnapshot previous, MaintenanceEntity maintenance)
    {
        var changes = new List<string>();

        MaintenanceChangeRules.AddChange(changes, "Тип", MaintenanceChangeRules.GetMaintenanceTypeText(previous.Type), MaintenanceChangeRules.GetMaintenanceTypeText(maintenance.Type));
        MaintenanceChangeRules.AddChange(changes, "Плановая дата", MaintenanceChangeRules.FormatDate(previous.PlannedDate), MaintenanceChangeRules.FormatDate(maintenance.PlannedDate));
        MaintenanceChangeRules.AddChange(changes, "Плановая дата окончания", MaintenanceChangeRules.FormatDate(previous.PlannedEndDate), MaintenanceChangeRules.FormatDate(maintenance.PlannedEndDate));
        MaintenanceChangeRules.AddChange(changes, "Время начала", MaintenanceChangeRules.FormatTime(previous.PlannedStartTime), MaintenanceChangeRules.FormatTime(maintenance.PlannedStartTime));
        MaintenanceChangeRules.AddChange(changes, "Время окончания", MaintenanceChangeRules.FormatTime(previous.PlannedEndTime), MaintenanceChangeRules.FormatTime(maintenance.PlannedEndTime));
        MaintenanceChangeRules.AddChange(changes, "Дата выполнения", MaintenanceChangeRules.FormatDate(previous.CompletedDate), MaintenanceChangeRules.FormatDate(maintenance.CompletedDate));
        MaintenanceChangeRules.AddChange(changes, "Готовность к выдаче (UTC)", previous.ReadyForHandoverAt?.ToString("O"), maintenance.ReadyForHandoverAt?.ToString("O"));
        MaintenanceChangeRules.AddChange(changes, "Выдача (UTC)", previous.HandedOverAt?.ToString("O"), maintenance.HandedOverAt?.ToString("O"));
        MaintenanceChangeRules.AddChange(changes, "Место", previous.Location, maintenance.Location);
        MaintenanceChangeRules.AddChange(changes, "Исполнитель", MaintenanceChangeRules.FormatEmployee(previous.EmployeeId, previous.Performer), MaintenanceChangeRules.FormatEmployee(maintenance.EmployeeId, maintenance.Performer));
        MaintenanceChangeRules.AddChange(changes, "Пробег/наработка", MaintenanceChangeRules.FormatNullableNumber(previous.Mileage), MaintenanceChangeRules.FormatNullableNumber(maintenance.Mileage));
        MaintenanceChangeRules.AddChange(changes, "Комментарий", previous.Comment, maintenance.Comment);
        MaintenanceChangeRules.AddChange(changes, "Скидка", MaintenanceChangeRules.FormatDecimalMoney(previous.DiscountAmount), MaintenanceChangeRules.FormatDecimalMoney(maintenance.DiscountAmount));
        MaintenanceChangeRules.AddChange(changes, "Оплачено", MaintenanceChangeRules.FormatDecimalMoney(previous.PaidAmount), MaintenanceChangeRules.FormatDecimalMoney(maintenance.PaidAmount));
        MaintenanceChangeRules.AddChange(changes, "Себестоимость труда", MaintenanceChangeRules.FormatDecimalMoney(previous.LaborCostAmount), MaintenanceChangeRules.FormatDecimalMoney(maintenance.LaborCostAmount));

        return changes;
    }

    /// <summary> Формирует список описаний изменений строк обслуживания. </summary>
    internal static List<string> BuildDetailChanges(IReadOnlyList<MaintenanceDetailSnapshot> previousDetails, IReadOnlyList<MaintenanceDetail> currentDetails)
    {
        var changes = new List<string>();
        var maxCommonCount = Math.Min(previousDetails.Count, currentDetails.Count);

        for (var index = 0; index < maxCommonCount; index++)
        {
            var previous = previousDetails[index];
            var current = currentDetails[index];
            if (MaintenanceChangeRules.AreSameDetail(previous, current))
            {
                continue;
            }

            var detailChanges = new List<string>();
            MaintenanceChangeRules.AddChange(detailChanges, "идентификатор строки", previous.DetailId.ToString(), current.DetailId.ToString());
            MaintenanceChangeRules.AddChange(detailChanges, "услуга из справочника", previous.CatalogItemId?.ToString(), current.CatalogItemId?.ToString());
            MaintenanceChangeRules.AddChange(detailChanges, "материал из справочника", previous.MaterialCatalogItemId?.ToString(), current.MaterialCatalogItemId?.ToString());
            MaintenanceChangeRules.AddChange(detailChanges, "сотрудник", MaintenanceChangeRules.FormatEmployee(previous.EmployeeId, previous.EmployeeName), MaintenanceChangeRules.FormatEmployee(current.EmployeeId, current.EmployeeName));
            MaintenanceChangeRules.AddChange(detailChanges, "название", previous.Name, current.Name);
            MaintenanceChangeRules.AddChange(detailChanges, "описание", previous.Description, current.Description);
            MaintenanceChangeRules.AddChange(detailChanges, "количество", previous.Quantity.ToString(), current.Quantity.ToString());
            MaintenanceChangeRules.AddChange(detailChanges, "стоимость", MaintenanceChangeRules.FormatNullableMoney(previous.Price), MaintenanceChangeRules.FormatNullableMoney(current.Price));
            MaintenanceChangeRules.AddChange(detailChanges, "НДС", previous.VatRateCode, current.VatRateCode);
            MaintenanceChangeRules.AddChange(detailChanges, "материал", previous.MaterialName, current.MaterialName);
            MaintenanceChangeRules.AddChange(detailChanges, "единица расхода", previous.MaterialUnit, current.MaterialUnit);
            MaintenanceChangeRules.AddChange(detailChanges, "себестоимость расхода", MaintenanceChangeRules.FormatNullableDecimal(previous.MaterialTotalCost), MaintenanceChangeRules.FormatNullableDecimal(current.MaterialTotalCost));
            MaintenanceChangeRules.AddChange(detailChanges, "расход", MaintenanceChangeRules.FormatNullableDecimal(previous.MaterialQuantity), MaintenanceChangeRules.FormatNullableDecimal(current.MaterialQuantity));
            MaintenanceChangeRules.AddChange(detailChanges, "себестоимость материала", MaintenanceChangeRules.FormatNullableDecimal(previous.MaterialUnitCost), MaintenanceChangeRules.FormatNullableDecimal(current.MaterialUnitCost));

            changes.Add($"Изменена услуга #{index + 1}: {string.Join("; ", detailChanges)}");
        }

        for (var index = maxCommonCount; index < currentDetails.Count; index++)
        {
            changes.Add($"Добавлена услуга: {MaintenanceChangeRules.FormatDetail(currentDetails[index])}");
        }

        for (var index = maxCommonCount; index < previousDetails.Count; index++)
        {
            changes.Add($"Удалена услуга: {MaintenanceChangeRules.FormatDetail(previousDetails[index])}");
        }

        return changes;
    }

    /// <summary> Добавляет описание изменения поля, если значение изменилось. </summary>
    internal static void AddChange(List<string> changes, string fieldName, string? previousValue, string? currentValue)
    {
        var previousDisplay = MaintenanceChangeRules.FormatNullableText(previousValue);
        var currentDisplay = MaintenanceChangeRules.FormatNullableText(currentValue);
        if (string.Equals(previousDisplay, currentDisplay, StringComparison.Ordinal))
        {
            return;
        }

        changes.Add($"{fieldName}: {previousDisplay} -> {currentDisplay}");
    }

    /// <summary> Определяет, совпадают ли прежняя и текущая строки обслуживания. </summary>
    internal static bool AreSameDetail(MaintenanceDetailSnapshot previous, MaintenanceDetail current)
    {
        return previous.DetailId == current.DetailId
            && previous.EmployeeId == current.EmployeeId
            && previous.EmployeeName == current.EmployeeName
            && previous.CatalogItemId == current.CatalogItemId
            && previous.MaterialCatalogItemId == current.MaterialCatalogItemId
            && string.Equals(previous.MaterialUnit, current.MaterialUnit, StringComparison.Ordinal)
            && Nullable.Equals(previous.MaterialTotalCost, current.MaterialTotalCost)
            && string.Equals(MaintenanceChangeRules.FormatNullableText(previous.Name), MaintenanceChangeRules.FormatNullableText(current.Name), StringComparison.Ordinal)
            && string.Equals(MaintenanceChangeRules.FormatNullableText(previous.Description), MaintenanceChangeRules.FormatNullableText(current.Description), StringComparison.Ordinal)
            && previous.Quantity == current.Quantity
            && Nullable.Equals(previous.Price, current.Price)
            && previous.VatRateCode == current.VatRateCode
            && string.Equals(MaintenanceChangeRules.FormatNullableText(previous.MaterialName), MaintenanceChangeRules.FormatNullableText(current.MaterialName), StringComparison.Ordinal)
            && Nullable.Equals(previous.MaterialQuantity, current.MaterialQuantity)
            && Nullable.Equals(previous.MaterialUnitCost, current.MaterialUnitCost);
    }

    /// <summary> Формирует текстовое представление снимка строки обслуживания. </summary>
    internal static string FormatDetail(MaintenanceDetailSnapshot detail)
    {
        return MaintenanceChangeRules.FormatDetail(detail.Name, detail.Description, detail.Price, detail.VatRateCode, detail.Quantity,
            detail.CatalogItemId, detail.MaterialCatalogItemId, detail.MaterialName, detail.MaterialQuantity,
            detail.MaterialUnit, detail.MaterialUnitCost, detail.MaterialTotalCost)
            + (detail.EmployeeId.HasValue ? $"; сотрудник: {MaintenanceChangeRules.FormatEmployee(detail.EmployeeId, detail.EmployeeName)}" : "");
    }

    /// <summary> Формирует текстовое представление строки обслуживания. </summary>
    internal static string FormatDetail(MaintenanceDetail detail)
    {
        return MaintenanceChangeRules.FormatDetail(detail.Name, detail.Description, detail.Price, detail.VatRateCode, detail.Quantity,
            detail.CatalogItemId, detail.MaterialCatalogItemId, detail.MaterialName, detail.MaterialQuantity,
            detail.MaterialUnit, detail.MaterialUnitCost, detail.MaterialTotalCost)
            + (detail.EmployeeId.HasValue ? $"; сотрудник: {MaintenanceChangeRules.FormatEmployee(detail.EmployeeId, detail.EmployeeName)}" : "");
    }

    /// <summary> Формирует текстовое представление строки по её полям, включая материал. </summary>
    internal static string FormatDetail(
        string? name,
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
        var parts = new List<string> { MaintenanceChangeRules.FormatNullableText(name), $"количество: {quantity}" };
        if (!string.IsNullOrWhiteSpace(description))
        {
            parts.Add($"описание: {description.Trim()}");
        }

        if (price.HasValue)
        {
            parts.Add($"стоимость: {MaintenanceChangeRules.FormatNullableMoney(price)}");
        }

        if (vatRateCode != null)
        {
            parts.Add($"НДС: {vatRateCode}");
        }

        if (catalogItemId.HasValue)
        {
            parts.Add($"услуга из справочника: {catalogItemId}");
        }

        if (materialCatalogItemId.HasValue || !string.IsNullOrWhiteSpace(materialName) || materialQuantity.HasValue)
        {
            parts.Add($"материал: {MaintenanceChangeRules.FormatNullableText(materialName)}; справочник: {materialCatalogItemId}");
            parts.Add($"расход: {MaintenanceChangeRules.FormatNullableDecimal(materialQuantity)} {materialUnit}");
            parts.Add($"себестоимость единицы: {MaintenanceChangeRules.FormatNullableDecimal(materialUnitCost)}; всего: {MaintenanceChangeRules.FormatNullableDecimal(materialTotalCost)}");
        }

        return string.Join("; ", parts);
    }

    /// <summary> Форматирует денежное значение с двумя знаками после разделителя. </summary>
    internal static string FormatDecimalMoney(decimal value) => $"{value:0.##} ₽";

    /// <summary> Форматирует необязательное денежное значение или прочерк. </summary>
    internal static string? FormatNullableDecimal(decimal? value) => value.HasValue ? value.Value.ToString("0.###") : null;

    /// <summary> Форматирует дату или возвращает прочерк. </summary>
    internal static string FormatDate(DateTime? value)
    {
        var date = MaintenanceDateRules.NormalizeMaintenanceDate(value);
        return date.HasValue ? date.Value.ToString("dd.MM.yyyy") : "не указано";
    }

    /// <summary> Форматирует время или возвращает прочерк. </summary>
    internal static string FormatTime(TimeSpan? value) => value.HasValue ? value.Value.ToString(@"hh\:mm") : "не указано";

    /// <summary> Форматирует необязательное целое число или прочерк. </summary>
    internal static string FormatNullableNumber(int? value) => value.HasValue ? value.Value.ToString() : "не указано";

    /// <summary> Форматирует необязательную денежную сумму или прочерк. </summary>
    internal static string FormatNullableMoney(decimal? value) => value.HasValue ? value.Value.ToString("0.##") : "не указано";

    /// <summary> Возвращает текст или прочерк для пустого значения. </summary>
    internal static string FormatNullableText(string? value) => string.IsNullOrWhiteSpace(value) ? "не указано" : value.Trim();

    /// <summary> Формирует краткое описание обслуживания для уведомлений и истории. </summary>
    internal static string BuildMaintenanceSummary(MaintenanceEntity maintenance)
    {
        var dateLabel = maintenance.Status == MaintenanceStatus.Completed ? "Дата выполнения" : "Плановая дата";
        var dateValue = maintenance.Status == MaintenanceStatus.Completed
            ? maintenance.CompletedDate ?? maintenance.Date
            : maintenance.PlannedDate ?? maintenance.Date;
        var parts = new List<string>
        {
            $"Сотрудник: {MaintenanceChangeRules.FormatEmployee(maintenance.EmployeeId, maintenance.Performer) ?? "Не назначен"}",
            $"Тип: {MaintenanceChangeRules.GetMaintenanceTypeText(maintenance.Type)}",
            $"Состояние: {MaintenanceChangeRules.GetMaintenanceStatusText(maintenance.Status)}",
            $"{dateLabel}: {dateValue:dd.MM.yyyy}"
        };

        if (maintenance.PlannedStartTime.HasValue || maintenance.PlannedEndTime.HasValue)
        {
            parts.Add($"Плановая дата окончания: {MaintenanceChangeRules.FormatDate(maintenance.PlannedEndDate ?? maintenance.PlannedDate)}");
            parts.Add($"Плановое время: {MaintenanceChangeRules.FormatTime(maintenance.PlannedStartTime)} - {MaintenanceChangeRules.FormatTime(maintenance.PlannedEndTime)}");
        }

        if (maintenance.Mileage.HasValue)
        {
            parts.Add($"Пробег/наработка: {maintenance.Mileage.Value}");
        }

        return string.Join("; ", parts);
    }

    /// <summary> Возвращает русское название статуса обслуживания. </summary>
    internal static string GetMaintenanceStatusText(MaintenanceStatus status) => status switch
    {
        MaintenanceStatus.Planned => "Запланировано",
        MaintenanceStatus.InProgress => "В работе",
        MaintenanceStatus.Completed => "Выполнено",
        MaintenanceStatus.Cancelled => "Отменено",
        _ => "Неизвестно"
    };

    /// <summary> Возвращает русское название вида обслуживания. </summary>
    internal static string GetMaintenanceTypeText(MaintenanceType type) => type switch
    {
        MaintenanceType.Repair => "Ремонт",
        MaintenanceType.Maintenance => "Ремонт и обслуживание",
        MaintenanceType.Refill => "Заправка топливом",
        MaintenanceType.Other => "Другое",
        _ => "Обслуживание"
    };

    /// <summary> Формирует представление сотрудника по идентификатору и имени. </summary>
    internal static string? FormatEmployee(Guid? employeeId, string? name) => employeeId.HasValue ? $"{name} [{employeeId}]" : name;
}
