using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.DependencyInjection;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Services.Interfaces.Organizations;
using OpenCRM.Modules.Inventory;
using OpenCRM.Modules.Inventory.Contracts;
using OpenCRM.Modules.Inventory.Contracts.Dtos;

namespace OpenCRM.Modules.Inventory.Tests.Integration;

public sealed class MaterialCostTests(InventoryDatabaseFixture fixture) : IClassFixture<InventoryDatabaseFixture>
{
    [Fact]
    public async Task OperationIssueConvertsUnitsAndCanBeReturnedOnce()
    {
        await using var context = fixture.CreateContext();
        var worker = new ApplicationUser();
        var otherWorker = new ApplicationUser();
        var member = new OrganizationMember { User = worker, RoleCode = OrganizationRoleCodes.Performer };
        var otherMember = new OrganizationMember { User = otherWorker, RoleCode = OrganizationRoleCodes.Performer };
        var organization = new Organization
        {
            Name = "Проверка расхода мастера", OwnerUser = worker,
            Members = [member, otherMember]
        };
        var employee = new OrganizationEmployee { Organization = organization, Member = member, Name = "Мастер" };
        var otherEmployee = new OrganizationEmployee { Organization = organization, Member = otherMember, Name = "Другой мастер" };
        var vehicle = new Vehicle { User = worker, Organization = organization };
        var detailId = Guid.NewGuid();
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            User = worker, Vehicle = vehicle, Status = MaintenanceStatus.InProgress,
            Date = DateTime.UtcNow, Details = [new MaintenanceDetail
            {
                DetailId = detailId, Name = "Полировка", EmployeeId = employee.Id
            }]
        };
        context.OrganizationEmployees.AddRange(employee, otherEmployee);
        context.Maintenance.Add(maintenance);
        context.MaintenanceWorkOperations.Add(new MaintenanceWorkOperation
        {
            Id = detailId, Maintenance = maintenance, OrganizationId = organization.Id,
            EmployeeId = employee.Id, Status = MaintenanceWorkStatus.InProgress
        });
        await context.SaveChangesAsync();

