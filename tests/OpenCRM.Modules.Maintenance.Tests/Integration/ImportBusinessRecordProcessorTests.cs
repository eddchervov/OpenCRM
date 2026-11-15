using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Infrastructure.Data;
using OpenCRM.Modules.Maintenance.Contracts.Imports;
using OpenCRM.Modules.Maintenance.Imports;

namespace OpenCRM.Modules.Maintenance.Tests.Integration;

public sealed class ImportBusinessRecordProcessorTests(MaintenanceDatabaseFixture fixture) : IClassFixture<MaintenanceDatabaseFixture>
{
    [Fact]
    public async Task AppliesCompletedOrderAggregateWithoutDomainSideEffects()
    {
        var (user, organization, vehicle) = await this.CreateVehicleAsync();
        await using var context = fixture.CreateContext();
        var processor = new ImportBusinessRecordProcessor(context);
        var records = CreateValidRecords();
        records.Single(record => record.Kind == "order-line").TargetId = null;
        var orderRecord = records.Single(record => record.Kind == "order");
        orderRecord.ClientExternalId = "client-1";
        orderRecord.PartyName = "Покупатель в исходном заказе";
        records.Single(record => record.Kind == "payment").PartyName = "Плательщик в исходной оплате";
        var vehicleIds = new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id };
        var clientIds = new Dictionary<string, Guid> { ["client-1"] = vehicle.OrganizationClientId!.Value };
        var orderIds = new Dictionary<string, Guid>();

        await processor.ValidateAsync(user.Id, organization.Id, records, vehicleIds, clientIds, orderIds, default);
        Assert.All(records, record => Assert.Equal("create", record.Status));
        var result = await processor.ApplyAsync(user.Id, organization.Id, records, vehicleIds, clientIds, orderIds, default);
        await context.SaveChangesAsync();

