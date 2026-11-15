using OpenCRM.Testing;
using OpenCRM.Domain;
using OpenCRM.Domain.Enums;
using OpenCRM.Domain.Identity;
using OpenCRM.Modules.Workspace.Export1C;
using OpenCRM.Services.Interfaces.Export1C;
using OpenCRM.Services.Interfaces.Export1C.Dtos;

namespace OpenCRM.Modules.Workspace.Tests.Integration;

public sealed class Export1CTests(NotificationFixture fixture) : IClassFixture<NotificationFixture>
{
    [Fact]
    public async Task ExportTreatsBuyerIdAsTheOnlyBuyerAssociation()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "СТО с покупателями", OwnerUser = user };
        var client = new OrganizationClient { Organization = organization, Name = "Покупатель" };
        var vehicle = new Vehicle { User = user, Organization = organization, OrganizationClient = client };
        var buyerlessOrder = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc),
            CompletedDate = new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc),
            BuyerName = "Снимок без связи",
            BuyerInn = "123456789012",
            Details = [new OpenCRM.Domain.MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа", Price = 100 }]
        };
        var buyerOrder = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = buyerlessOrder.Date,
            CompletedDate = buyerlessOrder.CompletedDate,
            BuyerClientId = client.Id,
            BuyerName = client.Name,
            Details = [new OpenCRM.Domain.MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа", Price = 100 }]
        };
        context.Maintenance.AddRange(buyerlessOrder, buyerOrder);
        await context.SaveChangesAsync();

        var package = await new Export1CService(context).CreateAsync(organization.Id, new Export1COptionsDto
        {
            DateFrom = new DateOnly(2026, 10, 9),
            DestinationCode = "bp3",
            DateTo = new DateOnly(2026, 10, 9),
            IncludePayments = false,
            IncludeClients = false,
            IncludeVehicles = false,
            IncludeCatalog = false,
            IncludeMaterials = false
        });

        var exportedBuyerlessOrder = Assert.Single(package.Orders, order => order.Id == buyerlessOrder.Id);
        Assert.Null(exportedBuyerlessOrder.ClientId);
        Assert.Null(exportedBuyerlessOrder.ClientName);
        Assert.Null(exportedBuyerlessOrder.ClientInn);
        var exportedBuyerOrder = Assert.Single(package.Orders, order => order.Id == buyerOrder.Id);
        Assert.Equal(client.Id, exportedBuyerOrder.ClientId);
        Assert.Equal(client.Name, exportedBuyerOrder.ClientName);
        var orderWarnings = package.ValidationIssues.Where(issue => issue.EntityType == "order").ToList();
        var buyerlessWarning = Assert.Single(orderWarnings, issue => issue.OrderId == buyerlessOrder.Id);
        Assert.Equal("Заказ без покупателя. Проверьте, нужен ли он для загрузки в 1С.", buyerlessWarning.Message);
        Assert.DoesNotContain(orderWarnings, issue => issue.OrderId == buyerOrder.Id);
        Assert.DoesNotContain(package.MissingAccountingFields, message => message.Contains("стар", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ExportKeepsAdvanceAndCancelledPaymentsWithoutLeakingAnotherOrganization()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "СТО для выгрузки", OwnerUser = user };
        var foreignOrganization = new Organization { Name = "Другая СТО", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var foreignVehicle = new Vehicle { User = user, Organization = foreignOrganization };
        var completed = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            CompletedDate = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
            Details = [new OpenCRM.Domain.MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа", Price = 100, Quantity = 2 }]
        };
        var open = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.InProgress,
            Date = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Details = []
        };
        var foreign = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = foreignVehicle,
            Status = MaintenanceStatus.Completed,
            Date = completed.Date,
            CompletedDate = completed.CompletedDate,
            Details = []
        };
        context.Maintenance.AddRange(completed, open, foreign);
        context.MaintenancePayments.AddRange(
            new OpenCRM.Domain.MaintenancePayment
            {
                Id = Guid.NewGuid(), Maintenance = open, Amount = 40,
                CreatedAt = new DateTime(2026, 10, 4, 20, 30, 0, DateTimeKind.Utc)
            },
            new OpenCRM.Domain.MaintenancePayment
            {
                Id = Guid.NewGuid(), Maintenance = completed, Amount = 20,
                CreatedAt = new DateTime(2026, 9, 30, 1, 0, 0, DateTimeKind.Utc),
                CancelledAt = new DateTime(2026, 10, 5, 2, 0, 0, DateTimeKind.Utc),
                CancellationReason = "Ошибка"
            },
            new OpenCRM.Domain.MaintenancePayment
            {
                Id = Guid.NewGuid(), Maintenance = foreign, Amount = 90,
                CreatedAt = new DateTime(2026, 10, 5, 1, 0, 0, DateTimeKind.Utc)
            });
        await context.SaveChangesAsync();

        var package = await new Export1CService(context).CreateAsync(organization.Id, new Export1COptionsDto
        {
            DateFrom = new DateOnly(2026, 10, 5),
            DateTo = new DateOnly(2026, 10, 5),
            TimeZoneId = "Asia/Novosibirsk",
            IncludeClients = false,
            IncludeVehicles = false,
            IncludeCatalog = false,
            IncludeMaterials = false
        });

        Assert.Equal(2, package.Orders.Count);
        Assert.Equal(3, package.SchemaVersion);
        Assert.Contains(package.ValidationIssues, issue => issue.EntityType == "order_line");
        Assert.DoesNotContain(package.Orders, item => item.Id == foreign.Id);
        Assert.False(package.Orders.Single(item => item.Id == completed.Id).IsPaymentContextOnly);
        Assert.True(package.Orders.Single(item => item.Id == open.Id).IsPaymentContextOnly);
        Assert.Equal(100, package.Orders.Single(item => item.Id == completed.Id).GrossAmount);
        Assert.Equal(2, package.Payments.Count);
        Assert.Contains(package.Payments, item => item.OrderId == open.Id && item.Amount == 40);
        Assert.Equal(new DateOnly(2026, 10, 5), package.Payments.Single(item => item.OrderId == open.Id).AccountingDate);
        Assert.Contains(package.Payments, item => item.OrderId == completed.Id && item.CancelledAtUtc.HasValue);
    }

    [Fact]
    public async Task ExportKeepsCalendarOrderDateIndependentOfTimeZone()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "СТО на границе суток", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var order = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            CompletedDate = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc),
            Details = []
        };
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();

        var export1CService = new Export1CService(context);
        var previousDay = await export1CService.CreateAsync(organization.Id, new Export1COptionsDto
        {
            DateFrom = new DateOnly(2026, 10, 5),
            DateTo = new DateOnly(2026, 10, 5),
            TimeZoneId = "Pacific/Honolulu",
            IncludePayments = false,
            IncludeClients = false,
            IncludeVehicles = false,
            IncludeCatalog = false,
            IncludeMaterials = false
        });
        var nextDay = await export1CService.CreateAsync(organization.Id, new Export1COptionsDto
        {
            DateFrom = new DateOnly(2026, 10, 6),
            DateTo = new DateOnly(2026, 10, 6),
            TimeZoneId = "Pacific/Honolulu",
            IncludePayments = false,
            IncludeClients = false,
            IncludeVehicles = false,
            IncludeCatalog = false,
            IncludeMaterials = false
        });

        Assert.DoesNotContain(previousDay.Orders, item => item.Id == order.Id);
        Assert.Equal(new DateOnly(2026, 10, 6), nextDay.Orders.Single(item => item.Id == order.Id).AccountingDate);
    }

    [Fact]
    public async Task ExportDistributesDiscountAndVatAcrossSaleLinesWithoutSellingConsumedMaterials()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "СТО с НДС", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var order = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            CompletedDate = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            DiscountAmount = 30m,
            Details =
            [
                new OpenCRM.Domain.MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа А", Price = 122, VatRateCode = "22" },
                new OpenCRM.Domain.MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Работа Б", Price = 100, VatRateCode = "without_vat" },
                new OpenCRM.Domain.MaintenanceDetail
                {
                    DetailId = Guid.NewGuid(), Name = "Расход фильтра", Price = null,
                    MaterialName = "Фильтр", MaterialQuantity = 1, MaterialTotalCost = 50
                }
            ]
        };
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();

        var package = await new Export1CService(context).CreateAsync(organization.Id, new Export1COptionsDto
        {
            DateFrom = new DateOnly(2026, 10, 8),
            DestinationCode = "bp3",
            DateTo = new DateOnly(2026, 10, 8),
            IncludePayments = false,
            IncludeClients = false,
            IncludeVehicles = false,
            IncludeCatalog = false,
            IncludeMaterials = false
        });

        var exported = Assert.Single(package.Orders);
        Assert.Equal(2, exported.Lines.Count);
        Assert.Equal(222m, exported.GrossAmount);
        Assert.Equal(192m, exported.NetAmount);
        Assert.Equal(exported.NetAmount, exported.Lines.Sum(line => line.DiscountedTotalPrice));
        Assert.Equal(19.03m, exported.Lines[0].VatAmount);
        Assert.Equal(0m, exported.Lines[1].VatAmount);
        Assert.DoesNotContain(package.ValidationIssues, issue => issue.EntityType == "order_line");
    }

    [Fact]
    public async Task ExportUsesVersionThreeForExplicitlyVatFreeOrders()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "СТО без НДС", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        context.Maintenance.Add(new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            CompletedDate = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            Details = [new OpenCRM.Domain.MaintenanceDetail
            {
                DetailId = Guid.NewGuid(), Name = "Работа", Price = 100, VatRateCode = MaintenanceVatRates.WithoutVat
            }]
        });
        await context.SaveChangesAsync();

        var package = await new Export1CService(context).CreateAsync(organization.Id, new Export1COptionsDto
        {
            DateFrom = new DateOnly(2026, 10, 8),
            DateTo = new DateOnly(2026, 10, 8),
            IncludePayments = false,
            IncludeClients = false,
            IncludeVehicles = false,
            IncludeCatalog = false,
            IncludeMaterials = false
        });

        Assert.Equal(3, package.SchemaVersion);
        Assert.DoesNotContain(package.ValidationIssues, issue => issue.EntityType == "order_line");
    }

    [Fact]
    public async Task ExportWarnsWhenFullDiscountPreventsImportingARealization()
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "СТО с полной скидкой", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var order = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            CompletedDate = new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc),
            DiscountAmount = 100m,
            Details = [new OpenCRM.Domain.MaintenanceDetail
            {
                DetailId = Guid.NewGuid(), Name = "Работа", Price = 100, VatRateCode = MaintenanceVatRates.WithoutVat
            }]
        };
        context.Maintenance.Add(order);
        await context.SaveChangesAsync();

        var package = await new Export1CService(context).CreateAsync(organization.Id, new Export1COptionsDto
        {
            DestinationCode = "bp3",
            DateFrom = new DateOnly(2026, 10, 8),
            DateTo = new DateOnly(2026, 10, 8),
            IncludePayments = false,
            IncludeClients = false,
            IncludeVehicles = false,
            IncludeCatalog = false,
            IncludeMaterials = false
        });

        Assert.Contains(package.ValidationIssues, issue => issue.OrderId == order.Id
            && issue.Message.Contains("больше нуля"));
    }

    [Theory]
    [InlineData("full", false)]
    [InlineData("bp3", true)]
    public async Task AccountingWarningsDependOnDestinationWithoutDroppingUniversalData(string destination, bool hasWarnings)
    {
        await using var context = fixture.CreateContext();
        var user = new ApplicationUser();
        var organization = new Organization { Name = "QA универсальный экспорт", OwnerUser = user };
        var vehicle = new Vehicle { User = user, Organization = organization };
        var date = new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc);
        var order = new OpenCRM.Domain.Maintenance
        {
            User = user,
            Vehicle = vehicle,
            Status = MaintenanceStatus.Completed,
            Date = date,
            CompletedDate = date,
            Details = [new MaintenanceDetail { DetailId = Guid.NewGuid(), Name = "Бесплатная работа", Price = 0, VatRateCode = MaintenanceVatRates.WithoutVat }]
        };
        context.Maintenance.Add(order);
        context.MaintenancePayments.Add(new MaintenancePayment
        {
            Id = Guid.NewGuid(), Maintenance = order, Amount = 25, MethodCode = "other", CreatedAt = date
        });
        await context.SaveChangesAsync();

        var package = await new Export1CService(context).CreateAsync(organization.Id, new Export1COptionsDto
        {
            DestinationCode = destination,
            DateFrom = new DateOnly(2026, 10, 30),
            DateTo = new DateOnly(2026, 10, 30)
        });

        Assert.Equal(order.Id, Assert.Single(package.Orders).Id);
        Assert.Equal("other", Assert.Single(package.Payments).MethodCode);
        Assert.Equal(hasWarnings, package.ValidationIssues.Count > 0);
        Assert.Equal(hasWarnings, package.MissingAccountingFields.Count > 0);
        Assert.All(package.ValidationIssues, issue => Assert.Contains("1С", issue.Message));
    }
}
