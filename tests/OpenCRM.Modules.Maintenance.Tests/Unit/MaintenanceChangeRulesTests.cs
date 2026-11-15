using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Modules.Maintenance;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceChangeRulesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetailIdentityAndVatChangesHaveDescriptions(bool changeIdentity)
    {
        var previous = CreateDetailSnapshot();
        var current = CreateDetail(previous);
        if (changeIdentity)
        {
            current.DetailId = Guid.NewGuid();
        }
        else
        {
            current.VatRateCode = "vat20";
        }

        var description = Assert.Single(MaintenanceChangeRules.BuildDetailChanges([previous], [current]));

        Assert.Contains(changeIdentity ? "идентификатор строки:" : "НДС:", description);
        Assert.Contains(" -> ", description);
    }

    [Fact]
    public void AddChangeComparesDisplayedValuesAndIgnoresEquivalentEmptyOrTrimmedText()
    {
        var changes = new List<string>();

        MaintenanceChangeRules.AddChange(changes, "Место", null, "   ");
        MaintenanceChangeRules.AddChange(changes, "Комментарий", " пост 1 ", "пост 1");
        MaintenanceChangeRules.AddChange(changes, "Пробег", "1", "2");

        Assert.Equal(new[] { "Пробег: 1 -> 2" }, changes);
    }

    [Fact]
    public void HasMaintenanceChangesDetectsStatusFieldAndDetailDifferences()
    {
        var maintenance = CreateMaintenance();
        var baseline = MaintenanceSnapshot.From(maintenance);

        Assert.False(MaintenanceChangeRules.HasMaintenanceChanges(baseline, maintenance));

        maintenance.Comment = "Изменение";
        Assert.True(MaintenanceChangeRules.HasMaintenanceChanges(baseline, maintenance));

        maintenance.Comment = null;
        maintenance.Status = MaintenanceStatus.InProgress;
        Assert.True(MaintenanceChangeRules.HasMaintenanceChanges(baseline, maintenance));

        maintenance.Status = baseline.Status;
        maintenance.Details[0].Quantity++;
        Assert.True(MaintenanceChangeRules.HasMaintenanceChanges(baseline, maintenance));
    }

    [Fact]
    public void BuildDetailChangesReportsEditsAdditionsAndRemovalsInOrder()
    {
        var previous = new[] { CreateDetailSnapshot(name: "Старая работа", materialName: null) };
        var current = new[]
        {
            new MaintenanceDetail
            {
                DetailId = previous[0].DetailId,
                Name = "Новая работа",
                Description = "Капот",
                Price = 1200,
                Quantity = 2,
                MaterialName = "Полироль",
                MaterialQuantity = 0.5m,
                MaterialUnit = "л",
                MaterialUnitCost = 300,
                MaterialTotalCost = 150
            },
            new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Добавленная работа" }
        };

        var changes = MaintenanceChangeRules.BuildDetailChanges(previous, current);

        Assert.Equal(2, changes.Count);
        Assert.StartsWith("Изменена услуга #1:", changes[0]);
        Assert.Contains("название: Старая работа -> Новая работа", changes[0]);
        Assert.Contains("материал: не указано -> Полироль", changes[0]);
        Assert.StartsWith("Добавлена услуга: Добавленная работа", changes[1]);

        var removed = MaintenanceChangeRules.BuildDetailChanges(previous, []);
        Assert.Single(removed);
        Assert.StartsWith("Удалена услуга: Старая работа", removed[0]);
    }

    [Theory]
    [InlineData("DetailId")]
    [InlineData("EmployeeId")]
    [InlineData("EmployeeName")]
    [InlineData("CatalogItemId")]
    [InlineData("MaterialCatalogItemId")]
    [InlineData("MaterialUnit")]
    [InlineData("MaterialTotalCost")]
    [InlineData("Name")]
    [InlineData("Description")]
    [InlineData("Quantity")]
    [InlineData("Price")]
    [InlineData("VatRateCode")]
    [InlineData("MaterialName")]
    [InlineData("MaterialQuantity")]
    [InlineData("MaterialUnitCost")]
    public void AreSameDetailDetectsEveryChangedSnapshotProperty(string propertyName)
    {
        var previous = CreateDetailSnapshot();
        var current = CreateDetail(previous);
        switch (propertyName)
        {
            case "DetailId":
                current.DetailId = Guid.NewGuid();
                break;
            case "EmployeeId":
                current.EmployeeId = Guid.NewGuid();
                break;
            case "EmployeeName":
                current.EmployeeName = "Другой сотрудник";
                break;
            case "CatalogItemId":
                current.CatalogItemId = Guid.NewGuid();
                break;
            case "MaterialCatalogItemId":
                current.MaterialCatalogItemId = Guid.NewGuid();
                break;
            case "MaterialUnit":
                current.MaterialUnit = "шт";
                break;
            case "MaterialTotalCost":
                current.MaterialTotalCost = 11;
                break;
            case "Name":
                current.Name = "Другая работа";
                break;
            case "Description":
                current.Description = "Другой участок";
                break;
            case "Quantity":
                current.Quantity++;
                break;
            case "Price":
                current.Price = 11;
                break;
            case "VatRateCode":
                current.VatRateCode = "vat20";
                break;
            case "MaterialName":
                current.MaterialName = "Другой материал";
                break;
            case "MaterialQuantity":
                current.MaterialQuantity = 11;
                break;
            case "MaterialUnitCost":
                current.MaterialUnitCost = 11;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(propertyName));
        }

        Assert.False(MaintenanceChangeRules.AreSameDetail(previous, current));
    }

    [Fact]
    public void AreSameDetailTreatsDisplayWhitespaceAsEquivalent()
    {
        var previous = CreateDetailSnapshot(name: " Работа ", description: " участок ", materialName: " масло ");
        var current = CreateDetail(previous);
        current.Name = "Работа";
        current.Description = "участок";
        current.MaterialName = "масло";

        Assert.True(MaintenanceChangeRules.AreSameDetail(previous, current));
    }

    [Fact]
    public void FormatDetailAddsOptionalCatalogMaterialAndEmployeeInformation()
    {
        var detail = new MaintenanceDetail
        {
            Name = "  Полировка ",
            Description = " Капот ",
            Price = 1200,
            VatRateCode = "vat20",
            Quantity = 2,
            CatalogItemId = Guid.NewGuid(),
            MaterialCatalogItemId = Guid.NewGuid(),
            MaterialName = " Полироль ",
            MaterialQuantity = 0.5m,
            MaterialUnit = "л",
            MaterialUnitCost = 300,
            MaterialTotalCost = 150,
            EmployeeId = Guid.NewGuid(),
            EmployeeName = "Мастер"
        };

        var text = MaintenanceChangeRules.FormatDetail(detail);

        Assert.Contains("Полировка", text);
        Assert.Contains("описание: Капот", text);
        Assert.Contains("стоимость: 1200", text);
        Assert.Contains("НДС: vat20", text);
        Assert.Contains($"услуга из справочника: {detail.CatalogItemId}", text);
        Assert.Contains("материал: Полироль", text);
        Assert.Contains($"расход: {0.5m:0.###} л", text);
        Assert.Contains($"себестоимость единицы: {300m:0.###}; всего: {150m:0.###}", text);
        Assert.Contains($"сотрудник: Мастер [{detail.EmployeeId}]", text);
    }

    [Fact]
    public void FormatDetailOmitsOptionalFieldsWhenTheyAreNotSet()
    {
        var text = MaintenanceChangeRules.FormatDetail(new MaintenanceDetail
        {
            Name = "Работа",
            Quantity = 1,
            Description = "   "
        });

        Assert.Equal("Работа; количество: 1", text);
    }

    [Fact]
    public void FormatDetailSnapshotIncludesAssignedEmployee()
    {
        var snapshot = CreateDetailSnapshot();

        var text = MaintenanceChangeRules.FormatDetail(snapshot);

        Assert.Contains($"сотрудник: {snapshot.EmployeeName} [{snapshot.EmployeeId}]", text);
    }

    [Fact]
    public void FormatMaintenanceSummarySelectsDateAndOptionalScheduleMileageAndEmployee()
    {
        var plannedDate = new DateTime(2026, 10, 11);
        var maintenance = CreateMaintenance();
        maintenance.PlannedDate = plannedDate;
        maintenance.PlannedStartTime = TimeSpan.FromHours(9);
        maintenance.PlannedEndTime = TimeSpan.FromHours(10);
        maintenance.PlannedEndDate = plannedDate;
        maintenance.Mileage = 123;
        maintenance.EmployeeId = Guid.NewGuid();
        maintenance.Performer = "Мастер";

        var planned = MaintenanceChangeRules.BuildMaintenanceSummary(maintenance);
        maintenance.Status = MaintenanceStatus.Completed;
        maintenance.CompletedDate = plannedDate.AddDays(1);
        var completed = MaintenanceChangeRules.BuildMaintenanceSummary(maintenance);

        Assert.Contains("Сотрудник: Мастер", planned);
        Assert.Contains("Плановая дата: 11.10.2026", planned);
        Assert.Contains("Плановая дата окончания: 11.10.2026", planned);
        Assert.Contains("Плановое время: 09:00 - 10:00", planned);
        Assert.Contains("Пробег/наработка: 123", planned);
        Assert.Contains("Дата выполнения: 12.10.2026", completed);
    }

    [Fact]
    public void FormatMaintenanceSummaryOmitsUnspecifiedScheduleMileageAndEmployee()
    {
        var summary = MaintenanceChangeRules.BuildMaintenanceSummary(CreateMaintenance());

        Assert.Contains("Сотрудник: Не назначен", summary);
        Assert.DoesNotContain("Плановое время:", summary);
        Assert.DoesNotContain("Пробег/наработка:", summary);
    }

    [Fact]
    public void FormatMaintenanceSummaryUsesPlannedDateWhenScheduleHasNoEndDate()
    {
        var maintenance = CreateMaintenance();
        maintenance.PlannedStartTime = TimeSpan.FromHours(9);

        var summary = MaintenanceChangeRules.BuildMaintenanceSummary(maintenance);

        Assert.Contains("Плановая дата окончания: не указано", summary);
        Assert.Contains("Плановое время: 09:00 - не указано", summary);
    }

    [Fact]
    public void FormatHelpersCoverKnownUnknownAndNullableValues()
    {
        Assert.Equal("не указано", MaintenanceChangeRules.FormatDate(null));
        Assert.Equal("11.10.2026", MaintenanceChangeRules.FormatDate(new DateTime(2026, 10, 11)));
        Assert.Equal("не указано", MaintenanceChangeRules.FormatTime(null));
        Assert.Equal("09:30", MaintenanceChangeRules.FormatTime(TimeSpan.FromMinutes(570)));
        Assert.Equal("не указано", MaintenanceChangeRules.FormatNullableNumber(null));
        Assert.Equal("123", MaintenanceChangeRules.FormatNullableNumber(123));
        Assert.Equal("не указано", MaintenanceChangeRules.FormatNullableMoney(null));
        Assert.Equal(12.5.ToString("0.##"), MaintenanceChangeRules.FormatNullableMoney(12.5m));
        Assert.Equal("не указано", MaintenanceChangeRules.FormatNullableText("  "));
        Assert.Equal("работа", MaintenanceChangeRules.FormatNullableText(" работа "));
        Assert.Equal($"{12.5m:0.##} ₽", MaintenanceChangeRules.FormatDecimalMoney(12.5m));
        Assert.Null(MaintenanceChangeRules.FormatNullableDecimal(null));
        Assert.Equal(1.235m.ToString("0.###"), MaintenanceChangeRules.FormatNullableDecimal(1.2345m));
        Assert.Equal("Мастер", MaintenanceChangeRules.FormatEmployee(null, "Мастер"));
        Assert.Equal("Неизвестно", MaintenanceChangeRules.GetMaintenanceStatusText((MaintenanceStatus)999));
        Assert.Equal("Обслуживание", MaintenanceChangeRules.GetMaintenanceTypeText((MaintenanceType)999));
    }

    [Theory]
    [InlineData(MaintenanceStatus.Planned, "Запланировано")]
    [InlineData(MaintenanceStatus.InProgress, "В работе")]
    [InlineData(MaintenanceStatus.Completed, "Выполнено")]
    [InlineData(MaintenanceStatus.Cancelled, "Отменено")]
    public void GetMaintenanceStatusTextMapsEveryStatus(MaintenanceStatus status, string expected)
    {
        Assert.Equal(expected, MaintenanceChangeRules.GetMaintenanceStatusText(status));
    }

    [Theory]
    [InlineData(MaintenanceType.Repair, "Ремонт")]
    [InlineData(MaintenanceType.Maintenance, "Ремонт и обслуживание")]
    [InlineData(MaintenanceType.Refill, "Заправка топливом")]
    [InlineData(MaintenanceType.Other, "Другое")]
    public void GetMaintenanceTypeTextMapsEverySupportedType(MaintenanceType type, string expected)
    {
        Assert.Equal(expected, MaintenanceChangeRules.GetMaintenanceTypeText(type));
    }

    private static MaintenanceEntity CreateMaintenance() => new()
    {
        Status = MaintenanceStatus.Planned,
        Type = MaintenanceType.Repair,
        Date = new DateTime(2026, 10, 11),
        Details = [new MaintenanceDetail { Name = "Работа", Quantity = 1 }]
    };

    private static MaintenanceDetailSnapshot CreateDetailSnapshot(
        string name = "Работа", string description = "Капот", string? materialName = "Полироль") => new(
        Guid.NewGuid(),
        Guid.NewGuid(),
        "Мастер",
        Guid.NewGuid(),
        name,
        description,
        10,
        "withoutVat",
        2,
        Guid.NewGuid(),
        "л",
        10,
        materialName,
        1,
        10);

    private static MaintenanceDetail CreateDetail(MaintenanceDetailSnapshot snapshot) => new()
    {
        DetailId = snapshot.DetailId,
        EmployeeId = snapshot.EmployeeId,
        EmployeeName = snapshot.EmployeeName,
        CatalogItemId = snapshot.CatalogItemId,
        MaterialCatalogItemId = snapshot.MaterialCatalogItemId,
        MaterialUnit = snapshot.MaterialUnit,
        MaterialTotalCost = snapshot.MaterialTotalCost,
        Name = snapshot.Name!,
        Description = snapshot.Description,
        Quantity = snapshot.Quantity,
        Price = snapshot.Price,
        VatRateCode = snapshot.VatRateCode,
        MaterialName = snapshot.MaterialName,
        MaterialQuantity = snapshot.MaterialQuantity,
        MaterialUnitCost = snapshot.MaterialUnitCost
    };
}
