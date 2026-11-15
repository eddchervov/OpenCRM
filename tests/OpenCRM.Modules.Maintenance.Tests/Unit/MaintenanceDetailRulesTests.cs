using OpenCRM.Domain;
using OpenCRM.Domain.Materials;
using OpenCRM.Modules.Maintenance;
using OpenCRM.Modules.Maintenance.Contracts.Dtos;

namespace OpenCRM.Modules.Maintenance.Tests.Unit;

public sealed class MaintenanceDetailRulesTests
{
    [Fact]
    public void MapDetailsRejectsNullAndEmptyCollectionsForBothContracts()
    {
        Assert.Throws<InvalidOperationException>(() => MaintenanceDetailRules.MapDetails(
            (IEnumerable<CreateMaintenanceDetailDto>)null!));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDetailRules.MapDetails(
            Array.Empty<CreateMaintenanceDetailDto>()));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDetailRules.MapDetails(
            (IEnumerable<UpdateMaintenanceDetailDto>)null!));
        Assert.Throws<InvalidOperationException>(() => MaintenanceDetailRules.MapDetails(
            Array.Empty<UpdateMaintenanceDetailDto>()));
    }

    [Fact]
    public void MapDetailsMapsCreateAndUpdateInputsThroughTheSameNormalization()
    {
        var created = MaintenanceDetailRules.MapDetails(new[]
        {
            new CreateMaintenanceDetailDto
            {
                Name = "  Замена фильтра ",
                Description = "  Передний   отсек ",
                Price = 125.5m,
                Quantity = 2
            }
        });
        var updated = MaintenanceDetailRules.MapDetails(new[]
        {
            new UpdateMaintenanceDetailDto
            {
                Name = "  Замена фильтра ",
                Description = "  Передний   отсек ",
                Price = 125.5m,
                Quantity = 2
            }
        });

        var createdDetail = Assert.Single(created);
        var updatedDetail = Assert.Single(updated);
        Assert.Equal("Замена фильтра", createdDetail.Name);
        Assert.Equal("Передний отсек", createdDetail.Description);
        Assert.Equal(createdDetail.Name, updatedDetail.Name);
        Assert.Equal(createdDetail.Description, updatedDetail.Description);
        Assert.Equal(2, createdDetail.Quantity);
    }

    [Fact]
    public void AssignDetailIdsReusesOneMatchingPreviousRowAndGeneratesIdsForAmbiguousMatches()
    {
        var catalogItemId = Guid.NewGuid();
        var previousId = Guid.NewGuid();
        var previous = new[]
        {
            new MaintenanceDetail { DetailId = previousId, CatalogItemId = catalogItemId, Name = "Замена масла" }
        };
        var matching = new MaintenanceDetail { DetailId = Guid.Empty, CatalogItemId = catalogItemId, Name = "Замена масла" };
        var additional = new MaintenanceDetail { DetailId = Guid.Empty, CatalogItemId = catalogItemId, Name = "Замена масла" };

        MaintenanceDetailRules.AssignDetailIds([matching, additional], previous);

        Assert.Equal(previousId, matching.DetailId);
        Assert.NotEqual(Guid.Empty, additional.DetailId);
        Assert.NotEqual(previousId, additional.DetailId);

        var ambiguousPrevious = new[]
        {
            new MaintenanceDetail { DetailId = Guid.NewGuid(), CatalogItemId = catalogItemId, Name = "Одинаково" },
            new MaintenanceDetail { DetailId = Guid.NewGuid(), CatalogItemId = catalogItemId, Name = "Одинаково" }
        };
        var ambiguous = new MaintenanceDetail { DetailId = Guid.Empty, CatalogItemId = catalogItemId, Name = "Одинаково" };

        MaintenanceDetailRules.AssignDetailIds([ambiguous], ambiguousPrevious);

        Assert.NotEqual(Guid.Empty, ambiguous.DetailId);
        Assert.DoesNotContain(ambiguousPrevious, detail => detail.DetailId == ambiguous.DetailId);
    }

    [Fact]
    public void AssignDetailIdsGeneratesIdentifierWhenThereIsNoPreviousMatch()
    {
        var detail = new MaintenanceDetail { DetailId = Guid.Empty, Name = "Новая работа" };

        MaintenanceDetailRules.AssignDetailIds([detail], [new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Другая работа"
        }]);

        Assert.NotEqual(Guid.Empty, detail.DetailId);
    }

    [Fact]
    public void AssignDetailIdsRejectsForeignAndRepeatedIdentifiers()
    {
        var foreign = new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа" };
        Assert.Throws<InvalidOperationException>(() => MaintenanceDetailRules.AssignDetailIds([foreign]));

        var existingId = Guid.NewGuid();
        var previous = new[] { new MaintenanceDetail { DetailId = existingId, Name = "Работа" } };
        var repeated = new[]
        {
            new MaintenanceDetail { DetailId = existingId, Name = "Работа" },
            new MaintenanceDetail { DetailId = existingId, Name = "Работа" }
        };

        Assert.Throws<InvalidOperationException>(() => MaintenanceDetailRules.AssignDetailIds(repeated, previous));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AssignDetailIdsDoesNotReuseARowAfterItsMaterialReferenceChanges(
        bool previousHasMaterial, bool nextHasMaterial)
    {
        var previous = new MaintenanceDetail
        {
            DetailId = Guid.NewGuid(),
            Name = "Материал",
            MaterialCatalogItemId = previousHasMaterial ? Guid.NewGuid() : null
        };
        var next = new MaintenanceDetail
        {
            DetailId = Guid.Empty,
            Name = previous.Name,
            MaterialCatalogItemId = nextHasMaterial ? Guid.NewGuid() : null
        };

        MaintenanceDetailRules.AssignDetailIds([next], [previous]);

        Assert.NotEqual(Guid.Empty, next.DetailId);
        Assert.NotEqual(previous.DetailId, next.DetailId);
    }

    [Fact]
    public void MapDetailTrimsTextNormalizesMoneyAndCanonicalizesMaterialUnit()
    {
        var detail = MaintenanceDetailRules.MapDetail(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Промывка системы ",
            "  Безопасная   работа ",
            1200.5m,
            MaintenanceVatRates.WithoutVat,
            2,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "  Омывающая жидкость ",
            1.25m,
            " Л ",
            2.345m,
            3.456m);

        Assert.Equal("Промывка системы", detail.Name);
        Assert.Equal("Безопасная работа", detail.Description);
        Assert.Equal("Омывающая жидкость", detail.MaterialName);
        Assert.Equal("л", detail.MaterialUnit);
        Assert.Equal(2.35m, detail.MaterialUnitCost);
        Assert.Equal(3.46m, detail.MaterialTotalCost);
        Assert.Equal(1200.5m, detail.Price);
    }

    [Fact]
    public void MapDetailLeavesMaterialUnitNullWhenThereIsNoMaterialAndDefaultsItForMaterial()
    {
        var service = MapDetail(materialName: null, materialQuantity: null, materialUnit: "л");
        var material = MapDetail(materialName: "Масло", materialQuantity: 1, materialUnit: null);

        Assert.Null(service.MaterialUnit);
        Assert.Equal("шт", material.MaterialUnit);
        Assert.Equal(0m, material.MaterialUnitCost);
        Assert.Equal(0m, material.MaterialTotalCost);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MapDetailRequiresAName(string? name)
    {
        Assert.Throws<InvalidOperationException>(() => MaintenanceDetailRules.MapDetail(
            null, null, name!, null, null, null, 1, null, null, null, null, null, null, null));
    }

    [Fact]
    public void MapDetailRejectsInvalidQuantityPriceVatAndMaterialAmounts()
    {
        Assert.Throws<InvalidOperationException>(() => MapDetail(quantity: 0));
        Assert.Throws<InvalidOperationException>(() => MapDetail(quantity: 10001));
        Assert.Throws<InvalidOperationException>(() => MapDetail(price: -1));
        Assert.Throws<InvalidOperationException>(() => MapDetail(price: 1.001m));
        Assert.Throws<InvalidOperationException>(() => MapDetail(vatRateCode: "unknown"));
        Assert.Throws<InvalidOperationException>(() => MapDetail(materialQuantity: -1));
        Assert.Throws<InvalidOperationException>(() =>
            MapDetail(materialQuantity: MaintenanceDetailRules.MaxMaterialQuantity + 1));
        Assert.Throws<InvalidOperationException>(() => MapDetail(materialName: "Масло", materialUnit: "ведро"));
    }

    private static MaintenanceDetail MapDetail(
        string? name = "Работа",
        int quantity = 1,
        decimal? price = null,
        string? vatRateCode = null,
        string? materialName = null,
        decimal? materialQuantity = null,
        string? materialUnit = null) => MaintenanceDetailRules.MapDetail(
            null,
            null,
            name!,
            null,
            price,
            vatRateCode,
            quantity,
            null,
            null,
            materialName,
            materialQuantity,
            materialUnit,
            null,
            null);
}