        Assert.Equal(1, result.CreatedOrders);
        Assert.Equal(1, result.CreatedOrderLines);
        Assert.Equal(1, result.CreatedPayments);
        Assert.Equal(2, result.CreatedHistoryEvents);
        Assert.Equal(4, result.RowTargets.Count);
        var importedLine = Assert.Single(records, record => record.Kind == "order-line");
        Assert.True(importedLine.TargetId.HasValue);
        Assert.Equal(importedLine.TargetId.Value, result.RowTargets[importedLine.Id]);
        var order = await context.Maintenance
            .Include(item => item.Details)
            .Include(item => item.HistoryEvents)
            .SingleAsync(item => item.Id == orderIds["order-1"]);
        var payment = await context.MaintenancePayments.SingleAsync(item => item.MaintenanceId == order.Id);
        Assert.Equal(MaintenanceStatus.Completed, order.Status);
        Assert.Equal(vehicle.Id, order.VehicleId);
        Assert.Equal(130m, Assert.Single(order.Details).Price);
        Assert.Equal(10m, order.DiscountAmount);
        Assert.Equal(120m, order.PaidAmount);
        Assert.Equal(vehicle.OrganizationClientId, order.BuyerClientId);
        Assert.Equal("Покупатель в исходном заказе", order.BuyerName);
        Assert.Equal(120m, payment.Amount);
        Assert.Equal("cash", payment.MethodCode);
        Assert.Equal(user.Id, payment.CreatedBy);
        Assert.Null(payment.PayerClientId);
        Assert.Equal("Плательщик в исходной оплате", payment.PayerName);
        Assert.Contains("Кассир", payment.Comment);
        Assert.Equal(2, order.HistoryEvents.Count);
        var importedHistory = Assert.Single(order.HistoryEvents, item => item.PayloadJson?.Contains("service_completed") == true);
        Assert.Equal("imported-history", importedHistory.EventType);
        Assert.Equal("История из прежней CRM: service_completed", importedHistory.Title);
        Assert.Equal("Импортированный акт", importedHistory.Description);
        Assert.Equal("service_completed", JsonDocument.Parse(importedHistory.PayloadJson!).RootElement
            .GetProperty("sourceEventType").GetString());
        Assert.Contains(order.HistoryEvents, item => item.Title == "Заказ импортирован из прежней CRM");
        Assert.Empty(await context.Set<MaintenanceWorkOperation>().ToListAsync());
        Assert.Empty(await context.Set<OrganizationMaterialMovement>().ToListAsync());
    }

    [Fact]
    public async Task RejectsActiveStatusAndPaymentsAboveOrderTotal()
    {
        var (user, organization, vehicle) = await this.CreateVehicleAsync();
        await using var context = fixture.CreateContext();
        var processor = new ImportBusinessRecordProcessor(context);
        var records = CreateValidRecords();
        records.Single(record => record.Kind == "order").OrderStatus = "inprogress";
        records.Single(record => record.Kind == "payment").Amount = "121.00";

        await processor.ValidateAsync(user.Id, organization.Id, records,
            new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id }, new Dictionary<string, Guid>(),
            new Dictionary<string, Guid>(), default);

        var order = Assert.Single(records, record => record.Kind == "order");
        Assert.Equal("error", order.Status);
        Assert.Contains("completed и cancelled", order.Message);
        Assert.Contains("превышает итог заказа", order.Message);
        Assert.False(await context.Maintenance.AnyAsync(item => item.VehicleId == vehicle.Id));
    }

    [Fact]
    public async Task RejectsMoneyOutsideStorageAndMaintenanceLimitsWithoutOverflowingValidation()
    {
        var (user, organization, vehicle) = await this.CreateVehicleAsync();
        await using var context = fixture.CreateContext();
        var processor = new ImportBusinessRecordProcessor(context);
        var records = new List<ImportBusinessRecord>();
        records.AddRange(CreateFinancialRecords("large-unit", ["10000000000000000.00"], []));
        records.AddRange(CreateFinancialRecords("line-total", ["9999999999999999.99", "0.01"], []));
        records.AddRange(CreateFinancialRecords("payment-total", ["9999999999999999.99"], ["9999999999999999.99", "0.01"]));
        records.AddRange(CreateFinancialRecords("large-payment", ["1.00"], ["10000000000000000.00"]));

        await processor.ValidateAsync(user.Id, organization.Id, records,
            new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id }, new Dictionary<string, Guid>(),
            new Dictionary<string, Guid>(), default);

        Assert.Contains(records, record => record.Id == "large-unit:line:0" && record.Status == "error");
        Assert.Contains(records, record => record.Id == "line-total:line:0" && record.Status == "error" &&
            record.Message.Contains("Стоимость услуги", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Id == "payment-total:payment:1" && record.Status == "error" &&
            record.Message.Contains("денежный диапазон", StringComparison.Ordinal));
        Assert.Contains(records, record => record.Id == "large-payment:payment:0" && record.Status == "error");
    }

    [Fact]
    public async Task RejectsOrderAggregateWithMoreThanOneThousandChildren()
    {
        var (user, organization, vehicle) = await this.CreateVehicleAsync();
        await using var context = fixture.CreateContext();
        var processor = new ImportBusinessRecordProcessor(context);
        var order = new ImportBusinessRecord
        {
            Id = "orders:2:order",
            Kind = "order",
            ExternalId = "order-1",
            VehicleExternalId = "vehicle-1",
            OccurredAt = "2025-04-15",
            OrderStatus = "completed",
            OrderType = "repair"
        };
        var records = new List<ImportBusinessRecord> { order };
        records.AddRange(Enumerable.Range(0, 1001).Select(index => new ImportBusinessRecord
        {
            Id = $"lines:{index + 2}:order-line",
            Kind = "order-line",
            OrderExternalId = "order-1",
            DetailName = "Работа",
            Quantity = "1",
            UnitPrice = "1.00"
        }));

        await processor.ValidateAsync(user.Id, organization.Id, records,
            new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id }, new Dictionary<string, Guid>(),
            new Dictionary<string, Guid>(), default);

        Assert.Equal("error", order.Status);
        Assert.Contains("больше 1000 дочерних строк", order.Message);
        Assert.All(records.Where(record => record.Kind == "order-line"), record => Assert.Equal("error", record.Status));
    }

    [Fact]
    public async Task ApplyRejectsVehicleFromAnotherOrganizationAndIsIdempotentByJournalIds()
    {
        var (user, organization, vehicle) = await this.CreateVehicleAsync();
        var (_, foreignOrganization, foreignVehicle) = await this.CreateVehicleAsync();
        await using var context = fixture.CreateContext();
        var processor = new ImportBusinessRecordProcessor(context);
        var records = CreateValidRecords();
        var orderIds = new Dictionary<string, Guid>();
        var clientIds = new Dictionary<string, Guid>();
        context.Attach(vehicle);
        vehicle.IsDeleted = true;
        await context.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ApplyAsync(user.Id, organization.Id, records,
            new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id }, clientIds, orderIds, default));
        vehicle.IsDeleted = false;
        await context.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ApplyAsync(user.Id, organization.Id, records,
            new Dictionary<string, Guid> { ["vehicle-1"] = foreignVehicle.Id }, clientIds, orderIds, default));

        records.RemoveAll(record => record.Kind == "history");
        var vehicleIds = new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id };
        var result = await processor.ApplyAsync(user.Id, organization.Id, records, vehicleIds, clientIds, orderIds, default);
        var repeated = await processor.ApplyAsync(user.Id, organization.Id, records, vehicleIds, clientIds, orderIds, default);

        Assert.Equal(1, result.CreatedOrders);
        Assert.Equal(1, result.CreatedHistoryEvents);
        Assert.Equal(0, repeated.CreatedOrders);
        Assert.Single(orderIds);
        Assert.NotEqual(foreignOrganization.Id, organization.Id);
    }

    [Fact]
    public async Task RequiresHistoricalPartyNameAndRejectsClientFromAnotherOrganization()
    {
        var (user, organization, vehicle) = await this.CreateVehicleAsync();
        var (_, _, foreignVehicle) = await this.CreateVehicleAsync();
        await using var context = fixture.CreateContext();
        var processor = new ImportBusinessRecordProcessor(context);
        var records = CreateValidRecords();
        var order = records.Single(record => record.Kind == "order");
        order.ClientExternalId = "foreign-client";
        var vehicleIds = new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id };
        var foreignClientIds = new Dictionary<string, Guid> { ["foreign-client"] = foreignVehicle.OrganizationClientId!.Value };

        await processor.ValidateAsync(user.Id, organization.Id, records, vehicleIds, foreignClientIds,
            new Dictionary<string, Guid>(), default);

        Assert.Equal("error", order.Status);
        Assert.Contains("исходной записи", order.Message);
        order.PartyName = "Исходное имя покупателя";
        order.Status = "create";

        await Assert.ThrowsAsync<InvalidDataException>(() => processor.ApplyAsync(user.Id, organization.Id,
            records, vehicleIds, foreignClientIds, new Dictionary<string, Guid>(), default));
    }

    [Theory]
    [InlineData("10001", "0", "Услуга")]
    [InlineData("1", "1000000000", "Услуга")]
    [InlineData("1", "100", "<script>Услуга</script>")]
    public async Task DetailPreviewUsesExistingMaintenanceRules(string quantity, string unitPrice, string name)
    {
        var (user, organization, vehicle) = await this.CreateVehicleAsync();
        await using var context = fixture.CreateContext();
        var records = CreateValidRecords();
        var line = records.Single(record => record.Kind == "order-line");
        line.Quantity = quantity;
        line.UnitPrice = unitPrice;
        line.DetailName = name;
        var processor = new ImportBusinessRecordProcessor(context);

        await processor.ValidateAsync(user.Id, organization.Id, records,
            new Dictionary<string, Guid> { ["vehicle-1"] = vehicle.Id }, new Dictionary<string, Guid>(),
            new Dictionary<string, Guid>(), default);

        Assert.Equal("error", line.Status);
    }

    private async Task<(ApplicationUser User, Organization Organization, Vehicle Vehicle)> CreateVehicleAsync()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser { UserName = $"import-{Guid.NewGuid():N}" };
        var organization = new Organization
        {
            Name = "Исторический импорт",
            OwnerUser = user,
            PrimaryScenarioCode = OrganizationTypeCodes.ServiceStation,
            Members = [new OrganizationMember { User = user, RoleCode = OrganizationRoleCodes.Owner }]
        };
        var client = new OrganizationClient { Organization = organization, Name = "Владелец автомобиля" };
        var vehicle = new Vehicle
        {
            User = user,
            Organization = organization,
            OrganizationClient = client,
            Name = "Тестовый автомобиль",
            Vin = $"VIN{Guid.NewGuid():N}"[..17],
            RegistrationPlate = $"IMP{Guid.NewGuid():N}"[..9]
        };
        context.Vehicles.Add(vehicle);
        await context.SaveChangesAsync();

        return (user, organization, vehicle);
    }

    private static List<ImportBusinessRecord> CreateValidRecords()
    {
        return
        [
            new ImportBusinessRecord
            {
                Id = "orders:2:order", TargetId = Guid.NewGuid(), Kind = "order", ExternalId = "order-1",
                VehicleExternalId = "vehicle-1", OccurredAt = "2025-04-15", OrderStatus = "completed",
                OrderType = "repair", DiscountAmount = "10.00", Amount = "120.00"
            },
            new ImportBusinessRecord
            {
                Id = "lines:2:order-line", TargetId = Guid.NewGuid(), Kind = "order-line", OrderExternalId = "order-1",
                DetailName = "Замена масла", Quantity = "2", UnitPrice = "65.00"
            },
            new ImportBusinessRecord
            {
                Id = "payments:2:payment", TargetId = Guid.NewGuid(), Kind = "payment", OrderExternalId = "order-1",
                Amount = "120.00", OccurredAt = "2025-04-15T10:15:00Z", PaymentMethod = "cash", ActorName = "Кассир"
            },
            new ImportBusinessRecord
            {
                Id = "history:2:history", TargetId = Guid.NewGuid(), Kind = "history", OrderExternalId = "order-1",
                OccurredAt = "2025-04-15T10:30:00Z", EventType = "service_completed", HistoryComment = "Импортированный акт"
            }
        ];
    }

    private static List<ImportBusinessRecord> CreateFinancialRecords(
        string externalId,
        IReadOnlyList<string> unitPrices,
        IReadOnlyList<string> paymentAmounts)
    {
        var records = new List<ImportBusinessRecord>
        {
            new()
            {
                Id = $"{externalId}:order",
                Kind = "order",
                ExternalId = externalId,
                VehicleExternalId = "vehicle-1",
                OccurredAt = "2025-04-15",
                OrderStatus = "completed",
                OrderType = "repair"
            }
        };
        records.AddRange(unitPrices.Select((price, index) => new ImportBusinessRecord
        {
            Id = $"{externalId}:line:{index}",
            Kind = "order-line",
            OrderExternalId = externalId,
            DetailName = "Работа",
            Quantity = "1",
            UnitPrice = price
        }));
        records.AddRange(paymentAmounts.Select((amount, index) => new ImportBusinessRecord
        {
            Id = $"{externalId}:payment:{index}",
            Kind = "payment",
            OrderExternalId = externalId,
            Amount = amount,
            OccurredAt = "2025-04-15T10:15:00Z"
        }));

        return records;
    }
}