        using var inventory = CreateInventoryScope(context);
        var stockCommands = inventory.StockCommands;
        var operationCommands = inventory.OperationCommands;
        var stock = await stockCommands.AddReceiptAsync(worker.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Паста", Unit = "кг", Quantity = 3, UnitCost = 1000, PackageQuantity = 600
        });
        var command = new IssueOperationMaterialDto
        {
            StockId = stock.Id, Quantity = 0.5m, Unit = "уп", CommandId = Guid.NewGuid()
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.IssueForOperationAsync(
            worker.Id, organization.Id, maintenance.Id, detailId,
            new IssueOperationMaterialDto { StockId = stock.Id, Quantity = 0.0005m, Unit = "уп", CommandId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => operationCommands.IssueForOperationAsync(
            otherWorker.Id, organization.Id, maintenance.Id, detailId, command));
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.IssueForOperationAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            new IssueOperationMaterialDto { StockId = stock.Id, Quantity = 1_000_000_000m, Unit = "уп", CommandId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.IssueForOperationAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            new IssueOperationMaterialDto { StockId = stock.Id, Quantity = 2_000_000, Unit = "уп", CommandId = Guid.NewGuid() }));
        var issue = await operationCommands.IssueForOperationAsync(worker.Id, organization.Id, maintenance.Id, detailId, command);
        var repeated = await operationCommands.IssueForOperationAsync(worker.Id, organization.Id, maintenance.Id, detailId, command);
        Assert.Equal(issue.Id, repeated.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.IssueForOperationAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            new IssueOperationMaterialDto
            {
                StockId = stock.Id,
                Quantity = 0.25m,
                Unit = "уп",
                CommandId = command.CommandId
            }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.IssueForOperationAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            new IssueOperationMaterialDto
            {
                StockId = stock.Id,
                Quantity = 5,
                Unit = "уп",
                CommandId = Guid.NewGuid()
            }));
        var movement = await context.OrganizationMaterialMovements.SingleAsync(item => item.Id == issue.Id);
        Assert.Equal(-300m, movement.Quantity);
        Assert.Equal(0.5m, movement.EnteredQuantity);
        Assert.Equal("уп", movement.EnteredUnit);
        Assert.Equal(600m, movement.ConversionFactor);
        Assert.Equal(300m, movement.TotalCost);
        Assert.Equal(300m, (await context.Maintenance.SingleAsync(item => item.Id == maintenance.Id)).MaterialCostAmount);
        var operationConsumption = await inventory.Consumption.SynchronizeAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            [new MaintenanceMaterialConsumptionLine(detailId, null, "Полировка", null, null, null)]);
        Assert.Equal(300m, operationConsumption.TotalCost);
        var operationCost = Assert.Single(operationConsumption.Details);
        Assert.Null(operationCost.UnitCost);
        Assert.Equal(300m, operationCost.TotalCost);

        var returnCommand = new ReverseOperationMaterialDto
        {
            CommandId = Guid.NewGuid(), Reason = "Не использовали"
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.ReverseOperationMaterialAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            issue.Id,
            new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = string.Empty }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.ReverseOperationMaterialAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            issue.Id,
            new ReverseOperationMaterialDto { CommandId = Guid.NewGuid(), Reason = new string('x', 1001) }));
        var returned = await operationCommands.ReverseOperationMaterialAsync(
            worker.Id, organization.Id, maintenance.Id, detailId, issue.Id, returnCommand);
        var repeatedReturn = await operationCommands.ReverseOperationMaterialAsync(
            worker.Id, organization.Id, maintenance.Id, detailId, issue.Id, returnCommand);
        Assert.Equal(returned.Id, repeatedReturn.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => operationCommands.ReverseOperationMaterialAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            detailId,
            issue.Id,
            new ReverseOperationMaterialDto
            {
                CommandId = returnCommand.CommandId,
                Reason = "Другая причина"
            }));
        Assert.True(returned.IsReturn);
        Assert.Equal(3000m, (await context.OrganizationMaterialStocks.SingleAsync(item => item.Id == stock.Id)).QuantityOnHand);
        Assert.Equal(0m, (await context.Maintenance.SingleAsync(item => item.Id == maintenance.Id)).MaterialCostAmount);
        Assert.Equal(2, await context.OrganizationMaterialMovements.CountAsync(item => item.OperationId == detailId));
        var consumptionAfterReturn = await inventory.Consumption.SynchronizeAsync(
            worker.Id,
            organization.Id,
            maintenance.Id,
            [new MaintenanceMaterialConsumptionLine(detailId, null, "Полировка", null, null, null)]);
        Assert.Equal(0m, consumptionAfterReturn.TotalCost);
        Assert.Equal(0m, Assert.Single(consumptionAfterReturn.Details).TotalCost);
    }

    [Theory]
    [InlineData("шт", "4.000", "4 шт.")]
    [InlineData("л", "1.500", "1500 мл.")]
    public async Task InsufficientStockMessageOmitsTrailingZeros(string unit, string storedQuantity, string expected)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка количества", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        var stockCommands = inventory.StockCommands;
        var quantity = decimal.Parse(storedQuantity, System.Globalization.CultureInfo.InvariantCulture);
        var stock = await stockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Материал", Unit = unit, Quantity = quantity, UnitCost = 100
        });
        context.ChangeTracker.Clear();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => stockCommands.IssueAsync(user.Id, organization.Id,
            new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = stock.QuantityOnHand + 1 }));
        Assert.EndsWith(expected, error.Message);
        if (unit == "шт")
        {
            var fractionalError = await Assert.ThrowsAsync<InvalidOperationException>(() => stockCommands.IssueAsync(user.Id, organization.Id,
                new CreateOrganizationMaterialIssueDto { StockId = stock.Id, Quantity = 0.5m }));
            Assert.Contains("должно быть целым", fractionalError.Message);
        }
    }

    [Fact]
    public async Task IssueUsesStockCostAndAdjustmentRequiresAnAuditedReason()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка склада", OwnerUser = user };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        var stockCommands = inventory.StockCommands;
        var inventoryQuery = inventory.Query;
        var stock = await stockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Состав", Unit = "л", Quantity = 10, UnitCost = 1000
        });
        var maliciousRequest = JsonSerializer.Deserialize<CreateOrganizationMaterialIssueDto>(
            $$"""{"StockId":"{{stock.Id}}","Quantity":1000,"UnitCost":1000000}""")!;
        stock = await stockCommands.IssueAsync(user.Id, organization.Id, maliciousRequest);
        Assert.Equal(9000, stock.QuantityOnHand);
        Assert.Equal(1, stock.AverageUnitCost);
        var issue = await context.OrganizationMaterialMovements.SingleAsync(x => x.StockId == stock.Id && x.TypeCode == "issue");
        Assert.Equal(1, issue.UnitCost);
        Assert.Equal(1000, issue.TotalCost);

        await Assert.ThrowsAsync<InvalidOperationException>(() => stockCommands.AdjustAsync(user.Id, organization.Id, stock.Id,
            new AdjustOrganizationMaterialStockDto { QuantityOnHand = 9000, AverageUnitCost = 1.5m }));
        var beforeAdjustment = DateTime.UtcNow;
        stock = await stockCommands.AdjustAsync(user.Id, organization.Id, stock.Id, new AdjustOrganizationMaterialStockDto
        {
            QuantityOnHand = 9000, AverageUnitCost = 1.5m, ExpectedUpdatedAt = stock.UpdatedAt,
            Comment = "Исправление закупочной стоимости"
        });
        Assert.Equal(13500, stock.InventoryValue);
        var adjustment = await context.OrganizationMaterialMovements.SingleAsync(x => x.StockId == stock.Id && x.TypeCode == "adjustment");
        Assert.Equal(1.5m, adjustment.UnitCost);
        Assert.Equal(4500m, adjustment.TotalCost);
        Assert.Contains("Исправление закупочной стоимости", adjustment.Comment);
        Assert.Equal(user.Id, adjustment.CreatedByUserId);
        var revaluedStock = await stockCommands.AdjustAsync(user.Id, organization.Id, stock.Id, new AdjustOrganizationMaterialStockDto
        {
            QuantityOnHand = 9000, AverageUnitCost = 2m, ExpectedUpdatedAt = stock.UpdatedAt,
            Comment = "Переоценка без изменения количества"
        });
        Assert.Equal(18000, revaluedStock.InventoryValue);
        var revaluation = await context.OrganizationMaterialMovements
            .Where(x => x.StockId == stock.Id && x.TypeCode == "adjustment")
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync();
        Assert.Equal(4500m, revaluation.TotalCost);
        var snapshot = await inventoryQuery.GetAsync(user.Id, organization.Id, null, null, null, 1, 20, 1, 20, snapshotAt: beforeAdjustment);
        Assert.Equal(9000, Assert.Single(snapshot.SnapshotStocks).InventoryValue);
        var after = await inventoryQuery.GetAsync(user.Id, organization.Id, null, null, null, 1, 20, 1, 20, snapshotAt: DateTime.UtcNow);
        Assert.Equal(18000, Assert.Single(after.SnapshotStocks).InventoryValue);
    }

    [Fact]
    public async Task MaintenanceIgnoresSubmittedCostAndPreservesSavedConsumptionCost()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка расхода", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var work = new OpenCRM.Domain.Maintenance
        {
            Vehicle = vehicle, User = user,
            Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance, Details = []
        };
        context.Maintenance.Add(work);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        var stockCommands = inventory.StockCommands;
        var consumption = inventory.Consumption;
        await stockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Состав", Unit = "л", Quantity = 10, UnitCost = 1000
        });
        var detail = new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Состав", "Состав", "л", 1);
        var result = await consumption.SynchronizeAsync(user.Id, organization.Id, work.Id, [detail]);
        Assert.Equal(1000, result.TotalCost);
        Assert.Equal(1000, Assert.Single(result.Details).UnitCost);
        Assert.Single(await context.OrganizationMaterialMovements
            .Where(movement => movement.MaintenanceId == work.Id)
            .ToListAsync());
        await stockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Состав", Unit = "л", Quantity = 9, UnitCost = 2000
        });
        result = await consumption.SynchronizeAsync(user.Id, organization.Id, work.Id, [detail]);
        Assert.Equal(1000, result.TotalCost);
        Assert.Equal(1000, Assert.Single(result.Details).UnitCost);
        Assert.Single(await context.OrganizationMaterialMovements
            .Where(movement => movement.MaintenanceId == work.Id)
            .ToListAsync());
        var stock = await context.OrganizationMaterialStocks.SingleAsync(x => x.OrganizationId == organization.Id);
        Assert.Equal(18000, stock.QuantityOnHand);
        Assert.Equal(1.5m, stock.AverageUnitCost);
        var increasedDetail = detail with { Quantity = 2 };
        result = await consumption.SynchronizeAsync(user.Id, organization.Id, work.Id, [increasedDetail]);
        Assert.Equal(2500, result.TotalCost);
        Assert.Equal(1250, Assert.Single(result.Details).UnitCost);
        Assert.Equal(17000, stock.QuantityOnHand);
        var movements = await context.OrganizationMaterialMovements
            .Where(movement => movement.MaintenanceId == work.Id)
            .OrderBy(movement => movement.CreatedAt)
            .ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, movement => Assert.True(movement.Quantity < 0));

        result = await consumption.SynchronizeAsync(user.Id, organization.Id, work.Id, [detail]);
        Assert.Equal(1250, result.TotalCost);
        Assert.Equal(1250, Assert.Single(result.Details).UnitCost);
        movements = await context.OrganizationMaterialMovements
            .Where(movement => movement.MaintenanceId == work.Id)
            .ToListAsync();
        Assert.Equal(3, movements.Count);
        Assert.Equal(1000, Assert.Single(movements, movement => movement.Quantity > 0).Quantity);
        Assert.Equal(18000, stock.QuantityOnHand);
    }

    [Fact]
    public async Task MaintenanceConsumptionRejectsMissingAndForeignMaintenance()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Доступная организация", OwnerUser = user };
        var foreignOrganization = new Organization { Name = "Другая организация", OwnerUser = user };
        var foreignMaintenance = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = new Vehicle { User = user, Organization = foreignOrganization },
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Details = []
        };
        context.Maintenance.Add(foreignMaintenance);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        var consumption = inventory.Consumption;
        var details = Array.Empty<MaintenanceMaterialConsumptionLine>();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => consumption.SynchronizeAsync(
            user.Id, organization.Id, foreignMaintenance.Id, details));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => consumption.SynchronizeAsync(
            user.Id, organization.Id, Guid.NewGuid(), details));
        Assert.Empty(await context.OrganizationMaterialMovements
            .Where(item => item.OrganizationId == organization.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task MaintenanceConsumptionSkipsEmptyQuantitiesAndRejectsExcessiveBaseQuantity()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка предельного расхода", OwnerUser = user };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = new Vehicle { User = user, Organization = organization },
            User = user,
            Date = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned,
            Type = MaintenanceType.Maintenance,
            Details = []
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        var consumption = inventory.Consumption;

        var result = await consumption.SynchronizeAsync(user.Id, organization.Id, maintenance.Id,
        [
            new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Пустое количество", "Пустое количество", "л", null),
            new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Нулевое количество", "Нулевое количество", "л", 0)
        ]);

        Assert.Equal(0, result.TotalCost);
        Assert.Empty(result.Details);
        Assert.False(await context.OrganizationMaterialStocks.AnyAsync(item => item.OrganizationId == organization.Id));

        await Assert.ThrowsAsync<InvalidOperationException>(() => consumption.SynchronizeAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            [new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Превышение", "Превышение", "л", 1_000_000_000m)]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => consumption.SynchronizeAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            [new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Перевод", "Перевод", "кг", 1_000_000m)]));

        Assert.False(await context.OrganizationMaterialStocks.AnyAsync(item => item.OrganizationId == organization.Id));
        Assert.False(await context.OrganizationMaterialMovements.AnyAsync(item => item.OrganizationId == organization.Id));

        using var inventoryScope = CreateInventoryScope(context);
        await inventoryScope.StockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Материал из названия",
            Unit = "л",
            Quantity = 2,
            UnitCost = 100
        });
        var fallbackResult = await inventoryScope.Consumption.SynchronizeAsync(
            user.Id,
            organization.Id,
            maintenance.Id,
            [new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Материал из названия", null, "л", 1)]);
        Assert.Equal(100, fallbackResult.TotalCost);
    }

    [Fact]
    public async Task MaintenanceDistributesRoundedCostAcrossDetails()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "Проверка распределения стоимости", OwnerUser = user };
        var maintenance = new OpenCRM.Domain.Maintenance
        {
            Vehicle = new Vehicle { User = user, Organization = organization }, User = user,
            Date = DateTime.UtcNow, DateTimeCreation = DateTime.UtcNow,
            Status = MaintenanceStatus.Planned, Type = MaintenanceType.Maintenance, Details = []
        };
        context.Maintenance.Add(maintenance);
        await context.SaveChangesAsync();
        using var inventory = CreateInventoryScope(context);
        var stockCommands = inventory.StockCommands;
        var consumption = inventory.Consumption;
        await stockCommands.AddReceiptAsync(user.Id, organization.Id, new CreateOrganizationMaterialReceiptDto
        {
            Name = "Порошок", Unit = "кг", Quantity = 1, UnitCost = 5
        });
        var details = new[]
        {
            new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Первая", "Порошок", "г", 1),
            new MaintenanceMaterialConsumptionLine(Guid.NewGuid(), null, "Вторая", "Порошок", "г", 1)
        };

        var result = await consumption.SynchronizeAsync(user.Id, organization.Id, maintenance.Id, details);

        Assert.Equal(0.01m, result.TotalCost);
        Assert.Equal(result.TotalCost, result.Details.Sum(detail => detail.TotalCost ?? 0));
    }

    private static InventoryTestScope CreateInventoryScope(OpenCRMContext context)
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddScoped(_ => context)
            .AddScoped(_ => Mock.Of<IOrganizationSubscriptionService>())
            .AddOpenCRMCatalog()
            .AddOpenCRMInventory()
            .BuildServiceProvider();

        return new InventoryTestScope(provider);
    }

    private sealed class InventoryTestScope(ServiceProvider provider) : IDisposable
    {
        private readonly IServiceScope _scope = provider.CreateScope();

        public IStockCommandService StockCommands => this._scope.ServiceProvider.GetRequiredService<IStockCommandService>();

        public IOperationMaterialCommandService OperationCommands => this._scope.ServiceProvider.GetRequiredService<IOperationMaterialCommandService>();

        public IInventoryQueryService Query => this._scope.ServiceProvider.GetRequiredService<IInventoryQueryService>();

        public IMaintenanceConsumptionService Consumption => this._scope.ServiceProvider.GetRequiredService<IMaintenanceConsumptionService>();

        public void Dispose()
        {
            this._scope.Dispose();
            provider.Dispose();
        }
    }
}
