using OpenCRM.Modules.Imports;
using OpenCRM.Services.Interfaces.Imports;

namespace OpenCRM.Modules.Imports.Tests.Unit;

public sealed class ImportReconciliationTests
{
    [Fact]
    public void SummarySeparatesNewMoneyFromCancelledExcludedAndDuplicateOperations()
    {
        var summary = ImportReconciliationBuilder.Build([
            new() { Kind = "order", ExternalId = "o1", Status = "create", OrderStatus = "completed", Amount = "1500", DiscountAmount = "500" },
            new() { Kind = "payment", OrderExternalId = " O1 ", Status = "create", Amount = "400" },
            new() { Kind = "payment", OrderExternalId = "o1", Status = "create", Amount = "200", CancelledAt = "2020-01-01" },
            new() { Kind = "payment", OrderExternalId = "o1", Status = "duplicate", Amount = "300" },
            new() { Kind = "order", ExternalId = "o2", Status = "create", OrderStatus = "cancelled", Amount = "9000" },
            new() { Kind = "order", ExternalId = "o3", Status = "duplicate", OrderStatus = "completed", Amount = "9000" },
            new() { Kind = "order", ExternalId = "o4", Status = "create", OrderStatus = "completed", Amount = "9000", Excluded = true },
            new() { Kind = "client", Status = "create" },
            new() { Kind = "client", Status = "link" },
            new() { Kind = "vehicle", Status = "duplicate" }
        ]);

        Assert.Equal(1500m, summary.OrderTotal);
        Assert.Equal(400m, summary.ActivePaymentsTotal);
        Assert.Equal(1100m, summary.OutstandingTotal);
        Assert.Equal(2, summary.NewOrders);
        Assert.Equal(2, summary.NewPayments);
        Assert.Equal(1, summary.NewClients);
        Assert.Equal(1, summary.LinkedClients);
        Assert.Equal(1, summary.ExistingVehicles);
    }
}
