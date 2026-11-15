using OpenCRM.Modules.Imports;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports.Tests.Unit;

public sealed class ImportMappingBusinessRecordTests
{
    [Fact]
    public void SuggestSelectsBusinessKindsOnlyFromDistinctHeaderSets()
    {
        var tables = new List<ImportSourceTable>
        {
            CreateTable("orders", "Order ID", "Vehicle ID", "Order Status", "Date", "Order Type", "Order Total"),
            CreateTable("lines", "Order ID", "Service Name", "Quantity", "Unit Price"),
            CreateTable("payments", "Order ID", "Payment Method", "Payment Amount", "Date", "Cancelled At"),
            CreateTable("history", "Order ID", "Event Type", "Date", "Actor Name", "Description")
        };

        var mappings = ImportMappingBuilder.Suggest(tables);

        Assert.Equal(["order", "order-line", "payment", "history"], mappings.Select(mapping => mapping.Kind));
        Assert.Equal([5], mappings[0].Fields["amount"]);
        Assert.Equal([3], mappings[1].Fields["unitPrice"]);
        Assert.Equal([2], mappings[3].Fields["occurredAt"]);
    }

    [Fact]
    public void BuildPreservesOrderAndVehicleExternalReferences()
    {
        var tables = new List<ImportSourceTable>
        {
            new()
            {
                Id = "orders",
                Name = "Заказы",
                Columns = ["Order ID", "Vehicle ID", "Order Status", "Date", "Order Type", "Order Total"],
                Rows = [new ImportSourceRow { Number = 2, Values = ["ord-1", "car-1", "completed", "2025-04-01", "repair", "120.00"] }]
            },
            new()
            {
                Id = "lines",
                Name = "Работы",
                Columns = ["Order ID", "Service Name", "Quantity", "Unit Price"],
                Rows = [new ImportSourceRow { Number = 2, Values = ["ord-1", "Замена масла", "2", "65.00"] }]
            }
        };
        var mappings = ImportMappingBuilder.Suggest(tables);

        var rows = ImportMappingBuilder.Build(tables, mappings);

        var order = Assert.Single(rows, row => row.Kind == "order");
        Assert.Equal("ord-1", order.ExternalId);
        Assert.Equal("car-1", order.VehicleExternalId);
        var line = Assert.Single(rows, row => row.Kind == "order-line");
        Assert.Equal("ord-1", line.OrderExternalId);
        Assert.Equal("2", line.Quantity);
        Assert.Equal("65.00", line.UnitPrice);
    }

    [Fact]
    public void SuggestLeavesAmbiguousAmountsUnmapped()
    {
        var table = CreateTable("payments", "Order ID", "Payment Method", "Amount", "Payment Amount");

        var mapping = Assert.Single(ImportMappingBuilder.Suggest([table]));

        Assert.Equal("payment", mapping.Kind);
        Assert.False(mapping.Fields.ContainsKey("amount"));
    }

    private static ImportSourceTable CreateTable(string id, params string[] columns) => new()
    {
        Id = id,
        Name = id,
        Columns = [.. columns]
    };
}
